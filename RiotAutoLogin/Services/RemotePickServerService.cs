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
        public string LocalUrl => $"http://{GetLocalIPv4Address()}:{Port}/";

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
            Debug.WriteLine($"Remote Pick server started at {LocalUrl}");
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
                state.Message = "League Client is not running.";
                await AddChampionAndSpellListsAsync(state, new HashSet<int>(), new HashSet<int>());
                return state;
            }

            state.Phase = await LCUService.GetCurrentGamePhaseAsync();
            state.IsInChampSelect = state.Phase == "ChampSelect";

            var bannedChampionIds = new HashSet<int>();
            var pickedChampionIds = new HashSet<int>();

            if (!state.IsInChampSelect)
            {
                state.Message = state.Phase == "None"
                    ? "Waiting for League Client state..."
                    : $"Current phase: {state.Phase}. Waiting for champion select.";

                await AddChampionAndSpellListsAsync(state, bannedChampionIds, pickedChampionIds);
                return state;
            }

            string[] sessionResult = LCUService.ClientRequest("GET", "lol-champ-select/v1/session");
            if (sessionResult[0] != "200")
            {
                state.Message = $"Could not read champion select session. Status: {sessionResult[0]}";
                await AddChampionAndSpellListsAsync(state, bannedChampionIds, pickedChampionIds);
                return state;
            }

            using JsonDocument doc = JsonDocument.Parse(sessionResult[1]);
            JsonElement root = doc.RootElement;

            ParseBans(root, bannedChampionIds);
            ParsePicks(root, pickedChampionIds);
            ParseLocalPlayerSelection(root, state);
            ParseCurrentAction(root, state);

            state.BannedChampionIds = bannedChampionIds.OrderBy(id => id).ToList();
            state.PickedChampionIds = pickedChampionIds.OrderBy(id => id).ToList();
            state.Message = state.IsMyTurn
                ? state.ActionType == "pick"
                    ? "It is your pick. Lock in or hover a champion."
                    : state.ActionType == "ban"
                        ? "It is your ban. Choose a champion to ban."
                        : $"It is your {state.ActionType} turn."
                : "You can hover an intended pick while waiting for your turn.";

            await AddChampionAndSpellListsAsync(state, bannedChampionIds, pickedChampionIds);
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

            if (!string.IsNullOrWhiteSpace(state.ActionId) && state.ActionType == "pick")
            {
                success = LCUService.SelectChampion(request.ChampionId, state.ActionId, complete: false);
            }

            if (!success)
            {
                string bodyJson = JsonSerializer.Serialize(new { championId = request.ChampionId }, _jsonOptions);
                string[] result = LCUService.ClientRequest("PATCH", "lol-champ-select/v1/session/my-selection", bodyJson);
                success = result[0].StartsWith("2");
            }

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

            bool success = LCUService.SelectSummonerSpell(request.SpellId, request.Slot);
            return new RemotePickActionResult
            {
                Success = success,
                Message = success ? "Summoner spell updated." : "League Client rejected the spell change."
            };
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
                    IsSpell2 = state.Spell2Id == spell.Id
                })
                .ToList();
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

        private static void ParseCurrentAction(JsonElement root, RemotePickState state)
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
                    if (!action.TryGetProperty("isInProgress", out JsonElement inProgressElement) || !inProgressElement.GetBoolean())
                        continue;

                    if (!action.TryGetProperty("actorCellId", out JsonElement actorCellElement) ||
                        !TryGetInt(actorCellElement, out int actorCellId) ||
                        actorCellId != localPlayerCellId)
                    {
                        continue;
                    }

                    state.IsMyTurn = true;
                    state.ActionId = action.TryGetProperty("id", out JsonElement idElement)
                        ? GetJsonValueAsString(idElement)
                        : string.Empty;
                    state.ActionType = action.TryGetProperty("type", out JsonElement typeElement)
                        ? (typeElement.GetString() ?? string.Empty).ToLowerInvariant()
                        : string.Empty;
                    return;
                }
            }
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

        private static string GetLocalIPv4Address()
        {
            try
            {
                foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up)
                        continue;

                    if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    IPInterfaceProperties properties = networkInterface.GetIPProperties();
                    foreach (UnicastIPAddressInformation address in properties.UnicastAddresses)
                    {
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(address.Address))
                        {
                            return address.Address.ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not get local IP address: {ex.Message}");
            }

            return "127.0.0.1";
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
