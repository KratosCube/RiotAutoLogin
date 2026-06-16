using RiotAutoLogin.Models;
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

namespace RiotAutoLogin.Services
{
    public static class LCUService
    {
        private static string[] _leagueAuth = Array.Empty<string>();
        private static int _lcuPid;
        private static bool _isLeagueOpen;
        private static CancellationTokenSource? _autoAcceptCts;
        private static bool _isAutoAcceptActive;

        public static bool IsLeagueOpen => _isLeagueOpen;
        public static bool IsAutoAcceptActive => _isAutoAcceptActive;

        public static void StartAutoAccept()
        {
            if (_isAutoAcceptActive)
                return;

            _isAutoAcceptActive = true;
            _autoAcceptCts = new CancellationTokenSource();
            Task.Run(() => AcceptQueueAsync(_autoAcceptCts.Token));
        }

        public static void StopAutoAccept()
        {
            _isAutoAcceptActive = false;
            _autoAcceptCts?.Cancel();
            _autoAcceptCts?.Dispose();
            _autoAcceptCts = null;
        }

        public static bool CheckIfLeagueClientIsOpen()
        {
            try
            {
                Process? client = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault();
                if (client == null)
                {
                    _isLeagueOpen = false;
                    return false;
                }

                _leagueAuth = GetLeagueAuth(client);
                _isLeagueOpen = true;
                if (_lcuPid != client.Id)
                {
                    _lcuPid = client.Id;
                    Debug.WriteLine($"League Client found. Process ID: {_lcuPid}");
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking for League Client: {ex.Message}");
                _isLeagueOpen = false;
                return false;
            }
        }

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
                    if (gameSession[0] == "200" && TryGetPhase(gameSession[1], out string phase))
                    {
                        if (phase == "ReadyCheck")
                        {
                            int acceptDelayMs = AutoAcceptSettingsService.DelayMilliseconds;
                            if (acceptDelayMs > 0)
                            {
                                await Task.Delay(acceptDelayMs, cancellationToken);
                                if (!CheckIfLeagueClientIsOpen())
                                    continue;

                                gameSession = ClientRequest("GET", "lol-gameflow/v1/session");
                                if (gameSession[0] != "200" || !TryGetPhase(gameSession[1], out phase) || phase != "ReadyCheck")
                                {
                                    Debug.WriteLine("ReadyCheck is no longer active after delay. Skipping auto-accept.");
                                    await Task.Delay(1000, cancellationToken);
                                    continue;
                                }
                            }

                            string[] acceptResult = ClientRequest("POST", "lol-matchmaking/v1/ready-check/accept");
                            Debug.WriteLine(acceptResult[0].StartsWith("2")
                                ? "Match accepted successfully!"
                                : $"Failed to accept match. Status: {acceptResult[0]}");
                        }

                        int delay = phase switch
                        {
                            "Lobby" or "Matchmaking" or "ChampSelect" or "InProgress" or "WaitingForStats" or "PreEndOfGame" or "EndOfGame" => 2000,
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

        private static string[] GetLeagueAuth(Process client)
        {
            const string query = "SELECT CommandLine FROM Win32_Process WHERE ProcessId = ";
            using var searcher = new ManagementObjectSearcher(query + client.Id);
            using var results = searcher.Get();
            string commandLine = results.Cast<ManagementObject>().FirstOrDefault()?["CommandLine"]?.ToString() ?? string.Empty;
            string port = Regex.Match(commandLine, @"--app-port=""?(\d+)""?").Groups[1].Value;
            string authToken = Regex.Match(commandLine, @"--remoting-auth-token=([a-zA-Z0-9_-]+)").Groups[1].Value;
            string auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{authToken}"));
            return new[] { auth, port };
        }

        public static string[] ClientRequest(string method, string url, string? body = null)
        {
            try
            {
                if (_leagueAuth.Length < 2 || string.IsNullOrWhiteSpace(_leagueAuth[0]) || string.IsNullOrWhiteSpace(_leagueAuth[1]))
                    return new[] { "999", "League Client authentication is not available." };

                using var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                };
                using var client = new HttpClient(handler)
                {
                    BaseAddress = new Uri($"https://127.0.0.1:{_leagueAuth[1]}/"),
                    Timeout = TimeSpan.FromSeconds(4)
                };
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", _leagueAuth[0]);

                using var request = new HttpRequestMessage(new HttpMethod(method), url);
                if (!string.IsNullOrEmpty(body))
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                using HttpResponseMessage response = client.SendAsync(request).Result;
                return new[] { ((int)response.StatusCode).ToString(), response.Content.ReadAsStringAsync().Result };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ClientRequest: {ex.Message}");
                return new[] { "999", ex.Message };
            }
        }

        public static async Task<List<ChampionModel>> GetChampionsAsync()
        {
            try
            {
                if (!CheckIfLeagueClientIsOpen())
                    return new List<ChampionModel>();

                string? currentSummonerId = await GetCurrentSummonerIdAsync();
                if (string.IsNullOrEmpty(currentSummonerId))
                    return new List<ChampionModel>();

                string[] ownedChampionsResult = ClientRequest("GET", "lol-champions/v1/owned-champions-minimal");
                if (ownedChampionsResult[0] != "200")
                    return new List<ChampionModel>();

                var champions = new List<ChampionModel>();
                using JsonDocument doc = JsonDocument.Parse(ownedChampionsResult[1]);
                foreach (JsonElement element in doc.RootElement.EnumerateArray())
                {
                    if (element.TryGetProperty("id", out JsonElement idProp) && element.TryGetProperty("name", out JsonElement nameProp) && element.TryGetProperty("ownership", out JsonElement ownershipProp))
                    {
                        string? name = nameProp.GetString();
                        if (!string.IsNullOrEmpty(name))
                        {
                            champions.Add(new ChampionModel
                            {
                                Id = idProp.GetInt32(),
                                Name = name,
                                IsAvailable = ownershipProp.GetProperty("owned").GetBoolean()
                            });
                        }
                    }
                }

                return champions.OrderBy(champion => champion.Name).ToList();
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
                    return GetHardcodedSpells();

                string[] result = ClientRequest("GET", "lol-game-data/assets/v1/summoner-spells.json");
                if (result[0] != "200")
                    return GetHardcodedSpells();

                var spells = new List<SummonerSpellModel>();
                using JsonDocument doc = JsonDocument.Parse(result[1]);
                foreach (JsonElement element in doc.RootElement.EnumerateArray())
                {
                    if (element.TryGetProperty("id", out JsonElement idProp) && element.TryGetProperty("name", out JsonElement nameProp))
                    {
                        string? name = nameProp.GetString();
                        if (!string.IsNullOrEmpty(name))
                        {
                            spells.Add(new SummonerSpellModel
                            {
                                Id = idProp.GetInt32(),
                                Name = name,
                                Description = element.TryGetProperty("description", out JsonElement desc) ? desc.GetString() ?? string.Empty : string.Empty
                            });
                        }
                    }
                }

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

        private static Task<string?> GetCurrentSummonerIdAsync()
        {
            try
            {
                string[] result = ClientRequest("GET", "lol-summoner/v1/current-summoner");
                if (result[0] == "200")
                {
                    using JsonDocument doc = JsonDocument.Parse(result[1]);
                    if (doc.RootElement.TryGetProperty("summonerId", out JsonElement idProp))
                        return Task.FromResult<string?>(idProp.GetString());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting current summoner ID: {ex.Message}");
            }
            return Task.FromResult<string?>(string.Empty);
        }

        public static bool SelectChampion(int championId, string actId, bool complete = false)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(actId))
                {
                    string body = JsonSerializer.Serialize(new { championId, completed = complete });
                    string[] result = ClientRequest("PATCH", $"lol-champ-select/v1/session/actions/{actId}", body);
                    if (result[0].StartsWith("2"))
                        return true;
                }

                string[] swapResult = ClientRequest("POST", $"lol-champ-select/v1/session/bench/swap/{championId}");
                return swapResult[0].StartsWith("2");
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
                string spellSlot = slot == 1 ? "spell1Id" : "spell2Id";
                string body = JsonSerializer.Serialize(new Dictionary<string, int> { [spellSlot] = spellId });
                string[] result = ClientRequest("PATCH", "lol-champ-select/v1/session/my-selection", body);
                return result[0].StartsWith("2");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error selecting summoner spell: {ex.Message}");
                return false;
            }
        }

        public static Task<string> GetCurrentGamePhaseAsync()
        {
            try
            {
                string[] result = ClientRequest("GET", "lol-gameflow/v1/session");
                return Task.FromResult(result[0] == "200" && TryGetPhase(result[1], out string phase) ? phase : "None");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting game phase: {ex.Message}");
                return Task.FromResult("None");
            }
        }

        public static async Task<(bool isMyTurn, string actId, string actionType)> GetChampSelectTurnAsync()
        {
            try
            {
                string[] result = ClientRequest("GET", "lol-champ-select/v1/session");
                if (result[0] != "200")
                    return (false, string.Empty, string.Empty);

                using JsonDocument doc = JsonDocument.Parse(result[1]);
                _ = await GetCurrentSummonerIdAsync();
                if (!doc.RootElement.TryGetProperty("actions", out JsonElement actionsArray))
                    return (false, string.Empty, string.Empty);

                foreach (JsonElement actionGroup in actionsArray.EnumerateArray())
                {
                    foreach (JsonElement action in actionGroup.EnumerateArray())
                    {
                        if (!action.TryGetProperty("actorCellId", out JsonElement actorProp) ||
                            !action.TryGetProperty("id", out JsonElement idProp) ||
                            !action.TryGetProperty("type", out JsonElement typeProp) ||
                            !action.TryGetProperty("isInProgress", out JsonElement inProgressProp) ||
                            !inProgressProp.GetBoolean())
                            continue;

                        if (!IsOurCellId(doc.RootElement, actorProp.GetInt32()))
                            continue;

                        string actId = idProp.ValueKind == JsonValueKind.Number ? idProp.GetInt32().ToString() : idProp.GetString() ?? string.Empty;
                        string actionType = typeProp.GetString() ?? string.Empty;
                        return (true, actId, actionType);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking champion select turn: {ex.Message}");
            }
            return (false, string.Empty, string.Empty);
        }

        private static bool TryGetPhase(string json, out string phase)
        {
            phase = string.Empty;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("phase", out JsonElement phaseProp))
                {
                    phase = phaseProp.GetString() ?? string.Empty;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static bool IsOurCellId(JsonElement sessionElement, int cellId)
        {
            return sessionElement.TryGetProperty("localPlayerCellId", out JsonElement localCellProp) && localCellProp.GetInt32() == cellId;
        }
    }
}