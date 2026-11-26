using RiotAutoLogin.Models;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
    public sealed partial class RemotePickServerService : IDisposable
    {
        public const int DefaultPort = 5055;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private TcpListener? _listener;
        private RemotePickDiscovery? _discovery;
        private CancellationTokenSource? _cts;
        private Task? _serverTask;
        private List<ChampionModel>? _championCache;
        private List<RemoteChampionDto>? _remoteChampionCache;
        private Dictionary<int, string[]> _championPositions = new();
        private DateTime _nextPositionsRefresh;
        private Dictionary<int, RemoteChampionDto>? _allChampionMetadata;
        private DateTime _nextChampionMetadataAttemptUtc;
        private List<RemoteSummonerSpellDto>? _remoteSpellCache;
        private readonly RemotePhaseClock _phaseClock = new();
        private readonly SemaphoreSlim _lobbyActionGate = new(1, 1);
        private sealed record RecommendedRuneCache(string Key, DateTime ExpiresUtc, List<RemoteRecommendedRunePageDto> Pages);
        private RecommendedRuneCache? _recommendedRuneCache;
        private readonly ConcurrentDictionary<string, DateTime> _unsupportedRuneEndpoints = new();
        private sealed record RuneMetadata(string Name, string IconUrl);
        private sealed record RuneMetadataCache(DateTime ExpiresUtc, Dictionary<int, RuneMetadata> Values);
        private RuneMetadataCache? _runeMetadataCache;
        private readonly ConcurrentDictionary<string, PlayerProfileCache> _playerProfiles = new();
        private sealed record PlayerProfileCache(string GameName, string TagLine, DateTime ExpiresUtc);
        private string _profileRegion = string.Empty;
        private DateTime _profileRegionExpiresUtc;
        private readonly SemaphoreSlim _banActionGate = new(1, 1);
        private readonly object _banFallbackSync = new();
        private CancellationTokenSource? _banFallbackCts;
        private string _banFallbackActionId = string.Empty;
        private int _banFallbackChampionId;

        public bool IsRunning { get; private set; }
        public int Port { get; private set; } = DefaultPort;
        public IReadOnlyList<string> LocalUrls => (_discovery is { IsRegistered: true } dns
            ? new[] { $"http://{dns.HostName}:{Port}/" }.Concat(GetLocalUrls(Port)) : GetLocalUrls(Port)).ToArray();
        public string LocalUrl => LocalUrls.FirstOrDefault(url => !url.Contains("127.0.0.1")) ?? LocalUrls.FirstOrDefault() ?? $"http://127.0.0.1:{Port}/";
        public string LocalUrlDisplay => string.Join(Environment.NewLine, LocalUrls);

        public async Task StartAsync(int port = DefaultPort)
        {
            if (IsRunning)
                return;

            Port = port;
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(Socket.OSSupportsIPv6 ? IPAddress.IPv6Any : IPAddress.Any, Port);
            if (Socket.OSSupportsIPv6) _listener.Server.DualMode = true;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            IsRunning = true;
            var serverToken = _cts.Token;
            _serverTask = AcceptLoopAsync(serverToken);
            Debug.WriteLine($"Remote Pick server started at:{Environment.NewLine}{LocalUrlDisplay}");
            _discovery = new RemotePickDiscovery();
            await _discovery.StartAsync(Port);
        }

        public void Stop()
        {
            if (!IsRunning)
                return;

            IsRunning = false;
            _discovery?.Dispose(); _discovery = null;
            CancelBanFallback(); InvalidateLoadout();
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

                    TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
                    // Some legacy LCU helpers still block; keep them off the accept loop.
                    _ = Task.Run(() => HandleClientAsync(client, cancellationToken));
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
                string? requestLine = await reader.ReadLineAsync(cancellationToken);
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
                bool isPageAction = false;
                string requestHost = "";

                string? headerLine;
                while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync(cancellationToken)))
                {
                    int separatorIndex = headerLine.IndexOf(':');
                    if (separatorIndex <= 0)
                        continue;

                    string name = headerLine[..separatorIndex].Trim();
                    string value = headerLine[(separatorIndex + 1)..].Trim();
                    if (name.Equals("Host", StringComparison.OrdinalIgnoreCase)) requestHost = value;
                    if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(value, out contentLength);
                    else if (name.Equals("X-Remote-Pick-Request", StringComparison.OrdinalIgnoreCase) && value == "1")
                        isPageAction = true;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!IsAllowedHost(requestHost, (IPEndPoint)client.Client.LocalEndPoint!))
                {
                    await WriteTextResponseAsync(stream, 403, "Forbidden", "Open the local Remote Pick address.", cancellationToken);
                    return;
                }
                // A cross-origin form can send a POST to the LAN server, but cannot add this
                // header without a CORS preflight. This server never grants CORS access.
                if (method == "POST" && !isPageAction)
                {
                    await WriteTextResponseAsync(stream, 403, "Forbidden", "Open Remote Pick to perform actions.", cancellationToken);
                    return;
                }
                if (contentLength < 0 || contentLength > 16 * 1024)
                {
                    await WriteTextResponseAsync(stream, 413, "Payload Too Large", "Request body is too large.", cancellationToken);
                    return;
                }

                string body = await ReadBodyAsync(reader, contentLength, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (method == "POST")
                {
                    LCUService.InvalidateCachedReads();
                    if (path is "/api/rune-page" or "/api/recommended-rune-page") InvalidateLoadout(keepPages: true);
                }

                if (method == "GET" && (path == "/" || path.StartsWith("/?", StringComparison.Ordinal)))
                    await WriteHtmlResponseAsync(stream, RemotePickPageHtml.Get(), cancellationToken);
                else if (method == "GET" && path.StartsWith("/api/timer", StringComparison.OrdinalIgnoreCase))
                    await WriteJsonResponseAsync(stream, 200, "OK", await GetTimerStateAsync(), cancellationToken);
                else if (method == "GET" && path == "/api/chat")
                    await WriteJsonResponseAsync(stream, 200, "OK", await GetChatAsync(), cancellationToken);
                else if (method == "GET" && path == "/api/skins")
                    await WriteJsonResponseAsync(stream, 200, "OK", await GetSkinsAsync(), cancellationToken);
                else if (method == "GET" && path.StartsWith("/api/rune-debug", StringComparison.OrdinalIgnoreCase))
                    await WriteJsonResponseAsync(stream, 200, "OK", await GetRuneDebugAsync(), cancellationToken);
                else if (method == "GET" && path.StartsWith("/api/state", StringComparison.OrdinalIgnoreCase))
                    await WriteJsonResponseAsync(stream, 200, "OK", await GetStateAsync(), cancellationToken);
                else if (method == "POST" && path.StartsWith("/api/pick", StringComparison.OrdinalIgnoreCase))
                    await WriteActionResponseAsync(stream, await PickChampionAsync(body), cancellationToken);
                else if (method == "POST" && path.StartsWith("/api/ban-hover", StringComparison.OrdinalIgnoreCase))
                    await WriteActionResponseAsync(stream, await HoverBanChampionAsync(body), cancellationToken);
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
                else if (method == "POST" && path == "/api/chat")
                    await WriteActionResponseAsync(stream, await SendChatAsync(body), cancellationToken);
                else if (method == "POST" && path == "/api/positions")
                    await WriteActionResponseAsync(stream, await SetPositionsAsync(body), cancellationToken);
                else if (method == "POST" && path == "/api/decline-match")
                    await WriteActionResponseAsync(stream, await RespondReadyCheckAsync(body, accept: false), cancellationToken);
                else if (method == "POST" && path == "/api/skin")
                    await WriteActionResponseAsync(stream, await SelectSkinAsync(body), cancellationToken);
                else if (method == "POST" && path == "/api/swap")
                    await WriteActionResponseAsync(stream, await SwapDraftAsync(body), cancellationToken);
                else if (method == "POST" && path == "/api/change-queue")
                    await WriteActionResponseAsync(stream, await ChangeQueueAsync(body), cancellationToken);
                else if (method == "POST" && path == "/api/bench-swap")
                    await WriteActionResponseAsync(stream, await SwapBenchAsync(body), cancellationToken);
                else if (method == "POST" && path == "/api/reroll")
                    await WriteActionResponseAsync(stream, await RerollAsync(), cancellationToken);
                else if (method == "POST" && path == "/api/start-queue")
                    await WriteActionResponseAsync(stream, await StartLobbyAsync(custom: false), cancellationToken);
                else if (method == "POST" && path == "/api/start-custom")
                    await WriteActionResponseAsync(stream, await StartLobbyAsync(custom: true), cancellationToken);
                else if (method == "POST" && path == "/api/accept-match")
                    await WriteActionResponseAsync(stream, await RespondReadyCheckAsync(body, accept: true), cancellationToken);
                else if (method == "POST" && path.StartsWith("/api/leave", StringComparison.OrdinalIgnoreCase))
                    await WriteActionResponseAsync(stream, await LeaveLobbyAsync(body), cancellationToken);
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

        private bool IsAllowedHost(string host, IPEndPoint local)
        {
            if (!Uri.TryCreate("http://" + host, UriKind.Absolute, out var uri) || uri.Port != Port) return false;
            string name = uri.DnsSafeHost.Trim('[', ']').TrimEnd('.');
            if (name.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                name.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) ||
                name.Equals(Environment.MachineName + ".local", StringComparison.OrdinalIgnoreCase) ||
                name.Equals(_discovery?.HostName, StringComparison.OrdinalIgnoreCase)) return true;
            return IPAddress.TryParse(name, out var ip) && (IPAddress.IsLoopback(ip) || ip.MapToIPv4().Equals(local.Address.MapToIPv4()));
        }

        private static async Task<string> ReadBodyAsync(StreamReader reader, int contentLength, CancellationToken token)
        {
            if (contentLength <= 0)
                return string.Empty;

            char[] buffer = new char[contentLength];
            int totalRead = 0;
            while (totalRead < contentLength)
            {
                int read = await reader.ReadAsync(buffer.AsMemory(totalRead, contentLength - totalRead), token);
                if (read == 0)
                    break;
                totalRead += read;
            }

            return new string(buffer, 0, totalRead);
        }

        public async Task<RemotePickState> GetStateAsync()
        {
            RemotePickState state = await GetTimerStateAsync();
            var pickedChampionIds = new HashSet<int>(state.PickedChampionIds);
            var allyPickedChampionIds = new HashSet<int>(state.AllyPickedChampionIds);
            var enemyPickedChampionIds = new HashSet<int>(state.EnemyPickedChampionIds);

            if (!state.IsInChampSelect) await AddLobbyDetailsAsync(state);

            if (state.IsInChampSelect)
            {
                await AddChampionSpellAndRuneListsAsync(state, new HashSet<int>(state.BannedChampionIds), pickedChampionIds, allyPickedChampionIds, enemyPickedChampionIds);
                await LoadPlayerProfilesAsync(state);
            }

            return state;
        }

        public async Task<RemotePickState> GetTimerStateAsync()
        {
            var state = new RemotePickState();

            if (!LCUService.CheckIfLeagueClientIsOpen())
            {
                _phaseClock.Reset(state.LiveSampleTickMs);
                state.Phase = "Closed";
                state.PhaseLabel = "League Closed";
                state.Message = "League Client is not running.";
                return state;
            }

            RefreshClientContext();
            if (DateTime.UtcNow >= _nextQueueCatalog) _ = EnsureQueueCatalogAsync();
            state.Phase = await LCUService.GetCurrentGamePhaseAsync();
            state.IsInChampSelect = state.Phase == "ChampSelect";
            if (state.Phase != "ReadyCheck") _readyCheck.Observe(state.Phase, _clientPid, "");
            state.CanChangeQueue = state.Phase == "None";
            state.CanLeave = state.Phase == "Lobby" || state.Phase == "Matchmaking" || state.Phase == "ReadyCheck" || state.Phase == "ChampSelect";
            if (!state.IsInChampSelect) _phaseClock.Reset(state.LiveSampleTickMs);
            if (state.Phase == "Lobby")
            {
                using var document = await ReadClientJsonAsync("lol-lobby/v2/lobby");
                if (document != null && RemoteLobby.Read(document.RootElement) is { } lobby)
                {
                    state.QueueId = lobby.QueueId;
                    state.QueueName = lobby.IsCustom ? "Custom game" : GetQueueName(lobby.QueueId, false);
                    state.CanStartLobby = lobby.CanStart;
                    state.IsCustomLobby = lobby.IsCustom;
                    state.PartySize = lobby.PartySize;
                    state.LobbyHint = lobby.StartHint;
                    state.CanChangeQueue = RemoteLobbyData.CanChange(document!.RootElement);
                    state.LobbyMembers = RemoteLobbyData.Members(document.RootElement);
                    state.CanSelectPositions = RemoteLobbyPositions.CanSelect(document.RootElement);
                    if (document.RootElement.TryGetProperty("localMember", out var local))
                    { state.FirstPosition = RemoteLobbyData.Text(local, "firstPositionPreference"); state.SecondPosition = RemoteLobbyData.Text(local, "secondPositionPreference"); }
                }
            }
            else
            {
                TryLoadQueueFromGameflowOrLobby(state);
                if (state.Phase is "Matchmaking" or "ReadyCheck")
                {
                    using var party = await ReadClientJsonAsync("lol-lobby/v2/lobby");
                    if (party != null) { state.LobbyMembers = RemoteLobbyData.Members(party.RootElement); state.PartySize = state.LobbyMembers.Count; }
                }
                if (state.Phase == "Matchmaking")
                {
                    using var search = await ReadClientJsonAsync("lol-matchmaking/v1/search");
                    state.TimerMeasuredTickMs = Environment.TickCount64;
                    if (search != null) state.PhaseElapsedSeconds = RemoteLobby.QueueSeconds(search.RootElement);
                }
                else if (state.Phase == "ReadyCheck")
                {
                    using var ready = await ReadClientJsonAsync("lol-matchmaking/v1/ready-check");
                    string? response = ready == null ? null : GetStringProperty(ready.RootElement, "playerResponse");
                    state.CanAcceptMatch = state.CanDeclineMatch = response == "None";
                    state.MatchAccepted = response == "Accepted"; state.MatchDeclined = response == "Declined";
                    string matchId = "";
                    string? gameflow = await LCUService.GetGameflowSessionAsync();
                    if (gameflow != null) { try { using var game = JsonDocument.Parse(gameflow); matchId = RemoteReadyCheck.MatchId(game.RootElement); } catch (JsonException) { } }
                    state.ReadyCheckId = _readyCheck.Observe(state.Phase, _clientPid, response ?? "", matchId);
                }
            }

            ApplyKnownQueueMode(state);
            if (state.Phase == "GameStart" || state.Phase == "InProgress")
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

            string[] sessionResult = await LCUService.ClientRequestAsync("GET", "lol-champ-select/v1/session");
            if (sessionResult[0] != "200")
            {
                state.PhaseLabel = "Champ Select";
                state.Message = $"Could not read champion select session. Status: {sessionResult[0]}";
                return state;
            }

            using JsonDocument doc = JsonDocument.Parse(sessionResult[1]);
            JsonElement root = doc.RootElement;
            ParseLocalPlayerSelection(root, state);
            state.DraftId = RemoteDraftInteractions.DraftId(root);
            state.CanSelectSkin = GetBoolProperty(root, "allowSkinSelection") && state.SelectedChampionId > 0;
            ParseTimer(root, state);
            ParseActions(root, state);
            string actionPhase = state.ChampSelectPhase;
            await SyncDraftClockAsync(root, state);
            if (state.ChampSelectPhase != actionPhase) ParseActions(root, state);
            var bannedChampionIds = new HashSet<int>();
            ParseBans(root, bannedChampionIds);
            ParseTeamSlots(root, state);
            EnrichTeamSlots(state);
            FillCachedPlayerProfiles(state);
            ParseQueueAndMode(root, state);
            ApplyKnownQueueMode(state);
            ParseAvailableChampionIds(root, state);
            if (state.IsRandomChampionMode)
            {
                var cards = new List<int>();
                if (GetBoolProperty(root, "allowSubsetChampionPicks"))
                {
                    var subset = await LCUService.ClientRequestAsync("GET", "lol-lobby-team-builder/champ-select/v1/subset-champion-list");
                    if (subset[0] == "200")
                    {
                        try { using var list = JsonDocument.Parse(subset[1]);
                            if (list.RootElement.ValueKind == JsonValueKind.Array) cards = list.RootElement.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.Number).Select(p => p.GetInt32()).Where(id => id > 0).ToList(); }
                        catch (JsonException) { }
                    }
                }
                RemoteRandomSelection.Read(root, state, cards);
            }
            var allPicks = new HashSet<int>(); var allies = new HashSet<int>(); var enemies = new HashSet<int>();
            ParsePicks(root, allPicks, allies, enemies);
            state.PickedChampionIds = allPicks.ToList(); state.AllyPickedChampionIds = allies.ToList(); state.EnemyPickedChampionIds = enemies.ToList();
            ParseAvailableSpellIds(root, state);
            await AddSwapsAsync(root, state);
            state.BanFallbackArmed = IsBanFallbackArmed(state.ActionId, state.HoveredBanChampionId);
            if (state.HoveredChampionId <= 0 && state.SelectedChampionId > 0 &&
                state.ChampSelectPhase.Contains("PLANNING", StringComparison.OrdinalIgnoreCase))
                {
                state.HoveredChampionId = state.SelectedChampionId;
                state.HoveredChampionSource = "PlanningSelection";
            }
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
                Probe("lol-game-data/assets/v1/perks.json");
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
            if (state.IsRandomChampionMode && (!state.CanSelectCard || !state.CardChampionIds.Contains(request.ChampionId)))
                return Failure("This champion card is no longer available.");

            bool success = LCUService.SelectChampion(request.ChampionId, state.IsRandomChampionMode ? state.PickActionId : state.ActionId, complete: true);
            return new RemotePickActionResult { Success = success, Message = success ? state.IsRandomChampionMode ? "Champion chosen." : "Champion locked in." : "League Client rejected the pick request." };
        }

        public async Task<RemotePickActionResult> BanChampionAsync(string body)
        {
            RemotePickRequest? request = ParseChampionRequest(body);
            if (request == null)
                return Failure("Invalid champion.");

            await _banActionGate.WaitAsync();
            try
            {
                RemotePickState state = await GetTimerStateAsync();
                if (!state.IsInChampSelect)
                    return Failure("You are not in champion select.");
                if (!state.CanBan)
                    return Failure("It is not your ban turn.");
                if (state.BannedChampionIds.Contains(request.ChampionId))
                    return Failure("This champion is already banned.");

                bool success = LCUService.SelectChampion(request.ChampionId, state.ActionId, complete: true);
                if (success) CancelBanFallback();
                return new RemotePickActionResult { Success = success, Message = success ? "Champion banned." : "League Client rejected the ban request." };
            }
            finally { _banActionGate.Release(); }
        }

        public async Task<RemotePickActionResult> HoverBanChampionAsync(string body)
        {
            RemotePickRequest? request = ParseChampionRequest(body);
            if (request == null)
                return Failure("Invalid champion.");

            await _banActionGate.WaitAsync();
            try
            {
                RemotePickState state = await GetTimerStateAsync();
                if (!state.CanBan || string.IsNullOrWhiteSpace(state.ActionId))
                    return Failure("It is not your ban turn.");
                if (state.BannedChampionIds.Contains(request.ChampionId))
                    return Failure("This champion is already banned.");

                RemotePickState? observed = state;
                if (state.HoveredBanChampionId != request.ChampionId)
                {
                    string payload = JsonSerializer.Serialize(new { championId = request.ChampionId, completed = false }, _jsonOptions);
                    string[] reply = await LCUService.ClientRequestAsync("PATCH", $"lol-champ-select/v1/session/actions/{state.ActionId}", payload);
                    if (!reply[0].StartsWith("2"))
                        return Failure("League Client did not accept this ban preview.");

                    observed = null;
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        if (attempt > 0) await Task.Delay(80);
                        RemotePickState? latest = await ReadClientHoverAsync();
                        if (latest?.ActionId == state.ActionId && latest.CanBan && latest.HoveredBanChampionId == request.ChampionId)
                        {
                            observed = latest;
                            break;
                        }
                    }
                }

                if (observed == null)
                    return Failure("League Client did not confirm this ban preview.");
                bool armed = ArmBanFallback(observed, request.ChampionId);
                return new RemotePickActionResult
                {
                    Success = true,
                    ConfirmedChampionId = request.ChampionId,
                    ConfirmedChampionSource = "BanSlot",
                    AutoBanArmed = armed,
                    Message = armed ? "Ban preview confirmed. It will lock near the end of your turn if you do not ban sooner." :
                        "Ban preview confirmed. Confirm the ban yourself in this mode."
                };
            }
            finally { _banActionGate.Release(); }
        }

        private bool ArmBanFallback(RemotePickState state, int championId)
        {
            CancelBanFallback();
            if (!state.CanBan || state.HoveredBanChampionId != championId || state.IsTimerInfinite || state.TimeLeftInPhaseMs < 0)
                return false;

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_cts?.Token ?? CancellationToken.None);
            lock (_banFallbackSync)
            {
                _banFallbackCts = cancellation;
                _banFallbackActionId = state.ActionId;
                _banFallbackChampionId = championId;
            }
            _ = AutoCompleteBanAsync(state.ActionId, championId, Math.Max(0, state.TimeLeftInPhaseMs - 1800), cancellation);
            return true;
        }

        private void CancelBanFallback()
        {
            lock (_banFallbackSync)
            {
                _banFallbackCts?.Cancel();
                _banFallbackCts = null;
                _banFallbackActionId = string.Empty;
                _banFallbackChampionId = 0;
            }
        }

        private bool IsBanFallbackArmed(string actionId, int championId)
        {
            lock (_banFallbackSync)
                return championId > 0 && _banFallbackCts is { IsCancellationRequested: false } &&
                    _banFallbackActionId == actionId && _banFallbackChampionId == championId;
        }

        private async Task AutoCompleteBanAsync(string actionId, int championId, int delayMs, CancellationTokenSource cancellation)
        {
            try
            {
                while (true)
                {
                    await Task.Delay(delayMs, cancellation.Token);
                    await _banActionGate.WaitAsync(cancellation.Token);
                    try
                    {
                        if (!IsBanFallbackArmed(actionId, championId)) return;
                        RemotePickState fresh = await GetTimerStateAsync();
                        if (!fresh.CanBan || fresh.ActionId != actionId || fresh.HoveredBanChampionId != championId ||
                            fresh.BannedChampionIds.Contains(championId) || fresh.IsTimerInfinite || fresh.TimeLeftInPhaseMs < 0)
                            return;
                        if (fresh.TimeLeftInPhaseMs <= 3000)
                        {
                            if (!cancellation.IsCancellationRequested)
                                LCUService.SelectChampion(championId, actionId, complete: true);
                            return;
                        }
                        delayMs = Math.Max(250, fresh.TimeLeftInPhaseMs - 1800);
                    }
                    finally { _banActionGate.Release(); }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.WriteLine($"Remote Pick backup ban failed: {ex.Message}"); }
            finally
            {
                lock (_banFallbackSync)
                {
                    if (ReferenceEquals(_banFallbackCts, cancellation))
                    {
                        _banFallbackCts = null;
                        _banFallbackActionId = string.Empty;
                        _banFallbackChampionId = 0;
                    }
                }
                cancellation.Dispose();
            }
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

            if (state.IsRandomChampionMode) return Failure("Choose a champion card or take a champion from the bench.");
            int confirmedId = state.HoveredChampionId;
            string confirmedSource = state.HoveredChampionSource;
            if (confirmedId == request.ChampionId)
                return new RemotePickActionResult { Success = true, Message = "Hover confirmed in League Client session.", ConfirmedChampionId = confirmedId, ConfirmedChampionSource = confirmedSource };

            async Task<bool> TryHoverAsync(string endpoint, object payload)
            {
                string json = JsonSerializer.Serialize(payload, _jsonOptions);
                string[] reply = await LCUService.ClientRequestAsync("PATCH", endpoint, json);
                if (!reply[0].StartsWith("2"))
                    return false;

                // An accepted PATCH does not prove that the client actually changed its hover.
                // Read the session back before reporting success to the phone.
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (attempt > 0)
                        await Task.Delay(80);
                    RemotePickState? observed = await ReadClientHoverAsync();
                    if (observed != null)
                    {
                        confirmedId = observed.HoveredChampionId;
                        confirmedSource = observed.HoveredChampionSource;
                        if (confirmedId == request.ChampionId)
                            return true;
                    }
                }
                return false;
            }

            bool confirmed = false;
            if (state.CanPick && !string.IsNullOrWhiteSpace(state.ActionId))
                confirmed = await TryHoverAsync($"lol-champ-select/v1/session/actions/{state.ActionId}",
                    new { championId = request.ChampionId, completed = false });

            if (!confirmed)
                confirmed = await TryHoverAsync("lol-champ-select/v1/session/my-selection",
                    new { championPickIntent = request.ChampionId });

            if (!confirmed)
                confirmed = await TryHoverAsync("lol-champ-select/v1/session/my-selection",
                    new { championId = request.ChampionId });

            return new RemotePickActionResult
            {
                Success = confirmed,
                ConfirmedChampionId = confirmedId,
                ConfirmedChampionSource = confirmedSource,
                Message = confirmed ? "Hover confirmed in League Client session." :
                    "League Client did not confirm this hover. It may no longer accept changes in this phase."
            };
        }

        private static async Task<RemotePickState?> ReadClientHoverAsync()
        {
            string[] response = await LCUService.ClientRequestAsync("GET", "lol-champ-select/v1/session");
            if (response[0] != "200")
                return null;

            try
            {
                using JsonDocument document = JsonDocument.Parse(response[1]);
                var snapshot = new RemotePickState();
                ParseLocalPlayerSelection(document.RootElement, snapshot);
                ParseTimer(document.RootElement, snapshot);
                ParseActions(document.RootElement, snapshot);
                if (snapshot.HoveredChampionId <= 0 && snapshot.SelectedChampionId > 0 &&
                    snapshot.ChampSelectPhase.Contains("PLANNING", StringComparison.OrdinalIgnoreCase))
                    {
                    snapshot.HoveredChampionId = snapshot.SelectedChampionId;
                    snapshot.HoveredChampionSource = "PlanningSelection";
                }
                return snapshot;
            }
            catch (JsonException)
            {
                return null;
            }
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
            if (!state.IsInChampSelect)
                return Failure("Summoner spells can only be changed during champion select.");
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

        public async Task<RemotePickActionResult> SelectRecommendedRunePageAsync(string body)
        {
            RemoteRecommendedRunePageRequest? request;
            try { request = JsonSerializer.Deserialize<RemoteRecommendedRunePageRequest>(body, _jsonOptions); }
            catch { return Failure("Invalid recommended rune page request."); }

            if (request == null || request.Index < 0)
                return Failure("Invalid recommended rune page.");
            if (!LCUService.CheckIfLeagueClientIsOpen())
                return Failure("League Client is not running.");

            RemotePickState state = await GetStateAsync();
            RemoteRecommendedRunePageDto? recommendedPage = state.RecommendedRunePages.FirstOrDefault(page => page.Index == request.Index);
            if (recommendedPage == null)
                return Failure("Recommended rune page was not found for this champion.");
            if (!recommendedPage.CanApply)
                return Failure("This recommended rune page does not include enough rune data to apply it.");

            long editablePageId = GetEditableRunePageId();
            if (editablePageId <= 0)
                return Failure("No editable rune page was found in League Client.");

            string payloadJson = JsonSerializer.Serialize(new
            {
                id = editablePageId,
                name = BuildAppliedRunePageName(recommendedPage),
                primaryStyleId = recommendedPage.PrimaryStyleId,
                subStyleId = recommendedPage.SubStyleId,
                selectedPerkIds = recommendedPage.SelectedPerkIds,
                current = true
            }, _jsonOptions);

            string[] result = LCUService.ClientRequest("PUT", $"lol-perks/v1/pages/{editablePageId}", payloadJson);
            bool success = result[0].StartsWith("2");
            if (success)
                _ = LCUService.ClientRequest("POST", $"lol-perks/v1/pages/{editablePageId}/current");

            return new RemotePickActionResult
            {
                Success = success,
                Message = success ? $"Applied: {BuildAppliedRunePageName(recommendedPage)}" : $"League Client rejected recommended runes. Status: {result[0]}"
            };
        }

        private static async Task<JsonDocument?> ReadClientJsonAsync(string endpoint, JsonValueKind expectedKind = JsonValueKind.Object)
        {
            string[] result = await LCUService.ClientRequestAsync("GET", endpoint);
            if (result[0] != "200") return null;
            try
            {
                var document = JsonDocument.Parse(result[1]);
                if (document.RootElement.ValueKind == expectedKind) return document;
                document.Dispose();
            }
            catch (JsonException) { }
            return null;
        }

        private async Task<RemotePickActionResult> StartLobbyAsync(bool custom)
        {
            if (!await _lobbyActionGate.WaitAsync(0)) return Failure("A lobby action is already in progress.");
            try
            {
                if (await LCUService.GetCurrentGamePhaseAsync() != "Lobby") return Failure("The lobby has changed.");
                using var doc = await ReadClientJsonAsync("lol-lobby/v2/lobby");
                var lobby = doc == null ? null : RemoteLobby.Read(doc.RootElement);
                if (lobby == null || lobby.IsCustom != custom) return Failure("The lobby has changed.");
                if (!lobby.CanStart) return Failure(lobby.StartHint);
                string endpoint = custom ? "lol-lobby/v1/lobby/custom/start-champ-select" : "lol-lobby/v2/lobby/matchmaking/search";
                var result = await LCUService.ClientRequestAsync("POST", endpoint);
                return result[0].StartsWith("2")
                    ? new RemotePickActionResult { Success = true, Message = custom ? "Custom game started." : "Queue started." }
                    : Failure("League Client could not start this lobby. Check its queue requirements.");
            }
            finally { _lobbyActionGate.Release(); }
        }

        public async Task<RemotePickActionResult> LeaveLobbyAsync(string body = "")
        {
            if (!LCUService.CheckIfLeagueClientIsOpen())
                return Failure("League Client is not running.");

            string phase = await LCUService.GetCurrentGamePhaseAsync();
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    using var request = JsonDocument.Parse(body);
                    if (request.RootElement.ValueKind != JsonValueKind.Object) return Failure("Invalid lobby action.");
                    string? expected = GetStringProperty(request.RootElement, "phase");
                    if (expected != null && expected != phase) return Failure("The lobby has changed.");
                }
                catch (JsonException) { return Failure("Invalid lobby action."); }
            }
            if (phase == "ReadyCheck")
                return RequestLeave("ReadyCheck", LCUService.ClientRequest("POST", "lol-matchmaking/v1/ready-check/decline"));
            if (phase == "Matchmaking")
                return RequestLeave("Matchmaking", LCUService.ClientRequest("DELETE", "lol-lobby/v2/lobby/matchmaking/search"));
            if (phase == "Lobby")
                return RequestLeave("Lobby", LCUService.ClientRequest("DELETE", "lol-lobby/v2/lobby"));
            if (phase == "ChampSelect")
            {
                if (!await _lobbyActionGate.WaitAsync(0)) return Failure("Another lobby action is in progress.");
                try
                {
                    using var draft = await ReadClientJsonAsync("lol-champ-select/v1/session");
                    if (draft == null) return Failure("Cannot confirm the current draft. Check League Client.");
                    string identity = RemoteDraftInteractions.DraftId(draft.RootElement);
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        using var request = JsonDocument.Parse(body);
                        if (RemoteLobbyData.Text(request.RootElement, "draftId") != identity) return Failure("The draft has changed.");
                    }
                    CancelBanFallback();
                    return await RemoteDraftExit.RunAsync(identity, ProbeDraftExitAsync,
                        endpoint => LCUService.ClientRequestAsync("POST", endpoint), Task.Delay, GetBoolProperty(draft.RootElement, "isCustomGame"));
                }
                finally { _lobbyActionGate.Release(); }
            }

            return Failure($"Cannot leave during phase: {phase}");
        }

        private async Task AddChampionSpellAndRuneListsAsync(RemotePickState state, HashSet<int> bannedChampionIds, HashSet<int> pickedChampionIds, HashSet<int> allyPickedChampionIds, HashSet<int> enemyPickedChampionIds)
        {
            await EnsureRemoteChampionCacheAsync();
            _ = EnsureChampionPositionsAsync(); // Optional metadata must not delay the live draft state.
            EnrichTeamSlots(state);
            await EnsureRemoteSpellCacheAsync();

            RemoteChampionAvailabilityService.Refresh();
            var availableChampionIds = new HashSet<int>(state.AvailableChampionIds);
            bool shouldFilterRandomChampions = state.IsRandomChampionMode;

            state.Champions = (_remoteChampionCache ?? new List<RemoteChampionDto>())
                .Where(champion => !shouldFilterRandomChampions || availableChampionIds.Contains(champion.Id) || state.SelectedChampionId == champion.Id)
                .Select(champion => new RemoteChampionDto
                {
                    Id = champion.Id,
                    Name = champion.Name,
                    ImageUrl = champion.ImageUrl,
                    Positions = _championPositions.GetValueOrDefault(champion.Id) ?? Array.Empty<string>(),
                    IsOwnedOrFree = state.CanBan || state.IsRandomChampionMode && availableChampionIds.Contains(champion.Id) || RemoteChampionAvailabilityService.CanUseChampion(champion.Id),
                    IsBanned = bannedChampionIds.Contains(champion.Id),
                    IsPicked = pickedChampionIds.Contains(champion.Id) && !(state.IsRandomChampionMode && state.SelectedChampionId == champion.Id && state.CanSelectCard),
                    IsSelected = state.SelectedChampionId == champion.Id,
                    IsIntent = state.PickIntentChampionId == champion.Id,
                    IsAvailableInCurrentMode = !shouldFilterRandomChampions || availableChampionIds.Contains(champion.Id) || state.SelectedChampionId == champion.Id || state.PickIntentChampionId == champion.Id,
                    TeamSide = allyPickedChampionIds.Contains(champion.Id) ? "ally" : enemyPickedChampionIds.Contains(champion.Id) ? "enemy" : string.Empty
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

            AddCachedLoadout(state);
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

                Dictionary<int, RuneMetadata> perks = GetRuneMetadata();
                foreach (JsonElement page in pagesDoc.RootElement.EnumerateArray())
                {
                    if (!TryGetPropertyLong(page, "id", out long pageId))
                        continue;

                    List<int> keyIds = GetPerkIds(page).Take(4).ToList();
                    List<string> keyRunes = keyIds.Select(id => GetNameOrId(perks, id)).ToList();
                    int primaryId = GetIntProperty(page, "primaryStyleId");
                    int secondaryId = GetIntProperty(page, "subStyleId");
                    var dto = new RemoteRunePageDto
                    {
                        Id = pageId,
                        Name = GetStringProperty(page, "name") ?? $"Rune Page {pageId}",
                        IsCurrent = currentPageId == pageId || GetBoolProperty(page, "current") || GetBoolProperty(page, "isCurrent"),
                        IsEditable = GetBoolProperty(page, "isEditable"),
                        IsDeletable = GetBoolProperty(page, "isDeletable"),
                        KeystoneName = keyRunes.FirstOrDefault() ?? string.Empty,
                        KeystoneIconUrl = keyIds.Count > 0 ? GetIconUrl(perks, keyIds[0]) : string.Empty,
                        PrimaryStyleName = primaryId > 0 ? GetNameOrId(perks, primaryId) : string.Empty,
                        PrimaryStyleIconUrl = GetIconUrl(perks, primaryId),
                        SecondaryStyleName = secondaryId > 0 ? GetNameOrId(perks, secondaryId) : string.Empty,
                        SecondaryStyleIconUrl = GetIconUrl(perks, secondaryId),
                        KeyRuneNames = keyRunes,
                        KeyRuneIconUrls = keyIds.Select(id => GetIconUrl(perks, id)).ToList()
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
            string cacheKey = $"{championId}:{position}:{mapId}";
            RecommendedRuneCache? cached = _recommendedRuneCache;
            if (cached != null && cached.Key == cacheKey && cached.ExpiresUtc > DateTime.UtcNow)
            {
                state.RecommendedRunePages = cached.Pages;
                return;
            }

            string[] sessionResult = LCUService.ClientRequest("GET", "lol-champ-select/v1/session");
            if (sessionResult[0] == "200")
                candidates.AddRange(ParseRecommendedRunePages(sessionResult[1]));

            foreach (string endpoint in GetRecommendedRuneEndpoints(championId, position, mapId))
            {
                if (!IsRunning) break;
                if (candidates.Any(page => page.CanApply)) break;
                if (_unsupportedRuneEndpoints.TryGetValue(endpoint, out var retry) && retry > DateTime.UtcNow) continue;
                string[] result = LCUService.ClientRequest("GET", endpoint);
                if (result[0] == "200") candidates.AddRange(ParseRecommendedRunePages(result[1]));
                else if (result[0] is "404" or "405")
                {
                    if (_unsupportedRuneEndpoints.Count > 100) _unsupportedRuneEndpoints.Clear();
                    _unsupportedRuneEndpoints[endpoint] = DateTime.UtcNow.AddMinutes(2);
                }
            }

            List<RemoteRecommendedRunePageDto> pages = candidates
                .Where(page => page.CanApply)
                .GroupBy(page => string.Join(',', page.SelectedPerkIds))
                .Select(group => group.First())
                .Take(3)
                .Select((page, index) => { page.Index = index; return page; })
                .ToList();

            EnrichRecommendedRunePageNames(pages);
            state.RecommendedRunePages = pages;
            _recommendedRuneCache = new RecommendedRuneCache(cacheKey, DateTime.UtcNow.AddMinutes(pages.Count > 0 ? 5 : 1), pages);
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

        private void EnrichRecommendedRunePageNames(List<RemoteRecommendedRunePageDto> pages)
        {
            if (pages.Count == 0 || !LCUService.CheckIfLeagueClientIsOpen())
                return;

            Dictionary<int, RuneMetadata> perks = GetRuneMetadata();

            foreach (RemoteRecommendedRunePageDto page in pages)
            {
                string originalSubtitle = page.Subtitle;
                string primaryStyle = GetNameOrId(perks, page.PrimaryStyleId);
                string secondaryStyle = GetNameOrId(perks, page.SubStyleId);

                var runeNames = page.SelectedPerkIds
                    .Select(id => GetNameOrId(perks, id))
                    .ToList();

                page.KeystoneName = runeNames.FirstOrDefault() ?? string.Empty;
                page.KeystoneIconUrl = page.SelectedPerkIds.Count > 0 ? GetIconUrl(perks, page.SelectedPerkIds[0]) : string.Empty;
                page.KeyRuneNames = runeNames.Take(4).ToList();
                page.KeyRuneIconUrls = page.SelectedPerkIds.Take(4).Select(id => GetIconUrl(perks, id)).ToList();
                page.PrimaryStyleName = primaryStyle;
                page.PrimaryStyleIconUrl = GetIconUrl(perks, page.PrimaryStyleId);
                page.SecondaryStyleName = secondaryStyle;
                page.SecondaryStyleIconUrl = GetIconUrl(perks, page.SubStyleId);
                if (!string.IsNullOrWhiteSpace(page.KeystoneName))
                    page.Name = page.KeystoneName;

                page.Subtitle = string.IsNullOrWhiteSpace(originalSubtitle)
                    ? $"{primaryStyle} / {secondaryStyle}"
                    : $"{originalSubtitle} · {primaryStyle} / {secondaryStyle}";
            }
        }

        private Dictionary<int, RuneMetadata> GetRuneMetadata()
        {
            RuneMetadataCache? cached = _runeMetadataCache;
            if (cached != null && cached.ExpiresUtc > DateTime.UtcNow)
                return cached.Values;

            Dictionary<int, RuneMetadata> metadata = GetLcuRuneMetadata("lol-game-data/assets/v1/perks.json");
            foreach (KeyValuePair<int, RuneMetadata> pair in GetLcuRuneMetadata("lol-perks/v1/styles"))
            {
                if (!metadata.TryGetValue(pair.Key, out RuneMetadata? existing) || string.IsNullOrEmpty(existing.IconUrl))
                    metadata[pair.Key] = pair.Value;
            }

            _runeMetadataCache = new RuneMetadataCache(DateTime.UtcNow.AddSeconds(30), metadata);
            return metadata;
        }

        private static Dictionary<int, RuneMetadata> GetLcuRuneMetadata(string endpoint)
        {
            var map = new Dictionary<int, RuneMetadata>();
            string[] result = LCUService.ClientRequest("GET", endpoint);
            if (result[0] != "200")
                return map;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(result[1]);
                CollectIdNamePairs(doc.RootElement, map);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not parse LCU rune metadata from {endpoint}: {ex.Message}");
            }
            return map;
        }

        private static void CollectIdNamePairs(JsonElement element, Dictionary<int, RuneMetadata> map)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (TryGetPropertyInt(element, "id", out int id))
                {
                    string? name = GetStringProperty(element, "name", "displayName");
                    if (id > 0 && !string.IsNullOrWhiteSpace(name) && !map.ContainsKey(id))
                        map[id] = new RuneMetadata(name, RemoteRuneAssets.ToDataDragonUrl(GetStringProperty(element, "iconPath", "icon")));
                }

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Object || property.Value.ValueKind == JsonValueKind.Array)
                        CollectIdNamePairs(property.Value, map);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in element.EnumerateArray())
                    CollectIdNamePairs(child, map);
            }
        }

        private static string GetNameOrId(Dictionary<int, RuneMetadata> metadata, int id)
            => metadata.TryGetValue(id, out RuneMetadata? rune) && !string.IsNullOrWhiteSpace(rune.Name) ? rune.Name : id.ToString();

        private static string GetIconUrl(Dictionary<int, RuneMetadata> metadata, int id)
            => metadata.TryGetValue(id, out RuneMetadata? rune) ? rune.IconUrl : string.Empty;

        private static string BuildAppliedRunePageName(RemoteRecommendedRunePageDto page)
        {
            string name = string.IsNullOrWhiteSpace(page.Name) ? "Recommended Runes" : page.Name;
            return name.StartsWith("Remote Pick:", StringComparison.OrdinalIgnoreCase) ? name : $"Remote Pick: {name}";
        }

        private static long GetEditableRunePageId()
        {
            string[] result = LCUService.ClientRequest("GET", "lol-perks/v1/pages");
            if (result[0] != "200")
                return 0;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(result[1]);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return 0;

                long firstEditableId = 0;
                foreach (JsonElement page in doc.RootElement.EnumerateArray())
                {
                    if (!TryGetPropertyLong(page, "id", out long pageId) || pageId <= 0)
                        continue;

                    bool isEditable = GetBoolProperty(page, "isEditable");
                    bool isCurrent = GetBoolProperty(page, "current") || GetBoolProperty(page, "isCurrent");
                    if (!isEditable)
                        continue;

                    if (firstEditableId == 0)
                        firstEditableId = pageId;
                    if (isCurrent)
                        return pageId;
                }

                return firstEditableId;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not parse editable rune page id: {ex.Message}");
                return 0;
            }
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

        private async Task EnsureChampionPositionsAsync()
        {
            if (DateTime.UtcNow < _nextPositionsRefresh) return;
            _nextPositionsRefresh = DateTime.UtcNow.AddSeconds(30);
            var response = await LCUService.ClientRequestAsync("GET", "lol-perks/v1/recommended-champion-positions");
            if (response[0] != "200") return;
            try
            {
                using var json = JsonDocument.Parse(response[1]);
                var positions = RemoteChampionPositions.Parse(json.RootElement);
                if (positions.Count == 0) return;
                _championPositions = positions;
                _nextPositionsRefresh = DateTime.UtcNow.AddMinutes(5);
            }
            catch (JsonException) { }
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

        private static void ParsePicks(JsonElement root, HashSet<int> pickedChampionIds, HashSet<int> allyPickedChampionIds, HashSet<int> enemyPickedChampionIds)
        {
            AddChampionIdsFromTeam(root, "myTeam", pickedChampionIds, allyPickedChampionIds);
            AddChampionIdsFromTeam(root, "theirTeam", pickedChampionIds, enemyPickedChampionIds);
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
                if (TryGetPropertyInt(player, "championPickIntent", out int intentChampionId))
                {
                    state.PickIntentChampionId = intentChampionId;
                    state.HoveredChampionId = intentChampionId;
                    if (intentChampionId > 0) state.HoveredChampionSource = "PickIntent";
                }
                if (TryGetPropertyInt(player, "selectedSkinId", out int skinId)) state.SelectedSkinId = skinId;
                if (TryGetPropertyInt(player, "spell1Id", out int spell1Id)) state.Spell1Id = spell1Id;
                if (TryGetPropertyInt(player, "spell2Id", out int spell2Id)) state.Spell2Id = spell2Id;
                state.AssignedPosition = GetStringProperty(player, "assignedPosition", "position") ?? string.Empty;
                return;
            }
        }

        private static void ParseTeamSlots(JsonElement root, RemotePickState state)
        {
            int localCellId = TryGetPropertyInt(root, "localPlayerCellId", out int id) ? id : -1;
            bool isCustomGame = GetBoolProperty(root, "isCustomGame");
            state.AllySlots = ReadTeamSlots(root, "myTeam", localCellId, isCustomGame);
            state.EnemySlots = ReadTeamSlots(root, "theirTeam", -1, isCustomGame);
        }

        private static List<RemoteTeamSlotDto> ReadTeamSlots(JsonElement root, string propertyName, int localCellId, bool isCustomGame)
        {
            var slots = new List<RemoteTeamSlotDto>();
            if (!root.TryGetProperty(propertyName, out JsonElement team) || team.ValueKind != JsonValueKind.Array)
                return slots;

            foreach (JsonElement player in team.EnumerateArray())
            {
                int cellId = GetIntProperty(player, "cellId");
                string gameName = GetStringProperty(player, "gameName", "riotIdGameName") ?? string.Empty;
                string tagLine = GetStringProperty(player, "tagLine", "riotIdTagLine", "riotIdTagline") ?? string.Empty;
                string riotId = GetStringProperty(player, "riotId") ?? string.Empty;
                int separator = riotId.LastIndexOf('#');
                if (string.IsNullOrEmpty(tagLine) && separator > 0)
                {
                    gameName = riotId[..separator];
                    tagLine = riotId[(separator + 1)..];
                }
                string visibility = GetStringProperty(player, "nameVisibilityType") ?? string.Empty;
                bool visible = cellId == localCellId || visibility.Equals("VISIBLE", StringComparison.OrdinalIgnoreCase) ||
                    (string.IsNullOrEmpty(visibility) &&
                        (isCustomGame || (!string.IsNullOrEmpty(gameName) && !string.IsNullOrEmpty(tagLine))));
                slots.Add(new RemoteTeamSlotDto
                {
                    CellId = cellId,
                    AssignedPosition = GetStringProperty(player, "assignedPosition", "position") ?? "",
                    PickTurn = TryGetPropertyInt(player, "pickTurn", out int pickTurn) ? pickTurn : -1,
                    ChampionId = GetIntProperty(player, "championId"),
                    IntentChampionId = GetIntProperty(player, "championPickIntent"),
                    IsLocalPlayer = localCellId >= 0 && cellId == localCellId,
                    PlayerPuuid = GetStringProperty(player, "puuid") ?? string.Empty,
                    PlayerSummonerId = player.TryGetProperty("summonerId", out JsonElement summonerId) ? GetJsonValueAsString(summonerId) : string.Empty,
                    IsPlayerNameVisible = visible,
                    RiotGameName = visible ? gameName : string.Empty,
                    RiotTagLine = visible ? tagLine : string.Empty
                });
            }
            // Swapping pick order changes pickTurn; myTeam array order need not change.
            return slots.Count > 0 && slots.All(s => s.PickTurn >= 0) ? slots.OrderBy(s => s.PickTurn).ToList() : slots;
        }

        private string GetProfileRegion()
        {
            if (DateTime.UtcNow < _profileRegionExpiresUtc)
                return _profileRegion;

            _profileRegionExpiresUtc = DateTime.UtcNow.AddSeconds(15);
            string[] response = LCUService.ClientRequest("GET", "riotclient/region-locale");
            if (response[0] == "200")
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(response[1]);
                    _profileRegion = GetStringProperty(document.RootElement, "region", "webRegion") ?? string.Empty;
                    _profileRegionExpiresUtc = DateTime.UtcNow.AddMinutes(5);
                }
                catch (Exception ex) { Debug.WriteLine($"Could not read player region: {ex.Message}"); _profileRegion = string.Empty; }
            }
            else _profileRegion = string.Empty;
            return _profileRegion;
        }

        private async Task LoadPlayerProfilesAsync(RemotePickState state)
        {
            if (string.IsNullOrEmpty(GetProfileRegion())) return;

            string[] missing = state.AllySlots.Concat(state.EnemySlots)
                .Where(slot => slot.IsPlayerNameVisible && !string.IsNullOrWhiteSpace(GetPlayerCacheKey(slot)) &&
                    (string.IsNullOrWhiteSpace(slot.RiotGameName) || string.IsNullOrWhiteSpace(slot.RiotTagLine)))
                .Select(GetPlayerCacheKey).Distinct().ToArray();
            await Task.WhenAll(missing.Select(ResolvePlayerProfileAsync));
            FillCachedPlayerProfiles(state);
        }

        private static string GetPlayerCacheKey(RemoteTeamSlotDto slot)
            => !string.IsNullOrWhiteSpace(slot.PlayerPuuid) ? "p:" + slot.PlayerPuuid :
                !string.IsNullOrWhiteSpace(slot.PlayerSummonerId) && slot.PlayerSummonerId.All(char.IsDigit) ? "s:" + slot.PlayerSummonerId : string.Empty;

        private async Task ResolvePlayerProfileAsync(string key)
        {
            if (_playerProfiles.TryGetValue(key, out PlayerProfileCache? cached) && cached.ExpiresUtc > DateTime.UtcNow)
                return;

            string value = key[2..];
            string path = Uri.EscapeDataString(value);
            string[] response = key.StartsWith("p:", StringComparison.Ordinal)
                ? await LCUService.ClientRequestAsync("GET", $"lol-summoner/v2/summoners/puuid/{path}")
                : await LCUService.ClientRequestAsync("GET", $"lol-summoner/v1/summoners/{path}");
            if (response[0] != "200" && key.StartsWith("p:", StringComparison.Ordinal))
                response = await LCUService.ClientRequestAsync("GET", $"lol-summoner/v1/summoners-by-puuid-cached/{path}");

            string gameName = string.Empty, tagLine = string.Empty;
            if (response[0] == "200")
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(response[1]);
                    JsonElement root = document.RootElement;
                    string returnedPuuid = GetStringProperty(root, "puuid") ?? string.Empty;
                    if (!key.StartsWith("p:", StringComparison.Ordinal) || string.IsNullOrEmpty(returnedPuuid) || returnedPuuid == value)
                    {
                        gameName = GetStringProperty(root, "gameName", "riotIdGameName") ?? string.Empty;
                        tagLine = GetStringProperty(root, "tagLine", "riotIdTagLine", "riotIdTagline") ?? string.Empty;
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"Could not read player profile: {ex.Message}"); }
            }
            _playerProfiles[key] = new PlayerProfileCache(gameName, tagLine,
                DateTime.UtcNow.AddSeconds(string.IsNullOrEmpty(gameName) || string.IsNullOrEmpty(tagLine) ? 15 : 120));
        }

        private void FillCachedPlayerProfiles(RemotePickState state)
        {
            foreach (RemoteTeamSlotDto slot in state.AllySlots.Concat(state.EnemySlots))
            {
                if (!slot.IsPlayerNameVisible) continue;
                string gameName = slot.RiotGameName, tagLine = slot.RiotTagLine;
                if ((string.IsNullOrWhiteSpace(gameName) || string.IsNullOrWhiteSpace(tagLine)) &&
                    !string.IsNullOrEmpty(GetPlayerCacheKey(slot)) &&
                    _playerProfiles.TryGetValue(GetPlayerCacheKey(slot), out PlayerProfileCache? cached) && cached.ExpiresUtc > DateTime.UtcNow)
                {
                    gameName = cached.GameName;
                    tagLine = cached.TagLine;
                }
                if (string.IsNullOrWhiteSpace(gameName) || string.IsNullOrWhiteSpace(tagLine)) continue;
                slot.ProfileUrl = RemotePlayerProfileLinks.Create(_profileRegion, gameName, tagLine);
                if (!string.IsNullOrEmpty(slot.ProfileUrl)) slot.PlayerName = $"{gameName}#{tagLine}";
            }
        }

        private void EnrichTeamSlots(RemotePickState state)
        {
            if (_remoteChampionCache == null) return;

            List<RemoteTeamSlotDto> slots = state.AllySlots.Concat(state.EnemySlots).ToList();
            if (slots.Any(slot =>
                (slot.ChampionId > 0 || slot.IntentChampionId > 0) &&
                !_remoteChampionCache.Any(champion => champion.Id == (slot.ChampionId > 0 ? slot.ChampionId : slot.IntentChampionId))))
                EnsureAllChampionMetadata();

            foreach (RemoteTeamSlotDto slot in slots)
            {
                int id = slot.ChampionId > 0 ? slot.ChampionId : slot.IntentChampionId;
                RemoteChampionDto? champion = _remoteChampionCache.FirstOrDefault(candidate => candidate.Id == id);
                if (champion == null && _allChampionMetadata != null)
                    _allChampionMetadata.TryGetValue(id, out champion);
                if (champion != null)
                {
                    slot.Name = champion.Name;
                    slot.ImageUrl = champion.ImageUrl;
                }
            }
        }

        private void EnsureAllChampionMetadata()
        {
            if (_allChampionMetadata != null || DateTime.UtcNow < _nextChampionMetadataAttemptUtc) return;
            _nextChampionMetadataAttemptUtc = DateTime.UtcNow.AddSeconds(30);
            string[] response = LCUService.ClientRequest("GET", "lol-game-data/assets/v1/champion-summary.json");
            if (response[0] != "200") return;

            try
            {
                using JsonDocument document = JsonDocument.Parse(response[1]);
                if (document.RootElement.ValueKind != JsonValueKind.Array) return;

                var metadata = new Dictionary<int, RemoteChampionDto>();
                foreach (JsonElement champion in document.RootElement.EnumerateArray())
                {
                    if (champion.ValueKind != JsonValueKind.Object) continue;
                    int id = GetIntProperty(champion, "id");
                    if (id <= 0) continue;
                    string name = GetStringProperty(champion, "name") ?? $"Champion #{id}";
                    string alias = GetStringProperty(champion, "alias") ?? name;
                    metadata[id] = new RemoteChampionDto
                    {
                        Id = id,
                        Name = name,
                        ImageUrl = DataDragonService.GetChampionImageUrl(alias)
                    };
                }
                _allChampionMetadata = metadata;
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"Could not read champion metadata for team picks: {ex.Message}");
            }
        }

        private static void ParseQueueAndMode(JsonElement root, RemotePickState state)
        {
            if (TryGetPropertyInt(root, "mapId", out int mapId)) state.MapId = mapId;
            if (TryGetPropertyInt(root, "queueId", out int queueId)) state.QueueId = queueId;

            state.IsRandomChampionMode = RemoteRandomSelection.IsRandom(root, state.QueueId, state.GameMode);
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

            ReadTimerFields(timerElement, state);
        }

        private static void ReadTimerFields(JsonElement timerElement, RemotePickState state)
        {
            state.ChampSelectPhase = GetStringProperty(timerElement, "phase") ?? string.Empty;
            if (TryGetPropertyInt(timerElement, "adjustedTimeLeftInPhase", out int adjustedLeftMs) && adjustedLeftMs >= 0)
                state.TimeLeftInPhaseMs = adjustedLeftMs;
            else if (TryGetPropertyInt(timerElement, "timeLeftInPhase", out int timeLeftMs) && timeLeftMs >= 0)
                state.TimeLeftInPhaseMs = timeLeftMs;
            if (TryGetPropertyInt(timerElement, "totalTimeInPhase", out int totalMs))
                state.TotalTimeInPhaseMs = Math.Max(0, totalMs);
            state.IsTimerInfinite = GetBoolProperty(timerElement, "isInfinite");
        }

        private async Task SyncDraftClockAsync(JsonElement session, RemotePickState state)
        {
            long epoch = 0;
            if (session.TryGetProperty("timer", out var timer) && timer.ValueKind == JsonValueKind.Object)
                TryGetPropertyLong(timer, "internalNowInEpochMs", out epoch);
            if (!state.IsTimerInfinite && (epoch <= 0 || state.TimeLeftInPhaseMs < 0))
            {
                using var live = await ReadClientJsonAsync("lol-champ-select/v1/session/timer");
                if (live != null && (state.ChampSelectPhase.Length == 0 || GetStringProperty(live.RootElement, "phase") == state.ChampSelectPhase))
                {
                    ReadTimerFields(live.RootElement, state);
                    TryGetPropertyLong(live.RootElement, "internalNowInEpochMs", out epoch);
                }
            }
            state.TimerMeasuredTickMs = Environment.TickCount64;
            if (state.IsTimerInfinite)
            {
                _phaseClock.Reset(state.TimerMeasuredTickMs);
                return;
            }
            TryGetPropertyLong(session, "gameId", out long gameId);
            // The other simultaneous bans finishing does not start a new local turn.
            string turn = $"{gameId}|{state.ChampSelectPhase}|{state.ActionGroupIndex}";
            state.TimeLeftInPhaseMs = _phaseClock.Remaining(turn, state.TimeLeftInPhaseMs, epoch,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), state.TimerMeasuredTickMs);
        }

        private static void ParseActions(JsonElement root, RemotePickState state) => RemoteDraftActions.Read(root, state);

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

        private static void AddChampionIdsFromTeam(JsonElement root, string propertyName, HashSet<int> championIds, HashSet<int>? teamChampionIds = null)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement teamElement) || teamElement.ValueKind != JsonValueKind.Array)
                return;
            foreach (JsonElement player in teamElement.EnumerateArray())
            {
                if (TryGetPropertyInt(player, "championId", out int championId) && championId > 0)
                {
                    championIds.Add(championId);
                    teamChampionIds?.Add(championId);
                }
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

            if (!state.IsGameClientRunning)
                return;

            TryReadLiveGameStats("https://127.0.0.1:2999/liveclientdata/gamestats", state);
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

        private static bool IsKnownRandomChampionQueue(int queueId) => queueId is 450 or 720 or 2400;


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
                2400 => "ARAM Mayhem",
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
            if (state.IsPlanning) return "Hover your champion";
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
