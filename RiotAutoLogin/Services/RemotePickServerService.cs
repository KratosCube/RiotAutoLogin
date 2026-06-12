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
                    await WriteHtmlResponseAsync(stream, GetIndexHtml(), cancellationToken);
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
                await AddChampionListAsync(state, new HashSet<int>(), new HashSet<int>());
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

                await AddChampionListAsync(state, bannedChampionIds, pickedChampionIds);
                return state;
            }

            string[] sessionResult = LCUService.ClientRequest("GET", "lol-champ-select/v1/session");
            if (sessionResult[0] != "200")
            {
                state.Message = $"Could not read champion select session. Status: {sessionResult[0]}";
                await AddChampionListAsync(state, bannedChampionIds, pickedChampionIds);
                return state;
            }

            using JsonDocument doc = JsonDocument.Parse(sessionResult[1]);
            JsonElement root = doc.RootElement;

            ParseBans(root, bannedChampionIds);
            ParsePicks(root, pickedChampionIds);
            ParseCurrentAction(root, state);

            state.BannedChampionIds = bannedChampionIds.OrderBy(id => id).ToList();
            state.PickedChampionIds = pickedChampionIds.OrderBy(id => id).ToList();
            state.Message = state.IsMyTurn
                ? state.ActionType == "pick"
                    ? "It is your pick. Choose a champion."
                    : $"It is your {state.ActionType} turn. Remote Pick currently supports picking only."
                : "Waiting for your pick turn.";

            await AddChampionListAsync(state, bannedChampionIds, pickedChampionIds);
            return state;
        }

        public async Task<RemotePickActionResult> PickChampionAsync(string body)
        {
            RemotePickRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<RemotePickRequest>(body, _jsonOptions);
            }
            catch
            {
                return new RemotePickActionResult { Success = false, Message = "Invalid pick request." };
            }

            if (request == null || request.ChampionId <= 0)
                return new RemotePickActionResult { Success = false, Message = "Invalid champion." };

            RemotePickState state = await GetStateAsync();
            if (!state.IsInChampSelect)
                return new RemotePickActionResult { Success = false, Message = "You are not in champion select." };

            if (!state.IsMyTurn || !state.ActionType.Equals("pick", StringComparison.OrdinalIgnoreCase))
                return new RemotePickActionResult { Success = false, Message = "It is not your pick turn." };

            if (state.BannedChampionIds.Contains(request.ChampionId))
                return new RemotePickActionResult { Success = false, Message = "This champion is banned." };

            if (state.PickedChampionIds.Contains(request.ChampionId))
                return new RemotePickActionResult { Success = false, Message = "This champion is already picked." };

            bool success = LCUService.SelectChampion(request.ChampionId, state.ActionId, complete: true);
            return new RemotePickActionResult
            {
                Success = success,
                Message = success ? "Champion picked." : "League Client rejected the pick request."
            };
        }

        private async Task AddChampionListAsync(RemotePickState state, HashSet<int> bannedChampionIds, HashSet<int> pickedChampionIds)
        {
            _championCache ??= await DataDragonService.GetAllChampionsAsync();

            state.Champions = _championCache
                .Where(champion => champion.Id > 0)
                .OrderBy(champion => champion.Name)
                .Select(champion => new RemoteChampionDto
                {
                    Id = champion.Id,
                    Name = champion.Name,
                    IsBanned = bannedChampionIds.Contains(champion.Id),
                    IsPicked = pickedChampionIds.Contains(champion.Id)
                })
                .ToList();
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
                        ? typeElement.GetString() ?? string.Empty
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

        private static string GetIndexHtml()
        {
            return """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>RiotAutoLogin Remote Pick</title>
  <style>
    :root { color-scheme: dark; font-family: system-ui, -apple-system, Segoe UI, sans-serif; }
    body { margin: 0; background: #0b111a; color: #e6edf7; }
    header { position: sticky; top: 0; z-index: 2; padding: 14px 16px; background: rgba(14, 21, 31, .96); border-bottom: 1px solid #263447; }
    h1 { margin: 0; font-size: 20px; }
    #status { margin-top: 6px; color: #a8b3c7; font-size: 14px; }
    #turn { margin-top: 8px; font-weight: 700; }
    main { padding: 14px; }
    input { width: 100%; box-sizing: border-box; padding: 13px 14px; margin-bottom: 14px; border-radius: 12px; border: 1px solid #2f4058; background: #111a27; color: #e6edf7; font-size: 16px; }
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(135px, 1fr)); gap: 10px; }
    button.champion { min-height: 62px; padding: 10px; border: 1px solid #33465f; border-radius: 14px; background: #141f2e; color: #e6edf7; font-size: 14px; text-align: left; }
    button.champion:not(:disabled):active { transform: scale(.98); }
    button.champion:not(:disabled) { cursor: pointer; }
    button.champion.disabled { opacity: .38; text-decoration: line-through; }
    .tag { display: block; margin-top: 5px; color: #f2a365; font-size: 12px; }
    .picked .tag { color: #91d7ff; }
    .toast { position: fixed; left: 14px; right: 14px; bottom: 14px; padding: 13px 14px; border-radius: 12px; background: #172235; border: 1px solid #33465f; display: none; }
  </style>
</head>
<body>
  <header>
    <h1>Remote Pick</h1>
    <div id="status">Connecting...</div>
    <div id="turn"></div>
  </header>
  <main>
    <input id="search" placeholder="Search champion..." autocomplete="off">
    <div id="champions" class="grid"></div>
  </main>
  <div id="toast" class="toast"></div>

  <script>
    let state = null;
    let query = '';
    const championsEl = document.getElementById('champions');
    const statusEl = document.getElementById('status');
    const turnEl = document.getElementById('turn');
    const searchEl = document.getElementById('search');
    const toastEl = document.getElementById('toast');

    searchEl.addEventListener('input', () => {
      query = searchEl.value.trim().toLowerCase();
      render();
    });

    function showToast(message) {
      toastEl.textContent = message;
      toastEl.style.display = 'block';
      clearTimeout(showToast.timer);
      showToast.timer = setTimeout(() => toastEl.style.display = 'none', 2500);
    }

    async function loadState() {
      try {
        const response = await fetch('/api/state', { cache: 'no-store' });
        state = await response.json();
        render();
      } catch (error) {
        statusEl.textContent = 'Disconnected from Remote Pick server.';
        turnEl.textContent = '';
      }
    }

    function render() {
      if (!state) return;

      statusEl.textContent = state.message || `Phase: ${state.phase}`;
      turnEl.textContent = state.isMyTurn && state.actionType === 'pick'
        ? 'Your pick turn'
        : state.isInChampSelect ? 'Watching champ select' : 'Waiting';

      const filtered = state.champions.filter(champion => champion.name.toLowerCase().includes(query));
      championsEl.innerHTML = '';

      for (const champion of filtered) {
        const button = document.createElement('button');
        button.className = 'champion' + (champion.isDisabled ? ' disabled' : '') + (champion.isPicked ? ' picked' : '');
        button.disabled = champion.isDisabled || !state.isMyTurn || state.actionType !== 'pick';
        button.innerHTML = `<strong>${escapeHtml(champion.name)}</strong>${champion.isBanned ? '<span class="tag">Banned</span>' : champion.isPicked ? '<span class="tag">Picked</span>' : ''}`;
        button.onclick = () => pick(champion.id, champion.name);
        championsEl.appendChild(button);
      }
    }

    async function pick(championId, championName) {
      if (!confirm(`Lock in ${championName}?`)) return;

      const response = await fetch('/api/pick', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ championId })
      });
      const result = await response.json();
      showToast(result.message || (result.success ? 'Picked.' : 'Pick failed.'));
      await loadState();
    }

    function escapeHtml(text) {
      return text.replace(/[&<>'"]/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#039;', '"': '&quot;' }[char]));
    }

    loadState();
    setInterval(loadState, 800);
  </script>
</body>
</html>
""";
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
