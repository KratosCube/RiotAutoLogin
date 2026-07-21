using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services
{
    public static class AutoAcceptDeclineGuardService
    {
        private static readonly object SyncRoot = new();
        private static CancellationTokenSource? _cts;
        private static Task? _monitorTask;
        private static bool _manualDeclineActive;

        [ModuleInitializer]
        internal static void Initialize()
        {
            Start();
        }

        public static void Start()
        {
            lock (SyncRoot)
            {
                if (_cts != null)
                    return;

                _cts = new CancellationTokenSource();
                _monitorTask = Task.Run(() => MonitorAsync(_cts.Token));
            }
        }

        public static void Stop()
        {
            lock (SyncRoot)
            {
                try
                {
                    _cts?.Cancel();
                    _cts?.Dispose();
                }
                catch
                {
                }

                _cts = null;
                _monitorTask = null;
                _manualDeclineActive = false;
            }
        }

        private static async Task MonitorAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!LCUService.CheckIfLeagueClientIsOpen())
                    {
                        _manualDeclineActive = false;
                        await Task.Delay(1000, cancellationToken);
                        continue;
                    }

                    string phase = await LCUService.GetCurrentGamePhaseAsync();
                    if (!string.Equals(phase, "ReadyCheck", StringComparison.OrdinalIgnoreCase))
                    {
                        _manualDeclineActive = false;
                        await Task.Delay(500, cancellationToken);
                        continue;
                    }

                    string[] result = LCUService.ClientRequest("GET", "lol-matchmaking/v1/ready-check");
                    if (result[0] == "200" && TryGetPlayerResponse(result[1], out string playerResponse))
                    {
                        if (string.Equals(playerResponse, "Declined", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!_manualDeclineActive)
                                Debug.WriteLine("Manual ReadyCheck decline detected. Cancelling delayed auto-accept.");

                            _manualDeclineActive = true;
                            LCUService.StopAutoAccept();
                        }
                        else if (string.Equals(playerResponse, "None", StringComparison.OrdinalIgnoreCase))
                        {
                            _manualDeclineActive = false;
                        }
                    }

                    if (_manualDeclineActive)
                        LCUService.StopAutoAccept();

                    await Task.Delay(200, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Auto-accept decline guard error: {ex.Message}");
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }

        private static bool TryGetPlayerResponse(string json, out string playerResponse)
        {
            playerResponse = string.Empty;

            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("playerResponse", out JsonElement responseElement) ||
                    responseElement.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                playerResponse = responseElement.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(playerResponse);
            }
            catch
            {
                return false;
            }
        }
    }
}
