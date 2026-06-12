using RiotAutoLogin.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services
{
    public sealed class RemotePickServerService : IDisposable
    {
        public const int DefaultPort = 5055;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _serverTask;
        private List<ChampionModel>? _championCache;
        private List<RemoteChampionDto>? _remoteChampionCache;
        private List<RemoteSummonerSpellDto>? _remoteSpellCache;

        public bool IsRunning { get; private set; }
        public int Port { get; private set; } = DefaultPort;
        public IReadOnlyList<string> LocalUrls => GetLocalUrls(Port);
        public string LocalUrl => LocalUrls.FirstOrDefault(url => !url.Contains("127.0.0.1")) ?? LocalUrls.FirstOrDefault() ?? $"http://127.0.0.1:{Port}/";
        public string LocalUrlDisplay => string.Join(Environment.NewLine, LocalUrls);

        public Task StartAsync(int port = DefaultPort)
        {
            if (IsRunning)
                return Task.CompletedTask;

            Port = port;
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();
            IsRunning = true;
            _serverTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
            Debug.WriteLine($"Remote Pick server started at:{Environment.NewLine}{LocalUrlDisplay}");
            return Task.CompletedTask;
        }

        public void Stop()
        {
            if (!IsRunning)
                return;

            IsRunning = false;

            try
            {
                _cts?.Cancel();
                _listener?.Stop();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error stopping Remote Pick server: {ex.Message}");
            }
            finally
            {
                _listener = null;
                _cts?.Dispose();
                _cts = null;
                _serverTask = null;
                Debug.WriteLine("Remote Pick server stopped");
            }
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_listener == null)
                        break;

                    TcpClient client = await _listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    if (!cancellationToken.IsCancellationRequested)
                        throw;
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Remote Pick accept error: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            await using NetworkStream stream = client.GetStream();
            using StreamReader reader = new(stream, Encoding.UTF8, leaveOpen: true);

            try
            {
                string? requestLine = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(requestLine))
                    return;

                string[] requestParts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (requestParts.Length < 2)
                {
                    await WriteTextResponseAsync(stream, 400, "Bad Request", "Bad request", cancellationToken);
                    return;
                }

                string method = requestParts[0].ToUpperInvariant();
                string path = requestParts[1];
                int contentLength = 0;

                string? headerLine;
                while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync()))
                {
                    int separatorIndex = headerLine.IndexOf(':');
                    if (separatorIndex <= 0)
                        continue;

                    string name = headerLine[..separatorIndex].Trim();
                    string value = headerLine[(separatorIndex + 1)..].Trim();
                    if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(value, out contentLength);
                }

                string body = string.Empty;
                if (contentLength > 0)
                {
                    char[] buffer = new char[contentLength];
                    int totalRead = 0;
                    while (totalRead < contentLength)
                    {
                        int read = await reader.ReadAsync(buffer, totalRead, contentLength - totalRead);
                        if (read == 0)
                            break;
                        totalRead += read;
                    }

                    body = new string(buffer, 0, totalRead);
                }

                if (method == "GET" && (path == "/" || path.StartsWith("/?", StringComparison.Ordinal)))
                {
                    await WriteHtmlResponseAsync(stream, RemotePickPageHtml.Get(), cancellationToken);
                }
                else if (method == "GET" && path.StartsWith("/api/state", StringComparison.OrdinalIgnoreCase))
                {
                    RemotePickState state = await GetStateAsync();
                    await WriteJsonResponseAsync(stream, 200, "OK", state, cancellationToken);
                }
                else if (method == "POST" && path.StartsWith("/api/pick", StringComparison.OrdinalIgnoreCase))
                {
                    RemotePickActionResult result = await PickChampionAsync(body);
                    await WriteJsonResponseAsync(stream, result.Success ? 200 : 400, result.Success ? "OK" : "Bad Request", result, cancellationToken);
                }
                else if (method == "POST" && path.StartsWith("/api/ban", StringComparison.OrdinalIgnoreCase))
                {
                    RemotePickActionResult result = await BanChampionAsync(body);
                    await WriteJsonResponseAsync(stream, result.Success ? 200 : 400, result.Success ? "OK" : "Bad Request", result, cancellationToken);
                }
                else if (method == "POST" && path.StartsWith("/api/hover", StringComparison.OrdinalIgnoreCase))
                {
                    RemotePickActionResult result = await HoverChampionAsync(body);
                    await WriteJsonResponseAsync(stream, result.Success ? 200 : 400, result.Success ? "OK" : "Bad Request", result, cancellationToken);
                }
                else if (method == "POST" && path.StartsWith("/api/spell", StringComparison.OrdinalIgnoreCase))
                {
                    RemotePickActionResult result = await SelectSpellAsync(body);
                    await WriteJsonResponseAsync(stream, result.Success ? 200 : 400, result.Success ? "OK" : "Bad Request", result, cancellationToken);
                }
                else if (method == "POST" && path.StartsWith("/api/rune-page", StringComparison.OrdinalIgnoreCase))
                {
                    RemotePickActionResult result = await SelectRunePageAsync(body);
                    await WriteJsonResponseAsync(stream, result.Success ? 200 : 400, result.Success ? "OK" : "Bad Request", result, cancellationToken);
                }
                else if (method == "POST" && path.StartsWith("/api/leave", StringComparison.OrdinalIgnoreCase))
                {
                    RemotePickActionResult result = await LeaveLobbyAsync();
                    await WriteJsonResponseAsync(stream, result.Success ? 200 : 400, result.Success ? "OK" : "Bad Request", result, cancellationToken);
                }
                else
                {
                    await WriteTextResponseAsync(stream, 404, "Not Found", "Not found", cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Remote Pick request error: {ex.Message}");
                try
                {
                    await WriteTextResponseAsync(stream, 500, "Internal Server Error", "Internal server error", cancellationToken);
                }
                catch
                {
                    // Ignore response failures caused by disconnected clients.
                }
            }
            finally
            {
                client.Close();
            }
        }

        public async Task<RemotePickState> GetStateAsync()
        {
            var state = new RemotePickState();

            if (!LCUService.CheckIfLeagueClientIsOpen())
            {
                state.PhaseLabel = "League Closed";
                state.Message = "League Client is not running.";
                await AddChampionSpellAndRuneListsAsync(state, new HashSet<int>(), new HashSet<int>());
                return state;
            }

            state.Phase = await LCUService.GetCurrentGamePhaseAsync();
            state.IsInChampSelect = state.Phase == "ChampSelect";
            state.CanLeave = state.Phase is "Lobby" or "Matchmaking" or "ReadyCheck" or "ChampSelect";

            var bannedChampionIds = new HashSet<int>();
            var pickedChampionIds = new HashSet<int>();

            if (!state.IsInChampSelect)
            {
                state.PhaseLabel = GetGameflowPhaseLabel(state.Phase);
                state.Message = state.Phase == "None"
                    ? "Waiting for League Client state..."
                    : $"Current phase: {state.Phase}. Waiting for champion select.";

                await AddChampionSpellAndRuneListsAsync(state, bannedChampionIds, pickedChampionIds);
                return state;
            }

            string[] sessionResult = LCUService.ClientRequest("GET", "lol-champ-select/v1/session");
            if (sessionResult[0] != "200")
            {
                state.PhaseLabel = "Champ Select";
                state.Message = $"Could not read champion select session. Status: {sessionResult[0]}";
                await AddChampionSpellAndRuneListsAsync(state, bannedChampionIds, pickedChampionIds);
                return state;
            }

            using JsonDocument doc = JsonDocument.Parse(sessionResult[1]);
            JsonElement root = doc.RootElement;

            ParseBans(root, bannedChampionIds);
            ParsePicks(root, pickedChampionIds);
            ParseLocalPlayerSelection(root, state);
            ParseAvailableSpellIds(root, state);
            ParseTimer(root, state);
            ParseActions(root, state);
            state.PhaseLabel = GetChampSelectPhaseLabel(state, bannedChampionIds);

            state.BannedChampionIds = bannedChampionIds.OrderBy(id => id).ToList();
            state.PickedChampionIds = pickedChampionIds.OrderBy(id => id).ToList();
            state.Message = state.IsMyTurn
                ? state.ActionType == "pick"
                    ? "It is your pick. Lock in or hover a champion."
                    : state.ActionType == "ban"
                        ? "It is your ban. Choose a champion to ban."
                        : $"It is your {state.ActionType} turn."
                : "You can hover an intended pick while waiting for your turn.";

            await AddChampionSpellAndRuneListsAsync(state, bannedChampionIds, pickedChampionIds);
            return state;
        }

        public async Task<RemotePickActionResult> PickChampionAsync(string body)
        {
            RemotePickRequest? request = ParseChampionRequest(body);
            if (request == null)
                return new RemotePickActionResult { Success = false, Message = "Invalid champion." };

            RemotePickState state = await GetStateAsync();
            if (!state.IsInChampSelect)
                return new RemotePickActionResult { Success = false, Message = "You are not in champion select." };

            if (!state.CanPick)
                return new RemotePickActionResult { Success = false, Message = "It is not your pick turn." };

            if (state.BannedChampionIds.Contains(request.ChampionId))
                return new RemotePickActionResult { Success = false, Message = "This champion is banned." };

            if (state.PickedChampionIds.Contains(request.ChampionId))
                return new RemotePickActionResult { Success = false, Message = "This champion is already picked." };

            bool success = LCUService.SelectChampion(request.ChampionId, state.ActionId, complete: true);
            return new RemotePickActionResult
            {
                Success = success,
                Message = success ? "Champion locked in." : "League Client rejected the pick request."
            };
        }

        public async Task<RemotePickActionResult> BanChampionAsync(string body)
        {
            RemotePickRequest? request = ParseChampionRequest(body);
            if (request == null)
                return new RemotePickActionResult { Success = false, Message = "Invalid champion." };

            RemotePickState state = await GetStateAsync();
            if (!state.IsInChampSelect)
                return new RemotePickActionResult { Success = false, Message = "You are not in champion select." };

            if (!state.CanBan)
                return new RemotePickActionResult { Success = false, Message = "It is not your ban turn." };

            if (state.BannedChampionIds.Contains(request.ChampionId))
                return new RemotePickActionResult { Success = false, Message = "This champion is already banned." };

            if (state.PickedChampionIds.Contains(request.ChampionId))
                return new RemotePickActionResult { Success = false, Message = "This champion is already picked." };

            bool success = LCUService.SelectChampion(request.ChampionId, state.ActionId, complete: true);
            return new RemotePickActionResult
            {
                Success = success,
                Message = success ? "Champion banned." : "League Client rejected the ban request."
            };
        }

        public async Task<RemotePickActionResult> HoverChampionAsync(string body)
        {
            RemotePickRequest? request = ParseChampionRequest(body);
            if (request == null)
                return new RemotePickActionResult { Success = false, Message = "Invalid champion." };

            RemotePickState state = await GetStateAsync();
            if (!state.IsInChampSelect)
                return new RemotePickActionResult { Success = false, Message = "You are not in champion select." };

            if (state.BannedChampionIds.Contains(request.ChampionId))
                return new RemotePickActionResult { Success = false, Message = "This champion is banned." };

            if (state.PickedChampionIds.Contains(request.ChampionId))
                return new RemotePickActionResult { Success = false, Message = "This champion is already picked." };

            bool success = false;

            if (!string.IsNullOrWhiteSpace(state.PickActionId))
                success = LCUService.SelectChampion(request.ChampionId, state.PickActionId, complete: false);

            if (!success && !string.IsNullOrWhiteSpace(state.ActionId) && state.ActionType == "pick")
                success = LCUService.SelectChampion(request.ChampionId, state.ActionId, complete: false);

            if (!success)
                success = TryPatchMySelection(new { championPickIntent = request.ChampionId });

            if (!success)
                success = TryPatchMySelection(new { championId = request.ChampionId });

            return new RemotePickActionResult
            {
                Success = success,
                Message = success ? "Champion intent updated." : "League Client rejected the hover request."
            };
        }

        public async Task<RemotePickActionResult> SelectSpellAsync(string body)
        {
            RemoteSpellRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<RemoteSpellRequest>(body, _jsonOptions);
            }
            catch
            {
                return new RemotePickActionResult { Success = false, Message = "Invalid spell request." };
            }

            if (request == null || request.SpellId <= 0 || request.Slot is not (1 or 2))
                return new RemotePickActionResult { Success = false, Message = "Invalid summoner spell." };

            if (!LCUService.CheckIfLeagueClientIsOpen())
                return new RemotePickActionResult { Success = false, Message = "League Client is not running." };

            RemotePickState state = await GetStateAsync();
            if (state.AvailableSpellIds.Count > 0 &&
                !state.AvailableSpellIds.Contains(request.SpellId) &&
                request.SpellId != state.Spell1Id &&
                request.SpellId != state.Spell2Id)
            {
                return new RemotePickActionResult { Success = false, Message = "This summoner spell is not available in the current queue." };
            }

            bool success = LCUService.SelectSummonerSpell(request.SpellId, request.Slot);
            return new RemotePickActionResult
            {
                Success = success,
                Message = success ? "Summoner spell updated." : "League Client rejected the spell change."
            };
        }

        public async Task<RemotePickActionResult> SelectRunePageAsync(string body)
        {
            RemoteRunePageRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<RemoteRunePageRequest>(body, _jsonOptions);
            }
            catch
            {
                return new RemotePickActionResult { Success = false, Message = "Invalid rune page request." };
            }

            if (request == null || request.PageId <= 0)
                return new RemotePickActionResult { Success = false, Message = "Invalid rune page." };

            if (!LCUService.CheckIfLeagueClientIsOpen())
                return new RemotePickActionResult { Success = false, Message = "League Client is not running." };

            string[] pagesResult = LCUService.ClientRequest("GET", "lol-perks/v1/pages");
            if (pagesResult[0] != "200")
                return new RemotePickActionResult { Success = false, Message = $"Could not load rune pages. Status: {pagesResult[0]}" };

            string? selectedPageJson = null;
            try
            {
                using JsonDocument pagesDoc = JsonDocument.Parse(pagesResult[1]);
                if (pagesDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement page in pagesDoc.RootElement.EnumerateArray())
                    {
                        if (page.TryGetProperty("id", out JsonElement idElement) &&
                            TryGetLong(idElement, out long pageId) &&
                            pageId == request.PageId)
                        {
                            selectedPageJson = page.GetRawText();
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not parse rune pages: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(selectedPageJson))
                return new RemotePickActionResult { Success = false, Message = "Rune page was not found." };

            string[] result = LCUService.ClientRequest("POST", $"lol-perks/v1/pages/{request.PageId}/current");
            if (!result[0].StartsWith("2"))
                result = LCUService.ClientRequest("PUT", "lol-perks/v1/currentpage", selectedPageJson);

            if (!result[0].StartsWith("2"))
                result = LCUService.ClientRequest("PATCH", "lol-perks/v1/currentpage", selectedPageJson);

            bool success = result[0].StartsWith("2");
            return new RemotePickActionResult
            {
                Success = success,
                Message = success ? "Rune page selected." : $"League Client rejected rune page change. Status: {result[0]}"
            };
        }

        public async Task<RemotePickActionResult> LeaveLobbyAsync()
        {
            if (!LCUService.CheckIfLeagueClientIsOpen())
                return new RemotePickActionResult { Success = false, Message = "League Client is not running." };

            string phase = await LCUService.GetCurrentGamePhaseAsync();
            if (phase == "ReadyCheck")
                return RequestLeave("ReadyCheck", LCUService.ClientRequest("POST", "lol-matchmaking/v1/ready-check/decline"));

            if (phase == "Matchmaking")
                return RequestLeave("Matchmaking", LCUService.ClientRequest("DELETE", "lol-matchmaking/v1/search"));

            if (phase == "Lobby")
                return RequestLeave("Lobby", LCUService.ClientRequest("DELETE", "lol-lobby/v2/lobby"));

            if (phase == "ChampSelect")
            {
                string[] result = LCUService.ClientRequest("POST", "lol-champ-select/v1/session/quit");
                if (result[0].StartsWith("2"))
                    return RequestLeave("ChampSelect", result);

                result = LCUService.ClientRequest("DELETE", "lol-lobby/v2/lobby");
                if (result[0].StartsWith("2"))
                    return RequestLeave("ChampSelect", result);

                return new RemotePickActionResult
                {
                    Success = false,
                    Message = $"Dodge request was rejected by League Client. Status: {result[0]}"
                };
            }

            return new RemotePickActionResult { Success = false, Message = $"Cannot leave during phase: {phase}" };
        }

        private static RemotePickActionResult RequestLeave(string phase, string[] result)
        {
            bool success = result[0].StartsWith("2");
            return new RemotePickActionResult
            {
                Success = success,
                Message = success ? "Leave request sent." : $"Leave failed during {phase}. Status: {result[0]}"
            };
        }

        private bool TryPatchMySelection(object payload)
        {
            string bodyJson = JsonSerializer.Serialize(payload, _jsonOptions);
            string[] result = LCUService.ClientRequest("PATCH", "lol-champ-select/v1/session/my-selection", bodyJson);
            Debug.WriteLine($"Remote hover PATCH my-selection result: {result[0]} / body: {bodyJson}");
            return result[0].StartsWith("2");
        }

        private RemotePickRequest? ParseChampionRequest(string body)
        {
            try
            {
                RemotePickRequest? request = JsonSerializer.Deserialize<RemotePickRequest>(body, _jsonOptions);
                return request == null || request.ChampionId <= 0 ? null : request;
            }
            catch
            {
                return null;
            }
        }

        private async Task AddChampionSpellAndRuneListsAsync(RemotePickState state, HashSet<int> bannedChampionIds, HashSet<int> pickedChampionIds)
        {
            await AddChampionAndSpellListsAsync(state, bannedChampionIds, pickedChampionIds);
            AddRunePages(state);
        }

        private async Task AddChampionAndSpellListsAsync(RemotePickState state, HashSet<int> bannedChampionIds, HashSet<int> pickedChampionIds)
        {
            await EnsureRemoteChampionCacheAsync();
            await EnsureRemoteSpellCacheAsync();

            state.Champions = (_remoteChampionCache ?? new List<RemoteChampionDto>())
                .Select(champion => new RemoteChampionDto
                {
                    Id = champion.Id,
                    Name = champion.Name,
                    ImageUrl = champion.ImageUrl,
                    IsBanned = bannedChampionIds.Contains(champion.Id),
                    IsPicked = pickedChampionIds.Contains(champion.Id),
                    IsSelected = state.SelectedChampionId == champion.Id,
                    IsIntent = state.PickIntentChampionId == champion.Id
                })
                .ToList();

            state.SummonerSpells = (_remoteSpellCache ?? new List<RemoteSummonerSpellDto>())
                .Select(spell => new RemoteSummonerSpellDto
                {
                    Id = spell.Id,
                    Name = spell.Name,
                    Description = spell.Description,
                    ImageUrl = spell.ImageUrl,
                    IsSpell1 = state.Spell1Id == spell.Id,
                    IsSpell2 = state.Spell2Id == spell.Id,
                    IsAvailable = state.AvailableSpellIds.Count == 0 ||
                                  state.AvailableSpellIds.Contains(spell.Id) ||
                                  state.Spell1Id == spell.Id ||
                                  state.Spell2Id == spell.Id
                })
                .ToList();
        }

        private void AddRunePages(RemotePickState state)
        {
            if (!LCUService.CheckIfLeagueClientIsOpen())
                return;

            string[] pagesResult = LCUService.ClientRequest("GET", "lol-perks/v1/pages");
            if (pagesResult[0] != "200")
            {
                Debug.WriteLine($"Could not load rune pages. Status: {pagesResult[0]}");
                return;
            }

            long currentPageId = 0;
            string[] currentPageResult = LCUService.ClientRequest("GET", "lol-perks/v1/currentpage");
            if (currentPageResult[0] == "200")
            {
                try
                {
                    using JsonDocument currentDoc = JsonDocument.Parse(currentPageResult[1]);
                    if (currentDoc.RootElement.TryGetProperty("id", out JsonElement idElement) &&
                        TryGetLong(idElement, out long parsedCurrentPageId))
                    {
                        currentPageId = parsedCurrentPageId;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Could not parse current rune page: {ex.Message}");
                }
            }

            try
            {
                using JsonDocument pagesDoc = JsonDocument.Parse(pagesResult[1]);
                if (pagesDoc.RootElement.ValueKind != JsonValueKind.Array)
                    return;

                foreach (JsonElement page in pagesDoc.RootElement.EnumerateArray())
                {
                    if (!page.TryGetProperty("id", out JsonElement idElement) || !TryGetLong(idElement, out long pageId))
                        continue;

                    string pageName = page.TryGetProperty("name", out JsonElement nameElement)
                        ? nameElement.GetString() ?? $"Rune Page {pageId}"
                        : $"Rune Page {pageId}";

                    bool isCurrent = currentPageId == pageId ||
                                     GetBoolProperty(page, "current") ||
                                     GetBoolProperty(page, "isCurrent");

                    var dto = new RemoteRunePageDto
                    {
                        Id = pageId,
                        Name = pageName,
                        IsCurrent = isCurrent,
                        IsEditable = GetBoolProperty(page, "isEditable"),
                        IsDeletable = GetBoolProperty(page, "isDeletable")
                    };

                    state.RunePages.Add(dto);
                    if (dto.IsCurrent)
                        state.CurrentRunePage = dto;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not parse rune pages: {ex.Message}");
            }
        }

        private async Task EnsureRemoteChampionCacheAsync()
        {
            if (_remoteChampionCache != null)
                return;

            _championCache ??= await DataDragonService.GetAllChampionsAsync();

            var champions = _championCache
                .Where(champion => champion.Id > 0)
                .OrderBy(champion => champion.Name)
                .ToList();

            var remoteChampions = new List<RemoteChampionDto>();
            foreach (ChampionModel champion in champions)
            {
                string imageUrl = string.Empty;
                try
                {
                    imageUrl = await DataDragonService.GetChampionImageUrlAsync(champion.Name);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Could not get champion image URL for {champion.Name}: {ex.Message}");
                }

                remoteChampions.Add(new RemoteChampionDto
                {
                    Id = champion.Id,
                    Name = champion.Name,
                    ImageUrl = imageUrl
                });
            }

            _remoteChampionCache = remoteChampions;
        }

        private async Task EnsureRemoteSpellCacheAsync()
        {
            if (_remoteSpellCache != null)
                return;

            List<SummonerSpellModel> spells = await LCUService.GetSummonerSpellsAsync();
            MergeDefaultSummonerSpells(spells);

            var remoteSpells = new List<RemoteSummonerSpellDto>();
            foreach (SummonerSpellModel spell in spells.Where(spell => spell.Id > 0).OrderBy(spell => spell.Name))
            {
                string imageUrl = spell.ImageUrl ?? string.Empty;
                try
                {
                    if (string.IsNullOrWhiteSpace(imageUrl) && !string.IsNullOrWhiteSpace(spell.Name))
                        imageUrl = await DataDragonService.GetSummonerSpellImageUrlAsync(spell.Name);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Could not get summoner spell image URL for {spell.Name}: {ex.Message}");
                }

                remoteSpells.Add(new RemoteSummonerSpellDto
                {
                    Id = spell.Id,
                    Name = spell.Name ?? $"Spell {spell.Id}",
                    Description = spell.Description ?? string.Empty,
                    ImageUrl = imageUrl
                });
            }

            _remoteSpellCache = remoteSpells;
        }

        private static void MergeDefaultSummonerSpells(List<SummonerSpellModel> spells)
        {
            SummonerSpellModel[] defaults =
            {
                new() { Name = "Cleanse", Id = 1, Description = "Removes disables and summoner spell debuffs." },
                new() { Name = "Exhaust", Id = 3, Description = "Slows an enemy champion and reduces their damage." },
                new() { Name = "Flash", Id = 4, Description = "Teleports your champion a short distance." },
                new() { Name = "Ghost", Id = 6, Description = "Gain movement speed and ghosting." },
                new() { Name = "Heal", Id = 7, Description = "Restores health to your champion and an ally." },
                new() { Name = "Smite", Id = 11, Description = "Deals true damage to monsters or minions." },
                new() { Name = "Teleport", Id = 12, Description = "Teleports to an allied structure, minion, or ward." },
                new() { Name = "Clarity", Id = 13, Description = "Restores mana to you and nearby allies." },
                new() { Name = "Ignite", Id = 14, Description = "Deals true damage over time to an enemy champion." },
                new() { Name = "Barrier", Id = 21, Description = "Shields your champion from damage." },
                new() { Name = "Mark", Id = 32, Description = "Throw a snowball and dash to the marked target." }
            };

            foreach (SummonerSpellModel spell in defaults)
            {
                if (spells.All(existing => existing.Id != spell.Id))
                    spells.Add(spell);
            }
        }

        private static void ParseBans(JsonElement root, HashSet<int> bannedChampionIds)
        {
            if (!root.TryGetProperty("bans", out JsonElement bansElement))
                return;

            AddChampionIdsFromArray(bansElement, "myTeamBans", bannedChampionIds);
            AddChampionIdsFromArray(bansElement, "theirTeamBans", bannedChampionIds);
        }

        private static void ParsePicks(JsonElement root, HashSet<int> pickedChampionIds)
        {
            AddChampionIdsFromTeam(root, "myTeam", pickedChampionIds);
            AddChampionIdsFromTeam(root, "theirTeam", pickedChampionIds);
        }

        private static void ParseLocalPlayerSelection(JsonElement root, RemotePickState state)
        {
            if (!root.TryGetProperty("localPlayerCellId", out JsonElement localCellElement) ||
                !TryGetInt(localCellElement, out int localPlayerCellId))
            {
                return;
            }

            if (!root.TryGetProperty("myTeam", out JsonElement myTeamElement) || myTeamElement.ValueKind != JsonValueKind.Array)
                return;

            foreach (JsonElement player in myTeamElement.EnumerateArray())
            {
                if (!player.TryGetProperty("cellId", out JsonElement cellElement) ||
                    !TryGetInt(cellElement, out int cellId) ||
                    cellId != localPlayerCellId)
                {
                    continue;
                }

                if (player.TryGetProperty("championId", out JsonElement championIdElement) &&
                    TryGetInt(championIdElement, out int championId))
                {
                    state.SelectedChampionId = championId;
                }

                if (player.TryGetProperty("championPickIntent", out JsonElement intentElement) &&
                    TryGetInt(intentElement, out int intentChampionId))
                {
                    state.PickIntentChampionId = intentChampionId;
                }

                if (player.TryGetProperty("spell1Id", out JsonElement spell1Element) && TryGetInt(spell1Element, out int spell1Id))
                    state.Spell1Id = spell1Id;

                if (player.TryGetProperty("spell2Id", out JsonElement spell2Element) && TryGetInt(spell2Element, out int spell2Id))
                    state.Spell2Id = spell2Id;

                return;
            }
        }

        private static void ParseAvailableSpellIds(JsonElement root, RemotePickState state)
        {
            var spellIds = new HashSet<int>();
            CollectAvailableSpellIds(root, spellIds);
            state.AvailableSpellIds = spellIds.OrderBy(id => id).ToList();
        }

        private static void ParseTimer(JsonElement root, RemotePickState state)
        {
            if (!root.TryGetProperty("timer", out JsonElement timerElement) || timerElement.ValueKind != JsonValueKind.Object)
                return;

            if (timerElement.TryGetProperty("phase", out JsonElement phaseElement))
                state.ChampSelectPhase = phaseElement.GetString() ?? string.Empty;

            if (timerElement.TryGetProperty("adjustedTimeLeftInPhase", out JsonElement leftElement) && TryGetInt(leftElement, out int timeLeftMs))
                state.TimeLeftInPhaseMs = Math.Max(0, timeLeftMs);

            if (timerElement.TryGetProperty("totalTimeInPhase", out JsonElement totalElement) && TryGetInt(totalElement, out int totalMs))
                state.TotalTimeInPhaseMs = Math.Max(0, totalMs);

            if (timerElement.TryGetProperty("isInfinite", out JsonElement infiniteElement) && infiniteElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                state.IsTimerInfinite = infiniteElement.GetBoolean();
        }

        private static string GetGameflowPhaseLabel(string phase)
        {
            return phase switch
            {
                "Lobby" => "Lobby",
                "Matchmaking" => "In Queue",
                "ReadyCheck" => "Match Found",
                "ChampSelect" => "Champ Select",
                "InProgress" => "In Game",
                "WaitingForStats" or "PreEndOfGame" or "EndOfGame" => "Post Game",
                _ => "Waiting"
            };
        }

        private static string GetChampSelectPhaseLabel(RemotePickState state, HashSet<int> bannedChampionIds)
        {
            if (state.CanBan)
                return "Ban Phase";

            if (state.CanPick)
                return "Pick Phase";

            string phase = state.ChampSelectPhase.ToUpperInvariant();
            if (phase.Contains("PLANNING"))
                return "Pre-Ban Planning";

            if (phase.Contains("BAN") && !phase.Contains("PICK"))
                return "Ban Phase";

            if (phase.Contains("BAN") || phase.Contains("PICK"))
                return bannedChampionIds.Count == 0 ? "Ban / Pick Phase" : "Pick Phase";

            if (phase.Contains("FINAL"))
                return "Finalize Loadout";

            return "Champ Select";
        }

        private static void CollectAvailableSpellIds(JsonElement element, HashSet<int> spellIds)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    bool looksLikeAvailableSpellList =
                        property.NameEquals("allowableSpellIds") ||
                        property.NameEquals("availableSpellIds") ||
                        property.NameEquals("allowedSpellIds");

                    if (looksLikeAvailableSpellList && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement idElement in property.Value.EnumerateArray())
                        {
                            if (TryGetInt(idElement, out int spellId) && spellId > 0)
                                spellIds.Add(spellId);
                        }
                    }
                    else if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        CollectAvailableSpellIds(property.Value, spellIds);
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in element.EnumerateArray())
                    CollectAvailableSpellIds(child, spellIds);
            }
        }

        private static void AddChampionIdsFromTeam(JsonElement root, string propertyName, HashSet<int> championIds)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement teamElement) || teamElement.ValueKind != JsonValueKind.Array)
                return;

            foreach (JsonElement player in teamElement.EnumerateArray())
            {
                if (player.TryGetProperty("championId", out JsonElement championIdElement) &&
                    TryGetInt(championIdElement, out int championId) &&
                    championId > 0)
                {
                    championIds.Add(championId);
                }
            }
        }

        private static void AddChampionIdsFromArray(JsonElement root, string propertyName, HashSet<int> championIds)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement idsElement) || idsElement.ValueKind != JsonValueKind.Array)
                return;

            foreach (JsonElement idElement in idsElement.EnumerateArray())
            {
                if (TryGetInt(idElement, out int championId) && championId > 0)
                    championIds.Add(championId);
            }
        }

        private static void ParseActions(JsonElement root, RemotePickState state)
        {
            if (!root.TryGetProperty("localPlayerCellId", out JsonElement localCellElement) ||
                !TryGetInt(localCellElement, out int localPlayerCellId))
            {
                return;
            }

            if (!root.TryGetProperty("actions", out JsonElement actionsElement) || actionsElement.ValueKind != JsonValueKind.Array)
                return;

            foreach (JsonElement actionGroup in actionsElement.EnumerateArray())
            {
                if (actionGroup.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (JsonElement action in actionGroup.EnumerateArray())
                {
                    if (!action.TryGetProperty("actorCellId", out JsonElement actorCellElement) ||
                        !TryGetInt(actorCellElement, out int actorCellId) ||
                        actorCellId != localPlayerCellId)
                    {
                        continue;
                    }

                    string actionType = action.TryGetProperty("type", out JsonElement typeElement)
                        ? (typeElement.GetString() ?? string.Empty).ToLowerInvariant()
                        : string.Empty;

                    string actionId = action.TryGetProperty("id", out JsonElement idElement)
                        ? GetJsonValueAsString(idElement)
                        : string.Empty;

                    if (actionType == "pick" && string.IsNullOrWhiteSpace(state.PickActionId))
                        state.PickActionId = actionId;

                    bool isInProgress = action.TryGetProperty("isInProgress", out JsonElement inProgressElement) &&
                                        inProgressElement.GetBoolean();
                    if (!isInProgress)
                        continue;

                    state.IsMyTurn = true;
                    state.ActionId = actionId;
                    state.ActionType = actionType;
                }
            }
        }

        private static bool GetBoolProperty(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out JsonElement valueElement) &&
                   valueElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                   valueElement.GetBoolean();
        }

        private static bool TryGetInt(JsonElement element, out int value)
        {
            if (element.ValueKind == JsonValueKind.Number)
                return element.TryGetInt32(out value);

            if (element.ValueKind == JsonValueKind.String)
                return int.TryParse(element.GetString(), out value);

            value = 0;
            return false;
        }

        private static bool TryGetLong(JsonElement element, out long value)
        {
            if (element.ValueKind == JsonValueKind.Number)
                return element.TryGetInt64(out value);

            if (element.ValueKind == JsonValueKind.String)
                return long.TryParse(element.GetString(), out value);

            value = 0;
            return false;
        }

        private static string GetJsonValueAsString(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Number => element.TryGetInt32(out int number) ? number.ToString() : element.GetRawText(),
                JsonValueKind.String => element.GetString() ?? string.Empty,
                _ => element.GetRawText()
            };
        }

        private async Task WriteJsonResponseAsync(NetworkStream stream, int statusCode, string statusText, object payload, CancellationToken cancellationToken)
        {
            string json = JsonSerializer.Serialize(payload, _jsonOptions);
            await WriteResponseAsync(stream, statusCode, statusText, "application/json; charset=utf-8", json, cancellationToken);
        }

        private async Task WriteHtmlResponseAsync(NetworkStream stream, string html, CancellationToken cancellationToken)
        {
            await WriteResponseAsync(stream, 200, "OK", "text/html; charset=utf-8", html, cancellationToken);
        }

        private async Task WriteTextResponseAsync(NetworkStream stream, int statusCode, string statusText, string text, CancellationToken cancellationToken)
        {
            await WriteResponseAsync(stream, statusCode, statusText, "text/plain; charset=utf-8", text, cancellationToken);
        }

        private static async Task WriteResponseAsync(NetworkStream stream, int statusCode, string statusText, string contentType, string content, CancellationToken cancellationToken)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(content);
            string headers =
                $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                $"Content-Type: {contentType}\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "Cache-Control: no-store\r\n" +
                "Connection: close\r\n" +
                "\r\n";

            byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
            await stream.WriteAsync(headerBytes, cancellationToken);
            await stream.WriteAsync(bodyBytes, cancellationToken);
        }

        private static IReadOnlyList<string> GetLocalUrls(int port)
        {
            var urls = new List<string> { $"http://127.0.0.1:{port}/" };

            foreach (IPAddress address in GetLocalIPv4Addresses())
            {
                string url = $"http://{address}:{port}/";
                if (!urls.Contains(url, StringComparer.OrdinalIgnoreCase))
                    urls.Add(url);
            }

            return urls;
        }

        private static IEnumerable<IPAddress> GetLocalIPv4Addresses()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up)
                    .Where(networkInterface => networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => address.Address)
                    .Where(address => !IPAddress.IsLoopback(address))
                    .OrderByDescending(IsPrivateIPv4Address)
                    .ThenBy(address => address.ToString())
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not get local IP addresses: {ex.Message}");
                return Array.Empty<IPAddress>();
            }
        }

        private static bool IsPrivateIPv4Address(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                   bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31 ||
                   bytes[0] == 192 && bytes[1] == 168;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
