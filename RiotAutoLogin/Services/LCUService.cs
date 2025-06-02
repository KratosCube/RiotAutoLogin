using System;
using System.Collections.Generic;
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
using RiotAutoLogin.Models;

namespace RiotAutoLogin.Services
{
    public static class LCUService
    {
        private static string[] _leagueAuth = Array.Empty<string>();
        private static int _lcuPid = 0;
        private static bool _isLeagueOpen = false;
        private static CancellationTokenSource? _autoAcceptCts;
        private static bool _isAutoAcceptActive = false;
        private static string _lockfilePath = string.Empty;
        private static string _port = string.Empty;
        private static string _password = string.Empty;
        private static readonly HttpClientHandler Handler = new HttpClientHandler()
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        };
        private static readonly HttpClient Client = new HttpClient(Handler);

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
                var client = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault();
                if (client != null)
                {
                    _leagueAuth = GetLeagueAuth(client);
                    _isLeagueOpen = true;

                    if (_lcuPid != client.Id)
                    {
                        _lcuPid = client.Id;
                        Debug.WriteLine($"League Client found. Process ID: {_lcuPid}");
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
                Debug.WriteLine($"Error checking for League Client: {ex.Message}");
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

                    var gameSession = ClientRequest("GET", "lol-gameflow/v1/session");
                    if (gameSession[0] == "200" && TryGetPhase(gameSession[1], out var phase))
                    {
                        if (phase == "ReadyCheck")
                        {
                            var acceptResult = ClientRequest("POST", "lol-matchmaking/v1/ready-check/accept");
                            Debug.WriteLine(acceptResult[0].StartsWith("2") ? 
                                "Match accepted successfully!" : 
                                $"Failed to accept match. Status: {acceptResult[0]}");
                                }

                        var delay = phase switch
                        {
                            "Lobby" or "Matchmaking" or "ChampSelect" or "InProgress" or 
                            "WaitingForStats" or "PreEndOfGame" or "EndOfGame" => 2000,
                            _ => 1000
                        };
                        
                        await Task.Delay(delay, cancellationToken);
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
            const string query = "SELECT CommandLine FROM Win32_Process WHERE ProcessId = ";
            using var searcher = new ManagementObjectSearcher(query + client.Id);
            using var results = searcher.Get();
            
            var commandLine = results.Cast<ManagementObject>()
                .FirstOrDefault()?["CommandLine"]?.ToString() ?? string.Empty;

            var port = Regex.Match(commandLine, @"--app-port=""?(\d+)""?").Groups[1].Value;
            var authToken = Regex.Match(commandLine, @"--remoting-auth-token=([a-zA-Z0-9_-]+)").Groups[1].Value;
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{authToken}"));

            return new[] { auth, port };
        }

        // Make a request to the League Client API
        public static string[] ClientRequest(string method, string url, string? body = null)
        {
            // Ignore invalid https
            var handler = new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            try
            {
                using var client = new HttpClient(handler);
                client.BaseAddress = new Uri($"https://127.0.0.1:{_leagueAuth[1]}/");
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", _leagueAuth[0]);

                    // Set headers
                using var request = new HttpRequestMessage(new HttpMethod(method), url);

                    // Send POST data when doing a post request
                    if (!string.IsNullOrEmpty(body))
                    {
                        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    }

                    // Get the response
                using var response = client.SendAsync(request).Result;

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

                var currentSummonerId = await GetCurrentSummonerIdAsync();
                if (string.IsNullOrEmpty(currentSummonerId))
                {
                    Debug.WriteLine("Could not get summoner ID, returning empty champion list");
                    return new List<ChampionModel>();
                }

                var ownedChampionsResult = ClientRequest("GET", $"lol-champions/v1/owned-champions-minimal");
                if (ownedChampionsResult[0] != "200")
                {
                    Debug.WriteLine($"Failed to get owned champions: {ownedChampionsResult[0]}");
                    return new List<ChampionModel>();
                }

                var champions = new List<ChampionModel>();
                using var doc = JsonDocument.Parse(ownedChampionsResult[1]);
                
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                    if (element.TryGetProperty("id", out var idProp) &&
                        element.TryGetProperty("name", out var nameProp) &&
                        element.TryGetProperty("ownership", out var ownershipProp))
                    {
                        var ownership = ownershipProp.GetProperty("owned").GetBoolean();
                        var name = nameProp.GetString();
                        
                        if (!string.IsNullOrEmpty(name))
                        {
                            champions.Add(new ChampionModel
                            {
                                Id = idProp.GetInt32(),
                                Name = name,
                                IsAvailable = ownership
                            });
                        }
                    }
                }

                // Add "None" option
                champions.Insert(0, new ChampionModel { Name = "None", Id = -1, IsAvailable = true });
                
                Debug.WriteLine($"Loaded {champions.Count} champions from LCU");
                return champions.OrderBy(c => c.Name).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting champions from LCU: {ex.Message}");
                return new List<ChampionModel>();
            }
        }

        public static async Task<List<SummonerSpellModel>> GetSummonerSpellsAsync()
        {
            try
            {
                if (!CheckIfLeagueClientIsOpen())
                {
                    Debug.WriteLine("League client not open, using hardcoded spells");
                    return GetHardcodedSpells();
                }

                var result = ClientRequest("GET", "lol-game-data/assets/v1/summoner-spells.json");
                if (result[0] != "200")
                {
                    Debug.WriteLine($"Failed to get spells from LCU: {result[0]}");
                    return GetHardcodedSpells();
                }

                var spells = new List<SummonerSpellModel>();
                using var doc = JsonDocument.Parse(result[1]);
                
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                    if (element.TryGetProperty("id", out var idProp) &&
                        element.TryGetProperty("name", out var nameProp))
                    {
                        var name = nameProp.GetString();
                        if (!string.IsNullOrEmpty(name))
                        {
                            spells.Add(new SummonerSpellModel
                            {
                                Id = idProp.GetInt32(),
                                Name = name,
                                Description = element.TryGetProperty("description", out var desc) ? 
                                    desc.GetString() ?? string.Empty : string.Empty
                            });
                        }
                    }
                }

                Debug.WriteLine($"Loaded {spells.Count} spells from LCU");
                return spells;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting spells from LCU: {ex.Message}");
                return GetHardcodedSpells();
            }
        }
        private static List<SummonerSpellModel> GetHardcodedSpells() => new()
    {
            new() { Name = "Flash", Id = 4, Description = "Teleports your champion a short distance." },
            new() { Name = "Ignite", Id = 14, Description = "Ignites target enemy champion, dealing true damage." },
            new() { Name = "Heal", Id = 7, Description = "Restores health to your champion and an ally." },
            new() { Name = "Teleport", Id = 12, Description = "Teleports to target allied structure or minion." },
            new() { Name = "Exhaust", Id = 3, Description = "Slows an enemy champion and reduces their damage." },
            new() { Name = "Barrier", Id = 21, Description = "Shields your champion from damage." },
            new() { Name = "Cleanse", Id = 1, Description = "Removes all disables and summoner spell debuffs." },
            new() { Name = "Smite", Id = 11, Description = "Deals true damage to target monster or minion." }
        };
        private static async Task<string?> GetCurrentSummonerIdAsync()
        {
            try
            {
                var result = ClientRequest("GET", "lol-summoner/v1/current-summoner");
                if (result[0] == "200")
                {
                    // Log the full response to diagnose
                    Debug.WriteLine("Full summoner JSON: " + result[1]);

                    using var doc = JsonDocument.Parse(result[1]);
                    if (doc.RootElement.TryGetProperty("summonerId", out var idProp))
                        return idProp.GetString();
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting current summoner ID: {ex.Message}");
                return string.Empty;
            }
        }

        // Champion selection and banning methods
        public static bool SelectChampion(int championId, string actId, bool complete = false)
        {
            try
            {
                var body = JsonSerializer.Serialize(new { championId, completed = complete });
                var result = ClientRequest("PATCH", $"lol-champ-select/v1/session/actions/{actId}", body);
                return result[0].StartsWith("2");
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
                var spellSlot = slot == 1 ? "spell1Id" : "spell2Id";
                var body = JsonSerializer.Serialize(new Dictionary<string, int> { [spellSlot] = spellId });
                var result = ClientRequest("PATCH", "lol-champ-select/v1/session/my-selection", body);
                return result[0].StartsWith("2");
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
                var result = ClientRequest("GET", "lol-gameflow/v1/session");
                return result[0] == "200" && TryGetPhase(result[1], out var phase) ? phase : "None";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting game phase: {ex.Message}");
                return "None";
            }
        }

        // Get champion select session to check pick/ban turns
        public static async Task<(bool isMyTurn, string actId, string actionType)> GetChampSelectTurnAsync()
        {
            try
            {
                var result = ClientRequest("GET", "lol-champ-select/v1/session");
                if (result[0] != "200") return (false, string.Empty, string.Empty);

                using var doc = JsonDocument.Parse(result[1]);
                var currentSummonerId = await GetCurrentSummonerIdAsync();
                
                if (doc.RootElement.TryGetProperty("actions", out var actionsArray))
                {
                    foreach (var actionGroup in actionsArray.EnumerateArray())
                    {
                        foreach (var action in actionGroup.EnumerateArray())
                        {
                            if (action.TryGetProperty("actorCellId", out var actorProp) &&
                                action.TryGetProperty("id", out var idProp) &&
                                action.TryGetProperty("type", out var typeProp) &&
                                action.TryGetProperty("isInProgress", out var inProgressProp) &&
                                inProgressProp.GetBoolean())
                            {
                                // Check if this is our action
                                var actorCellId = actorProp.GetInt32();
                                if (IsOurCellId(doc.RootElement, actorCellId))
                            {
                                    return (true, idProp.GetString(), typeProp.GetString());
                                }
                            }
                        }
                    }
                }

                return (false, string.Empty, string.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking champion select turn: {ex.Message}");
                return (false, string.Empty, string.Empty);
            }
        }

        private static bool TryGetPhase(string json, out string phase)
        {
            phase = string.Empty;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("phase", out var phaseProp))
                {
                    phase = phaseProp.GetString();
                    return true;
                }
            }
            catch
            {
                // Ignore parsing errors
            }
            return false;
        }

        private static bool IsOurCellId(JsonElement sessionElement, int cellId)
        {
            if (sessionElement.TryGetProperty("localPlayerCellId", out var localCellProp))
                return localCellProp.GetInt32() == cellId;
            return false;
        }
    }
}