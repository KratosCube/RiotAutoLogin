using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services
{
    public static class LCUService
    {
        private static string[] _leagueAuth;
        private static int _lcuPid = 0;
        private static bool _isLeagueOpen = false;
        private static CancellationTokenSource _autoAcceptCts;
        private static bool _isAutoAcceptActive = false;

        public static bool IsLeagueOpen => _isLeagueOpen;
        public static bool IsAutoAcceptActive => _isAutoAcceptActive;

        // Start auto-accept process
        public static void StartAutoAccept()
        {
            if (_isAutoAcceptActive)
                return;

            _isAutoAcceptActive = true;
            _autoAcceptCts = new CancellationTokenSource();

            // Start the monitoring task
            Task.Run(() => AcceptQueueAsync(_autoAcceptCts.Token));
        }

        // Stop auto-accept process
        public static void StopAutoAccept()
        {
            _isAutoAcceptActive = false;
            _autoAcceptCts?.Cancel();
            _autoAcceptCts?.Dispose();
            _autoAcceptCts = null;
        }

        // Check if League Client is open and get authentication details
        public static bool CheckIfLeagueClientIsOpen()
        {
            try
            {
                Process client = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault();
                if (client != null)
                {
                    _leagueAuth = GetLeagueAuth(client);
                    _isLeagueOpen = true;

                    if (_lcuPid != client.Id)
                    {
                        _lcuPid = client.Id;
                        Debug.WriteLine("League Client found. Process ID: " + _lcuPid);
                    }

                    return true;
                }
                else
                {
                    _isLeagueOpen = false;
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error checking for League Client: " + ex.Message);
                _isLeagueOpen = false;
                return false;
            }
        }

        // Main loop for accepting queue
        private static async Task AcceptQueueAsync(CancellationToken cancellationToken)
        {
            Debug.WriteLine("Auto-accept started. Monitoring for match...");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!CheckIfLeagueClientIsOpen())
                    {
                        await Task.Delay(2000, cancellationToken);
                        continue;
                    }

                    string[] gameSession = ClientRequest("GET", "lol-gameflow/v1/session");

                    if (gameSession[0] == "200")
                    {
                        string phase = gameSession[1].Split("phase").Last().Split('"')[2];

                        switch (phase)
                        {
                            case "ReadyCheck":
                                // Auto-accept match
                                string[] acceptResult = ClientRequest("POST", "lol-matchmaking/v1/ready-check/accept");
                                if (acceptResult[0].StartsWith("2"))
                                {
                                    Debug.WriteLine("Match accepted successfully!");
                                }
                                else
                                {
                                    Debug.WriteLine($"Failed to accept match. Status: {acceptResult[0]}");
                                }
                                break;

                            case "Lobby":
                            case "Matchmaking":
                            case "ChampSelect":
                            case "InProgress":
                            case "WaitingForStats":
                            case "PreEndOfGame":
                            case "EndOfGame":
                                // No need to do anything during these phases
                                await Task.Delay(2000, cancellationToken);
                                break;

                            default:
                                await Task.Delay(1000, cancellationToken);
                                break;
                        }
                    }
                    else
                    {
                        await Task.Delay(2000, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Cancellation requested
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in auto-accept monitor: {ex.Message}");
                    await Task.Delay(2000, cancellationToken);
                }
            }

            Debug.WriteLine("Auto-accept stopped");
        }

        // Get authentication details from the League Client process
        private static string[] GetLeagueAuth(Process client)
        {
            string query = $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {client.Id}";
            string commandLine = string.Empty;

            using (var searcher = new ManagementObjectSearcher(query))
            using (var results = searcher.Get())
            {
                foreach (var result in results)
                {
                    commandLine = result["CommandLine"]?.ToString();
                    break;
                }
            }

            // Parse the port and auth token
            string port = Regex.Match(commandLine, @"--app-port=""?(\d+)""?").Groups[1].Value;
            string authToken = Regex.Match(commandLine, @"--remoting-auth-token=([a-zA-Z0-9_-]+)").Groups[1].Value;

            // Compute the encoded key
            string auth = "riot:" + authToken;
            string authBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(auth));

            return new string[] { authBase64, port };
        }

        // Make a request to the League Client API
        public static string[] ClientRequest(string method, string url, string body = null)
        {
            // Ignore invalid https
            var handler = new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            try
            {
                using (HttpClient client = new HttpClient(handler))
                {
                    // Set URL
                    client.BaseAddress = new Uri("https://127.0.0.1:" + _leagueAuth[1] + "/");
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", _leagueAuth[0]);

                    // Set headers
                    HttpRequestMessage request = new HttpRequestMessage(new HttpMethod(method), url);

                    // Send POST data when doing a post request
                    if (!string.IsNullOrEmpty(body))
                    {
                        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    }

                    // Get the response
                    HttpResponseMessage response = client.SendAsync(request).Result;

                    // If the response is null (League client closed?)
                    if (response == null)
                    {
                        return new string[] { "999", "" };
                    }

                    // Get the HTTP status code
                    int statusCode = (int)response.StatusCode;
                    string statusString = statusCode.ToString();

                    // Get the body
                    string responseFromServer = response.Content.ReadAsStringAsync().Result;

                    // Clean up the response
                    response.Dispose();

                    // Return content
                    return new string[] { statusString, responseFromServer };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ClientRequest: {ex.Message}");
                // If the URL is invalid (League client closed?)
                return new string[] { "999", ex.Message };
            }
        }

        public static async Task<List<ChampionModel>> GetChampionsAsync()
        {
            try
            {
                if (!CheckIfLeagueClientIsOpen())
                {
                    Debug.WriteLine("League client not open, returning empty champion list");
                    return new List<ChampionModel>();
                }

                string currentSummonerId = await GetCurrentSummonerIdAsync();
                if (string.IsNullOrEmpty(currentSummonerId))
                {
                    Debug.WriteLine("Could not get summoner ID, returning empty champion list");
                    return new List<ChampionModel>();
                }

                string[] ownedChamps = ClientRequest("GET", $"lol-champions/v1/inventories/{currentSummonerId}/champions-minimal");
                if (ownedChamps[0] != "200")
                {
                    Debug.WriteLine($"Failed to get champions, status: {ownedChamps[0]}");
                    return new List<ChampionModel>();
                }

                List<ChampionModel> champions = new List<ChampionModel>();

                // Parse JSON response to extract champion data
                using (JsonDocument doc = JsonDocument.Parse(ownedChamps[1]))
                {
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        try
                        {
                            string name = element.GetProperty("name").GetString();
                            int id = element.GetProperty("id").GetInt32();
                            bool owned = element.GetProperty("owned").GetBoolean();
                            bool freeToPlay = false;
                            bool freeToPlayForNewPlayers = false;

                            if (element.TryGetProperty("freeToPlay", out var freeToPlayProp))
                                freeToPlay = freeToPlayProp.GetBoolean();

                            if (element.TryGetProperty("freeToPlayForNewPlayers", out var freeToPlayForNewPlayersProp))
                                freeToPlayForNewPlayers = freeToPlayForNewPlayersProp.GetBoolean();

                            // Skip the "None" champion
                            if (name == "None")
                                continue;

                            // Fix Nunu & Willump to just "Nunu" to be consistent
                            if (name == "Nunu & Willump")
                                name = "Nunu";

                            bool isAvailable = owned || freeToPlay || freeToPlayForNewPlayers;

                            champions.Add(new ChampionModel
                            {
                                Name = name,
                                Id = id,
                                IsAvailable = isAvailable
                            });
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error parsing champion: {ex.Message}");
                        }
                    }
                }

                return champions.OrderBy(c => c.Name).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching champions: {ex.Message}");
                return new List<ChampionModel>();
            }
        }

        public static async Task<List<SummonerSpellModel>> GetSummonerSpellsAsync()
        {
            try
            {
                if (!CheckIfLeagueClientIsOpen())
                {
                    Debug.WriteLine("League client not open, using fallback spells");
                    return GetHardcodedSpells();
                }

                string[] spellsJson = ClientRequest("GET", "lol-game-data/assets/v1/summoner-spells.json");
                Debug.WriteLine($"Summoner spell request result: {spellsJson[0]}");

                if (spellsJson[0] != "200")
                {
                    Debug.WriteLine($"Failed to get summoner spells from LCU, status: {spellsJson[0]}");
                    return GetHardcodedSpells();
                }

                // Log the response to diagnose issues
                Debug.WriteLine($"Response length: {spellsJson[1]?.Length ?? 0}");

                List<SummonerSpellModel> spells = new List<SummonerSpellModel>();

                // Parse JSON response to extract spell data
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(spellsJson[1]))
                    {
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            try
                            {
                                string name = element.GetProperty("name").GetString();
                                int id = element.GetProperty("id").GetInt32();
                                string description = "";

                                if (element.TryGetProperty("description", out var descProp))
                                    description = descProp.GetString();

                                spells.Add(new SummonerSpellModel
                                {
                                    Name = name,
                                    Id = id,
                                    Description = description
                                });

                                Debug.WriteLine($"Added spell from LCU: {name} (ID: {id})");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error parsing individual spell: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error parsing spells JSON: {ex.Message}");
                    return GetHardcodedSpells();
                }

                if (spells.Count == 0)
                {
                    Debug.WriteLine("No spells parsed from LCU, using fallback");
                    return GetHardcodedSpells();
                }

                return spells.OrderBy(s => s.Name).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching summoner spells: {ex.Message}");
                return GetHardcodedSpells();
            }
        }
        private static List<SummonerSpellModel> GetHardcodedSpells()
        {
            var spells = new List<SummonerSpellModel>
    {
        new SummonerSpellModel { Name = "Flash", Id = 4, Description = "Teleports your champion a short distance." },
        new SummonerSpellModel { Name = "Ignite", Id = 14, Description = "Ignites target enemy champion, dealing true damage." },
        new SummonerSpellModel { Name = "Heal", Id = 7, Description = "Restores health to your champion and an ally." },
        new SummonerSpellModel { Name = "Teleport", Id = 12, Description = "Teleports to target allied structure or minion." },
        new SummonerSpellModel { Name = "Exhaust", Id = 3, Description = "Slows an enemy champion and reduces their damage." },
        new SummonerSpellModel { Name = "Barrier", Id = 21, Description = "Shields your champion from damage." },
        new SummonerSpellModel { Name = "Cleanse", Id = 1, Description = "Removes all disables and summoner spell debuffs." },
        new SummonerSpellModel { Name = "Smite", Id = 11, Description = "Deals true damage to target monster or minion." },
        new SummonerSpellModel { Name = "Ghost", Id = 6, Description = "Your champion can move through units and gains increased Movement Speed." },
        new SummonerSpellModel { Name = "Clarity", Id = 13, Description = "Restores mana to you and nearby allies." }
    };

            Debug.WriteLine($"Using {spells.Count} hardcoded spells");
            return spells;
        }
        private static async Task<string> GetCurrentSummonerIdAsync()
        {
            try
            {
                string[] currentSummoner = ClientRequest("GET", "lol-summoner/v1/current-summoner");
                if (currentSummoner[0] == "200")
                {
                    // Log the full response to diagnose
                    Debug.WriteLine("Full summoner JSON: " + currentSummoner[1]);

                    using (JsonDocument doc = JsonDocument.Parse(currentSummoner[1]))
                    {
                        var summonerIdProperty = doc.RootElement.GetProperty("summonerId");

                        // Check the value kind and handle accordingly
                        if (summonerIdProperty.ValueKind == JsonValueKind.Number)
                        {
                            return summonerIdProperty.GetInt64().ToString();
                        }
                        else if (summonerIdProperty.ValueKind == JsonValueKind.String)
                        {
                            return summonerIdProperty.GetString();
                        }
                        else
                        {
                            Debug.WriteLine($"Unexpected summonerId type: {summonerIdProperty.ValueKind}");
                            return string.Empty;
                        }
                    }
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting summoner ID: {ex.Message}");
                return string.Empty;
            }
        }

        // Champion selection and banning methods
        public static bool SelectChampion(int championId, string actId, bool complete = false)
        {
            try
            {
                string body = complete ? $"{{\"championId\":{championId},\"completed\":true}}" : $"{{\"championId\":{championId}}}";
                string[] response = ClientRequest("PATCH", $"lol-champ-select/v1/session/actions/{actId}", body);
                return response[0] == "204";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error selecting champion: {ex.Message}");
                return false;
            }
        }

        public static bool SelectSummonerSpell(int spellId, int slot)
        {
            try
            {
                string body = $"{{\"spell{slot}Id\":{spellId}}}";
                string[] response = ClientRequest("PATCH", "lol-champ-select/v1/session/my-selection", body);
                return response[0] == "204";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error selecting summoner spell: {ex.Message}");
                return false;
            }
        }

        // Game session monitoring
        public static async Task<string> GetCurrentGamePhaseAsync()
        {
            try
            {
                string[] gameSession = ClientRequest("GET", "lol-gameflow/v1/session");
                if (gameSession[0] == "200")
                {
                    using (JsonDocument doc = JsonDocument.Parse(gameSession[1]))
                    {
                        if (doc.RootElement.TryGetProperty("phase", out var phaseProp))
                            return phaseProp.GetString();
                    }
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting game phase: {ex.Message}");
                return string.Empty;
            }
        }

        // Get champion select session to check pick/ban turns
        public static async Task<(bool isMyTurn, string actId, string actionType)> GetChampSelectTurnAsync()
        {
            try
            {
                string[] champSelectSession = ClientRequest("GET", "lol-champ-select/v1/session");
                if (champSelectSession[0] != "200")
                    return (false, "", "");

                using (JsonDocument doc = JsonDocument.Parse(champSelectSession[1]))
                {
                    // Get local player's cell ID
                    int localPlayerCellId = doc.RootElement.GetProperty("localPlayerCellId").GetInt32();

                    // Get actions array
                    var actionsArray = doc.RootElement.GetProperty("actions");

                    // Iterate through action groups
                    foreach (var actionGroup in actionsArray.EnumerateArray())
                    {
                        foreach (var action in actionGroup.EnumerateArray())
                        {
                            int actorCellId = action.GetProperty("actorCellId").GetInt32();
                            bool isInProgress = action.GetProperty("isInProgress").GetBoolean();
                            bool completed = action.GetProperty("completed").GetBoolean();
                            string actionType = action.GetProperty("type").GetString();

                            // Handle ID which could be either a string or a number
                            string actId;
                            var idProp = action.GetProperty("id");
                            if (idProp.ValueKind == JsonValueKind.Number)
                            {
                                actId = idProp.GetInt64().ToString();
                            }
                            else
                            {
                                actId = idProp.GetString();
                            }

                            // Check if this action is for the local player and is in progress
                            if (actorCellId == localPlayerCellId && isInProgress && !completed)
                            {
                                return (true, actId, actionType);
                            }
                        }
                    }
                }

                return (false, "", "");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking champion select turn: {ex.Message}");
                return (false, "", "");
            }
        }
    }
}