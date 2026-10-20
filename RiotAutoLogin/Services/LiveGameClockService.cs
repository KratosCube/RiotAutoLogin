using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services
{
    public static class LiveGameClockService
    {
        private static readonly HttpClient Client = new(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            UseProxy = false
        }) { Timeout = TimeSpan.FromMilliseconds(900) };
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static long _checkedAt = long.MinValue;
        private static double? _seconds;

        public static async Task<double?> ReadAsync(CancellationToken cancellationToken = default)
        {
            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_checkedAt != long.MinValue && Environment.TickCount64 - _checkedAt < 500) return _seconds;
                _seconds = null;
                try
                {
                    string json = await Client.GetStringAsync("https://127.0.0.1:2999/liveclientdata/gamestats", cancellationToken).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("gameTime", out var time) && time.TryGetDouble(out double seconds) && double.IsFinite(seconds))
                        _seconds = Math.Max(0, seconds);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException) { }
                _checkedAt = Environment.TickCount64;
                return _seconds;
            }
            finally { Gate.Release(); }
        }
    }
}
