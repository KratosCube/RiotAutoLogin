using RiotAutoLogin.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
            using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

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

                string body = await ReadBodyAsync(reader, contentLength);

                if (method == "GET" && (path == "/" || path.StartsWith("/?", StringComparison.Ordinal)))
                    await WriteHtmlResponseAsync(stream, RemotePickPageHtml.Get(), cancellationToken);
                else if (method == "GET" && path.StartsWith("/api/timer", StringComparison.OrdinalIgnoreCase))
                    await WriteJsonResponseAsync(stream, 200, "OK", await GetTimerStateAsync(), cancellationToken);
                else if (method == "GET" && path.StartsWith("/api/rune-debug", StringComparison.OrdinalIgnoreCase))
                    await WriteJsonResponseAsync(stream, 200, "OK", await GetRuneDebugAsync(), cancellationToken);
                else if (method == "GET" && path.StartsWith("/api/state", StringComparison.OrdinalIgnoreCase))
                    await WriteJsonResponseAsync(stream, 200, "OK", await GetStateAsync(), cancellationToken);
                else if (method == "POST" && path.StartsWith("/api/pick", StringComparison.OrdinalIgnoreCase))
                    await WriteActionResponseAsync(stream, await PickChampionAsync(body), cancellationToken);
                else if (method == "POST" && path.StartsWith("/api/ban", StringComparison.OrdinalIgnoreCase))
                    await WriteActionResponseAsync(stream, await BanChampionAsync(body), cancellationToken);
                else if (method == "POST" && path.StartsWith("/api/hover", StringComparison.OrdinalIgnoreCase))
                    await WriteActionResponseAsync(stream, await HoverChampionAsync(body), cancellationToken);
                else if (method == "POST" && path.StartsWith("/api/spell", StringComparison.OrdinalIgnoreCase))
                    await WriteActionResponseAsync(stream, await SelectSpellAsync(body), cancellationToken);
                else if (method == "POST" && path.StartsWith("/api/recommended-rune-page", StringComparison.OrdinalIgnoreCase))
                    await WriteActionResponseAsync(stream, await SelectRecommendedRunePageAsync(body), cancellationToken);
                else if (method == "POST" && path.StartsWith("/api/rune-page", StringComparison.OrdinalIgnoreCase))
                    await WriteActionResponseAsync(stream, await SelectRunePageAsync(body), cancellationToken);
                else if (method == "POST" && path.StartsWith("/api/leave", StringComparison.OrdinalIgnoreCase))
                    await WriteActionResponseAsync(stream, await LeaveLobbyAsync(), cancellationToken);
                else
                    await WriteTextResponseAsync(stream, 404, "Not Found", "Not found", cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Remote Pick request error: {ex.Message}");
                try { await WriteTextResponseAsync(stream, 500, "Internal Server Error", "Internal server error", cancellationToken); }
                catch { }
            }
            finally
            {
                client.Close();
            }
        }

        private static async Task<string> ReadBodyAsync(StreamReader reader, int contentLength)
        {
            if (contentLength <= 0)
                return string.Empty;

            char[] buffer = new char[contentLength];
            int totalRead = 0;
            while (totalRead < contentLength)
            {
                int read = await reader.ReadAsync(buffer, totalRead, contentLength - totalRead);
                if (read == 0)
                    break;
                totalRead += read;
            }

            return new string(buffer, 0, totalRead);
        }

        public async Task<RemotePickState> GetStateAsync()
        {
            RemotePickState state = await GetTimerStateAsync();
            var pickedChampionIds = new HashSet<int>();

            if (state.IsInChampSelect && LCUService.CheckIfLeagueClientIsOpen())
            {
                string[] sessionResult = LCUService.ClientRequest("GET", "lol-champ-select/v1/session");
                if (sessionResult[0] == "200")
                {
                    using JsonDocument doc = JsonDocument.Parse(sessionResult[1]);
                    ParsePicks(doc.RootElement, pickedChampionIds);
                    state.PickedChampionIds = pickedChampionIds.OrderBy(id => id).ToList();
                }
            }

            if (state.IsInChampSelect)
                await AddChampionSpellAndRuneListsAsync(state, new HashSet<int>(state.BannedChampionIds), pickedChampionIds);

            return state;
        }

        public async Task<RemotePickState> GetTimerStateAsync()
        {
            var state = new RemotePickState();

            if (!LCUService.CheckIfLeagueClientIsOpen())
            {
                state.PhaseLabel = "League Closed";
                state.Message = "League Client is not running.";
                return state;
            }

            state.Phase = await LCUService.GetCurrentGamePhaseAsync();
            state.IsInChampSelect = state.Phase == "ChampSelect";
            state.CanLeave = state.Phase == "Lobby" || state.Phase == "Matchmaking" || state.Phase == "ReadyCheck" || state.Phase == "ChampSelect";
            TryLoadQueueFromGameflowOrLobby(state);
            TryReadLiveGameState(state);

            if (!state.IsInChampSelect)
            {
                string queueSuffix = string.IsNullOrWhiteSpace(state.QueueName) ? string.Empty : $" ({state.QueueName})";
                if (state.Phase == "GameStart" || state.Phase == "InProgress")
                {
                    if (state.IsGameLoaded)
                    {
                        state.PhaseLabel = "In Game";
                        state.GameStatus = "InGame";
                        state.Message = $"In game{queueSuffix}. Game time: {FormatGameTime(state.GameTimeSeconds)}.";
                    }
                    else if (state.IsGameClientRunning)
                    {
                        state.PhaseLabel = "Loading Screen";
                        state.GameStatus = "Loading";
                        state.Message = $"Loading screen{queueSuffix}. Waiting for Live Client Data API...";
                    }
                    else
                    {
                        state.PhaseLabel = "Starting Game";
                        state.GameStatus = "Starting";
                        state.Message = $"Starting game{queueSuffix}. Waiting for League of Legends.exe...";
                    }
                    return state;
                }

                state.PhaseLabel = GetGameflowPhaseLabel(state.Phase);
                state.Message = state.Phase == "None"
                    ? "Waiting for League Client state..."
                    : $"Current phase: {state.Phase}{queueSuffix}. Waiting for champion select.";
                return state;
            }

            string[] sessionResult = LCUService.ClientRequest("GET", "lol-champ-select/v1/session");
            if (sessionResult[0] != "200")
            {
                state.PhaseLabel = "Champ Select";
                state.Message = $"Could not read champion select session. Status: {sessionResult[0]}";
                return state;
            }

            using JsonDocument doc = JsonDocument.Parse(sessionResult[1]);
            JsonElement root = doc.RootElement;
            var bannedChampionIds = new HashSet<int>();
            ParseBans(root, bannedChampionIds);
            ParseLocalPlayerSelection(root, state);
            ParseQueueAndMode(root, state);
            ParseAvailableChampionIds(root, state);
            ParseAvailableSpellIds(root, state);
            ParseTimer(root, state);
            ParseActions(root, state);
            state.BannedChampionIds = bannedChampionIds.OrderBy(id => id).ToList();
            state.PhaseLabel = GetChampSelectPhaseLabel(state, bannedChampionIds);
            string modePrefix = string.IsNullOrWhiteSpace(state.QueueName) ? string.Empty : $"{state.QueueName}: ";
            state.Message = state.IsMyTurn
                ? state.ActionType == "pick" ? $"{modePrefix}It is your pick. Lock in or hover a champion."
                    : state.ActionType == "ban" ? $"{modePrefix}It is your ban. Choose a champion to ban."
                    : $"{modePrefix}It is your {state.ActionType} turn."
                : state.IsRandomChampionMode && state.AvailableChampionIds.Count > 0
                    ? $"{modePrefix}Showing your available ARAM/random champions."
                    : $"{modePrefix}You can hover an intended pick while waiting for your turn.";

            return state;
        }

        public async Task<object> GetRuneDebugAsync()
        {
            RemotePickState state = await GetTimerStateAsync();
            int championId = state.SelectedChampionId > 0 ? state.SelectedChampionId : state.PickIntentChampionId;
            string position = NormalizePosition(state.AssignedPosition);
            int mapId = state.MapId > 0 ? state.MapId : 11;
            var endpointResults = new List<object>();

            void Probe(string endpoint)
            {
                string[] result = LCUService.ClientRequest("GET", endpoint);
                string body = result.Length > 1 ? result[1] : string.Empty;
                endpointResults.Add(new { endpoint, status = result[0], preview = PreviewResponse(body), parsedCount = result[0] == "200" ? ParseRecommendedRunePages(body).Count : 0 });
            }

            if (LCUService.CheckIfLeagueClientIsOpen())
            {
                Probe("lol-champ-select/v1/session");
                Probe("lol-perks/v1/currentpage");
                Probe("lol-perks/v1/pages");
                Probe("lol-perks/v1/styles");
                Probe("lol-perks/v1/inventory");
                Probe("lol-perks/v1/recommended-pages");

                if (championId > 0)
                {
                    foreach (string endpoint in GetRecommendedRuneEndpoints(championId, position, mapId))
                        Probe(endpoint);
                }
            }

            return new
            {
                phase = state.Phase,
                champSelectPhase = state.ChampSelectPhase,
                championId,
                selectedChampionId = state.SelectedChampionId,
                pickIntentChampionId = state.PickIntentChampionId,
                assignedPosition = state.AssignedPosition,
                normalizedPosition = position,
                mapId = state.MapId,
                effectiveMapId = mapId,
                queueId = state.QueueId,
                queueName = state.QueueName,
                champSelectMode = state.ChampSelectMode,
                isRandomChampionMode = state.IsRandomChampionMode,
                availableChampionIds = state.AvailableChampionIds,
                isGameClientRunning = state.IsGameClientRunning,
                isGameLoaded = state.IsGameLoaded,
                gameTimeSeconds = state.GameTimeSeconds,
                gameStatus = state.GameStatus,
                endpoints = endpointResults
            };
        }

        public async Task<RemotePickActionResult> PickChampionAsync(string body)
        {
            RemotePickRequest? request = ParseChampionRequest(body);
            if (request == null)
                return Failure("Invalid champion.");

            RemotePickState state = await GetTimerStateAsync();
            if (!state.IsInChampSelect)
                return Failure("You are not in champion select.");
            if (!state.CanPick)
                return Failure("It is not your pick turn.");
            if (state.BannedChampionIds.Contains(request.ChampionId))
                return Failure("This champion is banned.");
            if (state.IsRandomChampionMode && state.AvailableChampionIds.Count > 0 && !state.AvailableChampionIds.Contains(request.ChampionId))
                return Failure("This champion is not available in the current ARAM/random selection.");

            bool success = LCUService.SelectChampion(request.ChampionId, state.ActionId, complete: true);
            return new RemotePickActionResult { Success = success, Message = success ? "Champion locked in." : "League Client rejected the pick request." };
        }

        public async Task<RemotePickActionResult> BanChampionAsync(string body)
        {
            RemotePickRequest? request = ParseChampionRequest(body);
            if (request == null)
                return Failure("Invalid champion.");

            RemotePickState state = await GetTimerStateAsync();
            if (!state.IsInChampSelect)
                return Failure("You are not in champion select.");
            if (!state.CanBan)
                return Failure("It is not your ban turn.");
            if (state.BannedChampionIds.Contains(request.ChampionId))
                return Failure("This champion is already banned.");

            bool success = LCUService.SelectChampion(request.ChampionId, state.ActionId, complete: true);
            return new RemotePickActionResult { Success = success, Message = success ? "Champion banned." : "League Client rejected the ban request." };
        }

        public async Task<RemotePickActionResult> HoverChampionAsync(string body)
        {
            RemotePickRequest? request = ParseChampionRequest(body);
            if (request == null)
                return Failure("Invalid champion.");

            RemotePickState state = await GetTimerStateAsync();
            if (!state.IsInChampSelect)
                return Failure("You are not in champion select.");
            if (state.BannedChampionIds.Contains(request.ChampionId))
                return Failure("This champion is banned.");
            if (state.IsRandomChampionMode && state.AvailableChampionIds.Count > 0 && !state.AvailableChampionIds.Contains(request.ChampionId))
                return Failure("This champion is not available in the current ARAM/random selection.");

            bool success = false;
            if (!string.IsNullOrWhiteSpace(state.PickActionId))
                success = LCUService.SelectChampion(request.ChampionId, state.PickActionId, complete: false);
            if (!success && !string.IsNullOrWhiteSpace(state.ActionId) && state.ActionType == "pick")
                success = LCUService.SelectChampion(request.ChampionId, state.ActionId, complete: false);
            if (!success)
                success = TryPatchMySelection(new { championPickIntent = request.ChampionId });
            if (!success)
                success = TryPatchMySelection(new { championId = request.ChampionId });

            return new RemotePickActionResult { Success = success, Message = success ? "Champion intent updated." : "League Client rejected the hover request." };
        }

        public async Task<RemotePickActionResult> SelectSpellAsync(string body)
        {
            RemoteSpellRequest? request;
            try { request = JsonSerializer.Deserialize<RemoteSpellRequest>(body, _jsonOptions); }
            catch { return Failure("Invalid spell request."); }

            if (request == null || request.SpellId <= 0 || (request.Slot != 1 && request.Slot != 2))
                return Failure("Invalid summoner spell.");
            if (!LCUService.CheckIfLeagueClientIsOpen())
                return Failure("League Client is not running.");

            RemotePickState state = await GetTimerStateAsync();
            if (state.AvailableSpellIds.Count > 0 && !state.AvailableSpellIds.Contains(request.SpellId) && request.SpellId != state.Spell1Id && request.SpellId != state.Spell2Id)
                return Failure("This summoner spell is not available in the current queue.");

            bool success = LCUService.SelectSummonerSpell(request.SpellId, request.Slot);
            return new RemotePickActionResult { Success = success, Message = success ? "Summoner spell updated." : "League Client rejected the spell change." };
        }

        public Task<RemotePickActionResult> SelectRunePageAsync(string body)
        {
            RemoteRunePageRequest? request;
            try { request = JsonSerializer.Deserialize<RemoteRunePageRequest>(body, _jsonOptions); }
            catch { return Task.FromResult(Failure("Invalid rune page request.")); }

            if (request == null || request.PageId <= 0)
                return Task.FromResult(Failure("Invalid rune page."));
            if (!LCUService.CheckIfLeagueClientIsOpen())
                return Task.FromResult(Failure("League Client is not running."));

            string[] result = LCUService.ClientRequest("POST", $"lol-perks/v1/pages/{request.PageId}/current");
            bool success = result[0].StartsWith("2");
            return Task.FromResult(new RemotePickActionResult { Success = success, Message = success ? "Rune page selected." : $"League Client rejected rune page change. Status: {result[0]}" });
        }

        public Task<RemotePickActionResult> SelectRecommendedRunePageAsync(string body)
        {
            if (!LCUService.CheckIfLeagueClientIsOpen())
                return Task.FromResult(Failure("League Client is not running."));

            string[] result = LCUService.ClientRequest("POST", "lol-perks/v1/rune-recommender-auto-select");
            bool success = result[0].StartsWith("2");
            return Task.FromResult(new RemotePickActionResult
            {
                Success = success,
                Message = success ? "Riot recommended rune page applied." : $"League Client rejected Riot recommended runes. Status: {result[0]}"
            });
        }

        public async Task<RemotePickActionResult> LeaveLobbyAsync()
        {
            if (!LCUService.CheckIfLeagueClientIsOpen())
                return Failure("League Client is not running.");

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
                if (!result[0].StartsWith("2"))
                    result = LCUService.ClientRequest("DELETE", "lol-lobby/v2/lobby");
                return RequestLeave("ChampSelect", result);
            }

            return Failure($"Cannot leave during phase: {phase}");
        }

        private async Task AddChampionSpellAndRuneListsAsync(RemotePickState state, HashSet<int> bannedChampionIds, HashSet<int> pickedChampionIds)
        {
            await EnsureRemoteChampionCacheAsync();
            await EnsureRemoteSpellCacheAsync();

            var availableChampionIds = new HashSet<int>(state.AvailableChampionIds);
            bool shouldFilterRandomChampions = state.IsRandomChampionMode && availableChampionIds.Count > 0;

            state.Champions = (_remoteChampionCache ?? new List<RemoteChampionDto>())
                .Where(champion => !shouldFilterRandomChampions || availableChampionIds.Contains(champion.Id) || state.SelectedChampionId == champion.Id || state.PickIntentChampionId == champion.Id || pickedChampionIds.Contains(champion.Id))
                .Select(champion => new RemoteChampionDto
                {
                    Id = champion.Id,
                    Name = champion.Name,
                    ImageUrl = champion.ImageUrl,
                    IsBanned = bannedChampionIds.Contains(champion.Id),
                    IsPicked = pickedChampionIds.Contains(champion.Id),
                    IsSelected = state.SelectedChampionId == champion.Id,
                    IsIntent = state.PickIntentChampionId == champion.Id,
                    IsAvailableInCurrentMode = !shouldFilterRandomChampions || availableChampionIds.Contains(champion.Id) || state.SelectedChampionId == champion.Id || state.PickIntentChampionId == champion.Id
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
                    IsAvailable = state.AvailableSpellIds.Count == 0 || state.AvailableSpellIds.Contains(spell.Id) || state.Spell1Id == spell.Id || state.Spell2Id == spell.Id
                })
                .ToList();

            AddRunePages(state);
            AddRecommendedRunePages(state);
        }

        private void AddRunePages(RemotePickState state)
        {
            if (!LCUService.CheckIfLeagueClientIsOpen())
                return;

            string[] pagesResult = LCUService.ClientRequest("GET", "lol-perks/v1/pages");
            if (pagesResult[0] != "200")
                return;

            long currentPageId = GetCurrentRunePageId();
            try
            {
                using JsonDocument pagesDoc = JsonDocument.Parse(pagesResult[1]);
                if (pagesDoc.RootElement.ValueKind != JsonValueKind.Array)
                    return;

                foreach (JsonElement page in pagesDoc.RootElement.EnumerateArray())
                {
                    if (!TryGetPropertyLong(page, "id", out long pageId))
                        continue;

                    var dto = new RemoteRunePageDto
                    {
                        Id = pageId,
                        Name = GetStringProperty(page, "name") ?? $"Rune Page {pageId}",
                        IsCurrent = currentPageId == pageId || GetBoolProperty(page, "current") || GetBoolProperty(page, "isCurrent"),
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

        private void AddRecommendedRunePages(RemotePickState state)
        {
            int championId = state.SelectedChampionId > 0 ? state.SelectedChampionId : state.PickIntentChampionId;
            if (championId <= 0 || !LCUService.CheckIfLeagueClientIsOpen())
                return;

            var candidates = new List<RemoteRecommendedRunePageDto>();
            string position = NormalizePosition(state.AssignedPosition);
            int mapId = state.MapId > 0 ? state.MapId : 11;

            string[] sessionResult = LCUService.ClientRequest("GET", "lol-champ-select/v1/session");
            if (sessionResult[0] == "200")
                candidates.AddRange(ParseRecommendedRunePages(sessionResult[1]));

            foreach (string endpoint in GetRecommendedRuneEndpoints(championId, position, mapId))
            {
                string[] result = LCUService.ClientRequest("GET", endpoint);
                if (result[0] == "200")
                    candidates.AddRange(ParseRecommendedRunePages(result[1]));
            }

            state.RecommendedRunePages = candidates
                .Where(page => page.CanApply)
                .GroupBy(page => string.Join(',', page.SelectedPerkIds))
                .Select(group => group.First())
                .Take(3)
                .Select((page, index) => { page.Index = index; return page; })
                .ToList();
        }

        private static IEnumerable<string> GetRecommendedRuneEndpoints(int championId, string position, int mapId)
        {
            string safePosition = string.IsNullOrWhiteSpace(position) ? "UNKNOWN" : position;
            int safeMapId = mapId > 0 ? mapId : 11;

            yield return $"lol-perks/v1/recommended-pages?championId={championId}&position={safePosition}&mapId={safeMapId}";
            yield return $"lol-perks/v1/recommended-pages/champion/{championId}/position/{safePosition}/map/{safeMapId}";
            yield return $"lol-perks/v1/recommended-pages/champion/{championId}/position/{safePosition}";
            yield return $"lol-perks/v1/recommended-pages/champion/{championId}/map/{safeMapId}";
            yield return $"lol-perks/v1/recommended-pages/champion/{championId}";
            yield return $"lol-perks/v1/recommended-pages/{championId}/{safePosition}/{safeMapId}";
            yield return $"lol-perks/v1/recommended-pages/{championId}";
            yield return "lol-perks/v1/recommended-pages";
        }

        private static List<RemoteRecommendedRunePageDto> ParseRecommendedRunePages(string json)
        {
            var pages = new List<RemoteRecommendedRunePageDto>();
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                CollectRecommendedPages(doc.RootElement, pages);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not parse recommended rune pages: {ex.Message}");
            }

            for (int i = 0; i < pages.Count; i++)
                pages[i].Index = i;

            return pages.Where(page => page.CanApply).Take(3).ToList();
        }

        private static void CollectRecommendedPages(JsonElement element, List<RemoteRecommendedRunePageDto> pages)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                TryAddRecommendedPage(element, pages);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Object || property.Value.ValueKind == JsonValueKind.Array)
                        CollectRecommendedPages(property.Value, pages);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                    CollectRecommendedPages(item, pages);
            }
        }

        private static void TryAddRecommendedPage(JsonElement pageElement, List<RemoteRecommendedRunePageDto> pages)
        {
            JsonElement source = pageElement;
            if (pageElement.TryGetProperty("page", out JsonElement nestedPage) && nestedPage.ValueKind == JsonValueKind.Object)
                source = nestedPage;
            else if (pageElement.TryGetProperty("runePage", out JsonElement nestedRunePage) && nestedRunePage.ValueKind == JsonValueKind.Object)
                source = nestedRunePage;

            var dto = new RemoteRecommendedRunePageDto
            {
                Name = GetStringProperty(source, "name", "title", "pageName", "displayName", "recommendationId") ?? GetStringProperty(pageElement, "name", "title", "pageName", "displayName", "recommendationId") ?? "Recommended",
                Subtitle = GetStringProperty(source, "position") ?? GetStringProperty(pageElement, "subtitle", "description", "position") ?? string.Empty,
                PrimaryStyleId = GetIntProperty(source, "primaryStyleId", "primaryStyle", "primaryPath", "primaryTreeId", "primaryPerkStyleId"),
                SubStyleId = GetIntProperty(source, "subStyleId", "secondaryStyleId", "secondaryStyle", "subStyle", "secondaryPath", "secondaryTreeId", "secondaryPerkStyleId"),
                SelectedPerkIds = GetPerkIds(source)
            };

            if (dto.CanApply && pages.All(existing => !existing.SelectedPerkIds.SequenceEqual(dto.SelectedPerkIds)))
                pages.Add(dto);
        }

        private static List<int> GetPerkIds(JsonElement pageElement)
        {
            foreach (string arrayName in new[] { "selectedPerkIds", "selectedPerks", "perkIds", "perks", "runes", "runeIds" })
            {
                if (pageElement.TryGetProperty(arrayName, out JsonElement arrayElement) && arrayElement.ValueKind == JsonValueKind.Array)
                    return ReadIntArray(arrayElement);
            }

            var ids = new List<int>();
            CollectPerkIdsFromNamedArrays(pageElement, ids);
            return ids.Distinct().ToList();
        }

        private static void CollectPerkIdsFromNamedArrays(JsonElement element, List<int> ids)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if ((property.Name.Contains("perk", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("rune", StringComparison.OrdinalIgnoreCase)) && property.Value.ValueKind == JsonValueKind.Array)
                        ids.AddRange(ReadIntArray(property.Value));
                    else if (property.Value.ValueKind == JsonValueKind.Object || property.Value.ValueKind == JsonValueKind.Array)
                        CollectPerkIdsFromNamedArrays(property.Value, ids);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in element.EnumerateArray())
                    CollectPerkIdsFromNamedArrays(child, ids);
            }
        }

        private static List<int> ReadIntArray(JsonElement arrayElement)
        {
            var ids = new List<int>();
            foreach (JsonElement item in arrayElement.EnumerateArray())
            {
                if (TryGetInt(item, out int id) && id > 0)
                    ids.Add(id);
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    foreach (string propertyName in new[] { "id", "perkId", "runeId", "perk" })
                    {
                        if (TryGetPropertyInt(item, propertyName, out int objectId) && objectId > 0)
                        {
                            ids.Add(objectId);
                            break;
                        }
                    }
                }
            }
            return ids;
        }

        private static long GetCurrentRunePageId()
        {
            string[] currentPageResult = LCUService.ClientRequest("GET", "lol-perks/v1/currentpage");
            if (currentPageResult[0] != "200")
                return 0;

            try
            {
                using JsonDocument currentDoc = JsonDocument.Parse(currentPageResult[1]);
                if (TryGetPropertyLong(currentDoc.RootElement, "id", out long pageId))
                    return pageId;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not parse current rune page: {ex.Message}");
            }
            return 0;
        }

        private async Task EnsureRemoteChampionCacheAsync()
        {
            if (_remoteChampionCache != null)
                return;

            _championCache ??= await DataDragonService.GetAllChampionsAsync();
            var remoteChampions = new List<RemoteChampionDto>();
            foreach (ChampionModel champion in _championCache.Where(champion => champion.Id > 0).OrderBy(champion => champion.Name))
            {
                string imageUrl = string.Empty;
                try { imageUrl = await DataDragonService.GetChampionImageUrlAsync(champion.Name); }
                catch (Exception ex) { Debug.WriteLine($"Could not get champion image URL for {champion.Name}: {ex.Message}"); }
                remoteChampions.Add(new RemoteChampionDto { Id = champion.Id, Name = champion.Name, ImageUrl = imageUrl });
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
                string name = spell.Name ?? $"Spell {spell.Id}";
                string imageUrl = spell.ImageUrl ?? string.Empty;
                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    try { imageUrl = await DataDragonService.GetSummonerSpellImageUrlAsync(name); }
                    catch (Exception ex) { Debug.WriteLine($"Could not get summoner spell image URL for {name}: {ex.Message}"); }
                }

                remoteSpells.Add(new RemoteSummonerSpellDto { Id = spell.Id, Name = name, Description = spell.Description ?? string.Empty, ImageUrl = imageUrl });
            }

            _remoteSpellCache = remoteSpells;
        }

        private static void MergeDefaultSummonerSpells(List<SummonerSpellModel> spells)
        {
            SummonerSpellModel[] defaults =
            {
                new() { Name = "Cleanse", Id = 1 },
                new() { Name = "Exhaust", Id = 3 },
                new() { Name = "Flash", Id = 4 },
                new() { Name = "Ghost", Id = 6 },
                new() { Name = "Heal", Id = 7 },
                new() { Name = "Smite", Id = 11 },
                new() { Name = "Teleport", Id = 12 },
                new() { Name = "Clarity", Id = 13 },
                new() { Name = "Ignite", Id = 14 },
                new() { Name = "Barrier", Id = 21 },
                new() { Name = "Mark", Id = 32 }
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
            if (TryGetPropertyInt(root, "mapId", out int mapId)) state.MapId = mapId;
            if (TryGetPropertyInt(root, "queueId", out int queueId)) state.QueueId = queueId;

            if (!TryGetPropertyInt(root, "localPlayerCellId", out int localPlayerCellId))
                return;
            if (!root.TryGetProperty("myTeam", out JsonElement myTeamElement) || myTeamElement.ValueKind != JsonValueKind.Array)
                return;

            foreach (JsonElement player in myTeamElement.EnumerateArray())
            {
                if (!TryGetPropertyInt(player, "cellId", out int cellId) || cellId != localPlayerCellId)
                    continue;

                if (TryGetPropertyInt(player, "championId", out int championId)) state.SelectedChampionId = championId;
                if (TryGetPropertyInt(player, "championPickIntent", out int intentChampionId)) state.PickIntentChampionId = intentChampionId;
                if (TryGetPropertyInt(player, "spell1Id", out int spell1Id)) state.Spell1Id = spell1Id;
                if (TryGetPropertyInt(player, "spell2Id", out int spell2Id)) state.Spell2Id = spell2Id;
                state.AssignedPosition = GetStringProperty(player, "assignedPosition", "position") ?? string.Empty;
                return;
            }
        }

        private static void ParseQueueAndMode(JsonElement root, RemotePickState state)
        {
            if (TryGetPropertyInt(root, "mapId", out int mapId)) state.MapId = mapId;
            if (TryGetPropertyInt(root, "queueId", out int queueId)) state.QueueId = queueId;

            bool randomSignals = GetBoolProperty(root, "benchEnabled") || GetBoolProperty(root, "allowRerolling") || GetBoolProperty(root, "allowSubsetChampionPicks");
            state.IsRandomChampionMode = randomSignals || IsKnownRandomChampionQueue(state.QueueId);
            UpdateModeLabels(state);
        }

        private static void ParseAvailableChampionIds(JsonElement root, RemotePickState state)
        {
            var championIds = new HashSet<int>();
            CollectAvailableChampionIds(root, championIds);

            if (state.SelectedChampionId > 0) championIds.Add(state.SelectedChampionId);
            if (state.PickIntentChampionId > 0) championIds.Add(state.PickIntentChampionId);

            state.AvailableChampionIds = championIds.Where(id => id > 0).OrderBy(id => id).ToList();
        }

        private static void CollectAvailableChampionIds(JsonElement element, HashSet<int> championIds)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string name = property.Name.ToLowerInvariant();
                    bool looksLikeAvailableChampions = name == "benchchampions" ||
                        (name.Contains("champion") && (name.Contains("available") || name.Contains("allowable") || name.Contains("pickable") || name.Contains("bench") || name.Contains("unlocked") || name.Contains("subset")));

                    if (looksLikeAvailableChampions && property.Value.ValueKind == JsonValueKind.Array)
                        AddChampionIdsFromFlexibleArray(property.Value, championIds);
                    else if (property.Value.ValueKind == JsonValueKind.Object || property.Value.ValueKind == JsonValueKind.Array)
                        CollectAvailableChampionIds(property.Value, championIds);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in element.EnumerateArray())
                    CollectAvailableChampionIds(child, championIds);
            }
        }

        private static void AddChampionIdsFromFlexibleArray(JsonElement arrayElement, HashSet<int> championIds)
        {
            if (arrayElement.ValueKind != JsonValueKind.Array)
                return;

            foreach (JsonElement item in arrayElement.EnumerateArray())
            {
                if (TryGetInt(item, out int id) && id > 0)
                {
                    championIds.Add(id);
                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (string propertyName in new[] { "championId", "id", "champion" })
                {
                    if (TryGetPropertyInt(item, propertyName, out int objectId) && objectId > 0)
                    {
                        championIds.Add(objectId);
                        break;
                    }
                }
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

            state.ChampSelectPhase = GetStringProperty(timerElement, "phase") ?? string.Empty;
            if (TryGetPropertyInt(timerElement, "adjustedTimeLeftInPhase", out int adjustedLeftMs))
                state.TimeLeftInPhaseMs = Math.Max(0, adjustedLeftMs);
            else if (TryGetPropertyInt(timerElement, "timeLeftInPhase", out int timeLeftMs))
                state.TimeLeftInPhaseMs = Math.Max(0, timeLeftMs);
            if (TryGetPropertyInt(timerElement, "totalTimeInPhase", out int totalMs))
                state.TotalTimeInPhaseMs = Math.Max(0, totalMs);
            state.IsTimerInfinite = GetBoolProperty(timerElement, "isInfinite");
        }

        private static void ParseActions(JsonElement root, RemotePickState state)
        {
            if (!TryGetPropertyInt(root, "localPlayerCellId", out int localPlayerCellId))
                return;
            if (!root.TryGetProperty("actions", out JsonElement actionsElement) || actionsElement.ValueKind != JsonValueKind.Array)
                return;

            int groupIndex = 0;
            foreach (JsonElement actionGroup in actionsElement.EnumerateArray())
            {
                if (actionGroup.ValueKind != JsonValueKind.Array)
                {
                    groupIndex++;
                    continue;
                }

                var activeIds = new List<string>();
                string activeType = string.Empty;

                foreach (JsonElement action in actionGroup.EnumerateArray())
                {
                    string actionType = GetStringProperty(action, "type")?.ToLowerInvariant() ?? string.Empty;
                    string actionId = action.TryGetProperty("id", out JsonElement idElement) ? GetJsonValueAsString(idElement) : string.Empty;
                    bool inProgress = GetBoolProperty(action, "isInProgress");
                    if (inProgress)
                    {
                        activeIds.Add(actionId);
                        if (string.IsNullOrWhiteSpace(activeType)) activeType = actionType;
                    }

                    if (!TryGetPropertyInt(action, "actorCellId", out int actorCellId) || actorCellId != localPlayerCellId)
                        continue;
                    if (actionType == "pick" && string.IsNullOrWhiteSpace(state.PickActionId))
                        state.PickActionId = actionId;
                    if (!inProgress)
                        continue;

                    state.IsMyTurn = true;
                    state.ActionId = actionId;
                    state.ActionType = actionType;
                    state.ActionGroupIndex = groupIndex;
                }

                if (activeIds.Count > 0 && string.IsNullOrWhiteSpace(state.TimerActionKey))
                {
                    state.ActionGroupIndex = groupIndex;
                    state.TimerActionKey = $"group:{groupIndex}|type:{activeType}|ids:{string.Join(',', activeIds)}";
                }

                groupIndex++;
            }

            if (string.IsNullOrWhiteSpace(state.TimerActionKey))
                state.TimerActionKey = $"phase:{state.ChampSelectPhase}|action:{state.ActionType}|id:{state.ActionId}";
        }

        private static void CollectAvailableSpellIds(JsonElement element, HashSet<int> spellIds)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    bool looksLikeSpellList = property.Name.Equals("allowableSpellIds", StringComparison.OrdinalIgnoreCase) ||
                                             property.Name.Equals("availableSpellIds", StringComparison.OrdinalIgnoreCase) ||
                                             property.Name.Equals("allowedSpellIds", StringComparison.OrdinalIgnoreCase);
                    if (looksLikeSpellList && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement idElement in property.Value.EnumerateArray())
                        {
                            if (TryGetInt(idElement, out int spellId) && spellId > 0) spellIds.Add(spellId);
                        }
                    }
                    else if (property.Value.ValueKind == JsonValueKind.Object || property.Value.ValueKind == JsonValueKind.Array)
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
                if (TryGetPropertyInt(player, "championId", out int championId) && championId > 0) championIds.Add(championId);
            }
        }

        private static void AddChampionIdsFromArray(JsonElement root, string propertyName, HashSet<int> championIds)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement idsElement) || idsElement.ValueKind != JsonValueKind.Array)
                return;
            foreach (JsonElement idElement in idsElement.EnumerateArray())
            {
                if (TryGetInt(idElement, out int championId) && championId > 0) championIds.Add(championId);
            }
        }

        private static void TryReadLiveGameState(RemotePickState state)
        {
            state.IsGameClientRunning = IsLeagueGameClientRunning();
            state.IsGameLoaded = false;
            state.GameTimeSeconds = -1;
            state.GameStatus = state.IsGameClientRunning ? "Loading" : "Waiting";

            if (TryReadLiveGameStats("https://127.0.0.1:2999/liveclientdata/gamestats", state))
                return;

            TryReadLiveGameStats("http://127.0.0.1:2999/liveclientdata/gamestats", state);
        }

        private static bool TryReadLiveGameStats(string url, RemotePickState state)
        {
            try
            {
                using var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(350) };
                string json = client.GetStringAsync(url).Result;
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                using JsonDocument doc = JsonDocument.Parse(json);
                state.IsGameClientRunning = true;
                state.IsGameLoaded = true;
                state.GameStatus = "InGame";
                if (TryGetPropertyDouble(doc.RootElement, "gameTime", out double gameTime))
                    state.GameTimeSeconds = Math.Max(0, gameTime);
                return true;
            }
            catch
            {
                state.GameStatus = state.IsGameClientRunning ? "Loading" : "Waiting";
                return false;
            }
        }

        private static bool IsLeagueGameClientRunning()
        {
            try
            {
                return Process.GetProcessesByName("League of Legends").Any();
            }
            catch
            {
                return false;
            }
        }

        private static string FormatGameTime(double seconds)
        {
            if (seconds < 0) return "--:--";
            int totalSeconds = (int)Math.Floor(seconds);
            int minutes = totalSeconds / 60;
            int remainderSeconds = totalSeconds % 60;
            return $"{minutes}:{remainderSeconds:00}";
        }

        private static void TryLoadQueueFromGameflowOrLobby(RemotePickState state)
        {
            TryLoadQueueFromEndpoint("lol-gameflow/v1/session", state);
            if (state.QueueId <= 0) TryLoadQueueFromEndpoint("lol-lobby/v2/lobby", state);
            UpdateModeLabels(state);
        }

        private static void TryLoadQueueFromEndpoint(string endpoint, RemotePickState state)
        {
            string[] result = LCUService.ClientRequest("GET", endpoint);
            if (result[0] != "200") return;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(result[1]);
                JsonElement root = doc.RootElement;

                if (TryGetPropertyInt(root, "queueId", out int queueId) && queueId > 0) state.QueueId = queueId;
                if (TryGetPropertyInt(root, "mapId", out int mapId) && mapId > 0) state.MapId = mapId;

                if (root.TryGetProperty("gameData", out JsonElement gameData) && gameData.ValueKind == JsonValueKind.Object)
                {
                    if (TryGetPropertyInt(gameData, "queueId", out int gdQueueId) && gdQueueId > 0) state.QueueId = gdQueueId;
                    if (TryGetPropertyInt(gameData, "mapId", out int gdMapId) && gdMapId > 0) state.MapId = gdMapId;
                    if (gameData.TryGetProperty("queue", out JsonElement queue)) ParseQueueObject(queue, state);
                }

                if (root.TryGetProperty("gameConfig", out JsonElement gameConfig) && gameConfig.ValueKind == JsonValueKind.Object)
                {
                    if (TryGetPropertyInt(gameConfig, "queueId", out int gcQueueId) && gcQueueId > 0) state.QueueId = gcQueueId;
                    if (TryGetPropertyInt(gameConfig, "mapId", out int gcMapId) && gcMapId > 0) state.MapId = gcMapId;
                }

                if (root.TryGetProperty("queue", out JsonElement queueRoot)) ParseQueueObject(queueRoot, state);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not parse queue info from {endpoint}: {ex.Message}");
            }
        }

        private static void ParseQueueObject(JsonElement queue, RemotePickState state)
        {
            if (queue.ValueKind != JsonValueKind.Object) return;
            if (TryGetPropertyInt(queue, "id", out int queueId) && queueId > 0) state.QueueId = queueId;
            if (TryGetPropertyInt(queue, "queueId", out int directQueueId) && directQueueId > 0) state.QueueId = directQueueId;
        }

        private static void UpdateModeLabels(RemotePickState state)
        {
            if (state.QueueId > 0 && IsKnownRandomChampionQueue(state.QueueId)) state.IsRandomChampionMode = true;

            state.QueueName = GetQueueName(state.QueueId, state.IsRandomChampionMode);
            if (state.IsRandomChampionMode) state.ChampSelectMode = "Random / ARAM";
            else if (state.QueueId == 400 || state.QueueId == 420 || state.QueueId == 440) state.ChampSelectMode = "Draft";
            else state.ChampSelectMode = string.IsNullOrWhiteSpace(state.QueueName) ? string.Empty : state.QueueName;
        }

        private static bool IsKnownRandomChampionQueue(int queueId)
        {
            return queueId == 450 || queueId == 720 || queueId == 900 || queueId == 1020 || queueId == 1300 || queueId == 1400 || queueId == 1700 || queueId == 1710 || queueId == 1900 || queueId == 3210 || queueId == 3270;
        }

        private static string GetQueueName(int queueId, bool randomMode)
        {
            return queueId switch
            {
                400 => "Normal Draft",
                420 => "Ranked Solo/Duo",
                430 => "Blind Pick",
                440 => "Ranked Flex",
                450 => "ARAM",
                700 => "Clash",
                720 => "ARAM Clash",
                900 => "ARURF",
                1020 => "One for All",
                1300 => "Nexus Blitz",
                1400 => "Ultimate Spellbook",
                1700 => "Arena",
                1710 => "Arena",
                1900 => "URF",
                3210 => "ARAM / Random",
                3270 => "ARAM / Random",
                0 => randomMode ? "Random Champion Mode" : string.Empty,
                _ => randomMode ? $"Random Mode ({queueId})" : $"Queue {queueId}"
            };
        }

        private static string GetGameflowPhaseLabel(string phase) => phase switch
        {
            "Lobby" => "Lobby",
            "Matchmaking" => "In Queue",
            "ReadyCheck" => "Match Found",
            "ChampSelect" => "Champ Select",
            "GameStart" => "Loading Screen",
            "InProgress" => "In Game",
            "WaitingForStats" => "Post Game",
            "PreEndOfGame" => "Post Game",
            "EndOfGame" => "Post Game",
            _ => "Waiting"
        };

        private static string GetChampSelectPhaseLabel(RemotePickState state, HashSet<int> bannedChampionIds)
        {
            if (state.CanBan) return "Ban Phase";
            if (state.CanPick) return "Pick Phase";
            string phase = state.ChampSelectPhase.ToUpperInvariant();
            if (phase.Contains("PLANNING")) return "Pre-Ban Planning";
            if (phase.Contains("BAN") && !phase.Contains("PICK")) return "Ban Phase";
            if (phase.Contains("BAN") || phase.Contains("PICK")) return bannedChampionIds.Count == 0 ? "Ban / Pick Phase" : "Pick Phase";
            if (phase.Contains("FINAL")) return "Finalize Loadout";
            return "Champ Select";
        }

        private static string NormalizePosition(string position)
        {
            if (string.IsNullOrWhiteSpace(position)) return "UNKNOWN";
            string normalized = position.Trim().ToUpperInvariant();
            return normalized switch
            {
                "UTILITY" => "UTILITY",
                "SUPPORT" => "UTILITY",
                "BOTTOM" => "BOTTOM",
                "ADC" => "BOTTOM",
                "MIDDLE" => "MIDDLE",
                "MID" => "MIDDLE",
                "JUNGLE" => "JUNGLE",
                "TOP" => "TOP",
                _ => normalized
            };
        }

        private static bool GetBoolProperty(JsonElement element, string propertyName)
            => element.TryGetProperty(propertyName, out JsonElement valueElement) && (valueElement.ValueKind == JsonValueKind.True || valueElement.ValueKind == JsonValueKind.False) && valueElement.GetBoolean();

        private static string? GetStringProperty(JsonElement element, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                if (element.TryGetProperty(propertyName, out JsonElement valueElement) && valueElement.ValueKind == JsonValueKind.String)
                    return valueElement.GetString();
            }
            return null;
        }

        private static int GetIntProperty(JsonElement element, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                if (TryGetPropertyInt(element, propertyName, out int value)) return value;
            }
            return 0;
        }

        private static bool TryGetPropertyInt(JsonElement element, string propertyName, out int value)
        {
            if (element.TryGetProperty(propertyName, out JsonElement valueElement)) return TryGetInt(valueElement, out value);
            value = 0;
            return false;
        }

        private static bool TryGetPropertyLong(JsonElement element, string propertyName, out long value)
        {
            if (element.TryGetProperty(propertyName, out JsonElement valueElement)) return TryGetLong(valueElement, out value);
            value = 0;
            return false;
        }

        private static bool TryGetPropertyDouble(JsonElement element, string propertyName, out double value)
        {
            if (element.TryGetProperty(propertyName, out JsonElement valueElement))
            {
                if (valueElement.ValueKind == JsonValueKind.Number) return valueElement.TryGetDouble(out value);
                if (valueElement.ValueKind == JsonValueKind.String) return double.TryParse(valueElement.GetString(), out value);
            }
            value = 0;
            return false;
        }

        private static bool TryGetInt(JsonElement element, out int value)
        {
            if (element.ValueKind == JsonValueKind.Number) return element.TryGetInt32(out value);
            if (element.ValueKind == JsonValueKind.String) return int.TryParse(element.GetString(), out value);
            value = 0;
            return false;
        }

        private static bool TryGetLong(JsonElement element, out long value)
        {
            if (element.ValueKind == JsonValueKind.Number) return element.TryGetInt64(out value);
            if (element.ValueKind == JsonValueKind.String) return long.TryParse(element.GetString(), out value);
            value = 0;
            return false;
        }

        private static string GetJsonValueAsString(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt32(out int number) ? number.ToString() : element.GetRawText(),
            JsonValueKind.String => element.GetString() ?? string.Empty,
            _ => element.GetRawText()
        };

        private static RemotePickActionResult Failure(string message) => new() { Success = false, Message = message };

        private static RemotePickActionResult RequestLeave(string phase, string[] result)
        {
            bool success = result[0].StartsWith("2");
            return new RemotePickActionResult { Success = success, Message = success ? "Leave request sent." : $"Leave failed during {phase}. Status: {result[0]}" };
        }

        private bool TryPatchMySelection(object payload)
        {
            string bodyJson = JsonSerializer.Serialize(payload, _jsonOptions);
            string[] result = LCUService.ClientRequest("PATCH", "lol-champ-select/v1/session/my-selection", bodyJson);
            return result[0].StartsWith("2");
        }

        private RemotePickRequest? ParseChampionRequest(string body)
        {
            try
            {
                RemotePickRequest? request = JsonSerializer.Deserialize<RemotePickRequest>(body, _jsonOptions);
                return request == null || request.ChampionId <= 0 ? null : request;
            }
            catch { return null; }
        }

        private async Task WriteActionResponseAsync(NetworkStream stream, RemotePickActionResult result, CancellationToken cancellationToken)
            => await WriteJsonResponseAsync(stream, result.Success ? 200 : 400, result.Success ? "OK" : "Bad Request", result, cancellationToken);

        private async Task WriteJsonResponseAsync(NetworkStream stream, int statusCode, string statusText, object payload, CancellationToken cancellationToken)
        {
            string json = JsonSerializer.Serialize(payload, _jsonOptions);
            await WriteResponseAsync(stream, statusCode, statusText, "application/json; charset=utf-8", json, cancellationToken);
        }

        private async Task WriteHtmlResponseAsync(NetworkStream stream, string html, CancellationToken cancellationToken)
            => await WriteResponseAsync(stream, 200, "OK", "text/html; charset=utf-8", html, cancellationToken);

        private async Task WriteTextResponseAsync(NetworkStream stream, int statusCode, string statusText, string text, CancellationToken cancellationToken)
            => await WriteResponseAsync(stream, statusCode, statusText, "text/plain; charset=utf-8", text, cancellationToken);

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

        private static string PreviewResponse(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return string.Empty;
            string compact = body.Replace("\r", " ").Replace("\n", " ").Trim();
            return compact.Length <= 2200 ? compact : compact[..2200] + "...";
        }

        private static IReadOnlyList<string> GetLocalUrls(int port)
        {
            var urls = new List<string> { $"http://127.0.0.1:{port}/" };
            foreach (IPAddress address in GetLocalIPv4Addresses())
            {
                string url = $"http://{address}:{port}/";
                if (!urls.Contains(url, StringComparer.OrdinalIgnoreCase)) urls.Add(url);
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
            return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) || (bytes[0] == 192 && bytes[1] == 168);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
