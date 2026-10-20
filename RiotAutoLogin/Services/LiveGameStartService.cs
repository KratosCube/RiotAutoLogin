using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services
{
    public static class LiveGameStartService
    {
        private static readonly HttpClient Client = new(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            UseProxy = false
        }) { Timeout = TimeSpan.FromMilliseconds(900) };
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static long _checkedAt = long.MinValue;
        private static bool? _started;

        public static async Task<bool?> ReadAsync(CancellationToken cancellationToken = default)
        {
            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_checkedAt != long.MinValue && Environment.TickCount64 - _checkedAt < 500) return _started;
                _started = null;
                try
                {
                    string json = await Client.GetStringAsync(
                        "https://127.0.0.1:2999/liveclientdata/eventdata", cancellationToken).ConfigureAwait(false);
                    _started = Parse(json);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException) { }
                _checkedAt = Environment.TickCount64;
                return _started;
            }
            finally { Gate.Release(); }
        }

        // Null means the endpoint returned an invalid response; false is a valid
        // event list that has not yet reported the beginning of play.
        public static bool? Parse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                    !doc.RootElement.TryGetProperty("Events", out var events) ||
                    events.ValueKind != JsonValueKind.Array)
                    return null;
                foreach (var item in events.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object &&
                        item.TryGetProperty("EventName", out var name) &&
                        name.ValueKind == JsonValueKind.String &&
                        name.GetString() == "GameStart")
                        return true;
                }
                return false;
            }
            catch (JsonException) { return null; }
        }
    }
}
