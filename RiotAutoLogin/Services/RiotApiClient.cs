using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services;

internal sealed class RiotApiException(HttpStatusCode status) : HttpRequestException(
    status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Riot API key is invalid or expired. Update it in Settings.",
        HttpStatusCode.TooManyRequests => "Riot API limit reached. Refresh will resume after the server's cooldown.",
        _ => $"Riot API is unavailable (HTTP {(int)status}). Saved statistics are retained."
    }, null, status);

// Shared by own-rank and opponent lookups. Credentials are sent only to Riot hosts.
internal sealed class RiotApiClient(HttpClient http, Func<DateTimeOffset>? clock = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    internal static RiotApiClient Shared { get; } = new(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(10) });
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<(string Key, string Host), Budget> _budgets = new();
    private readonly Dictionary<string, DateTimeOffset> _rejectedKeys = new();
    private sealed class Budget
    {
        internal readonly Queue<DateTimeOffset> Calls = new();
        internal DateTimeOffset ResumeAt;
    }

    internal static string KeyId(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    internal async Task<JsonElement?> GetAsync(string url, string key, CancellationToken token = default)
    {
        var uri = new Uri(url);
        if (uri.Scheme != "https" || !uri.Host.EndsWith(".api.riotgames.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Port != 443 || uri.UserInfo.Length != 0 || string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("A Riot API host and API key are required.");
        string keyId = KeyId(key);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_rejectedKeys.TryGetValue(keyId, out var until) && until > _clock())
                throw new RiotApiException(HttpStatusCode.Forbidden);
            if (!_budgets.TryGetValue((keyId, uri.Host), out var budget))
            {
                if (_budgets.Count >= 100) _budgets.Clear();
                _budgets[(keyId, uri.Host)] = budget = new();
            }
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var now = _clock();
                while (budget.Calls.TryPeek(out var first) && now - first >= TimeSpan.FromSeconds(121))
                    budget.Calls.Dequeue();
                // Leave headroom below the default 20/s and 100/2min application limits.
                var resume = budget.ResumeAt;
                if (budget.Calls.Count >= 90) resume = Max(resume, budget.Calls.Peek().AddSeconds(121));
                var shortWindow = budget.Calls.Where(t => now - t < TimeSpan.FromSeconds(1.1)).ToArray();
                if (shortWindow.Length >= 15) resume = Max(resume, shortWindow[0].AddSeconds(1.1));
                if (resume <= now) break;
                await _delay(resume - now, token).ConfigureAwait(false);
            }
            budget.Calls.Enqueue(_clock());
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("X-Riot-Token", key);
            using var response = await http.SendAsync(request, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retry = response.Headers.RetryAfter;
                budget.ResumeAt = Max(budget.ResumeAt, retry?.Date ?? _clock().Add(retry?.Delta ?? TimeSpan.FromMinutes(2)));
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                if (_rejectedKeys.Count >= 100) _rejectedKeys.Clear();
                _rejectedKeys[keyId] = _clock().AddMinutes(5);
            }
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode) throw new RiotApiException(response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
            return json.RootElement.Clone();
        }
        finally { _gate.Release(); }
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
