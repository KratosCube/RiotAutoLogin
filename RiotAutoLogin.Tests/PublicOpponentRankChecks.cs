using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using RiotAutoLogin.Models;
using RiotAutoLogin.Services;

internal static class PublicOpponentRankChecks
{
    internal static async Task Run()
    {
        foreach (string platform in new[] { "eun1", "euw1" })
        foreach (var queue in Enum.GetValues<RankedQueue>())
        {
            int queueId = queue switch { RankedQueue.RankedFives => 710, RankedQueue.Flex => 440, _ => 420 };
            var calls = new List<string>();
            bool missingRanks = false, wrongIdentity = false;
            var service = new PublicOpponentRankService(Get);
            var sample = await service.ReadAsync("Two Words", "TAG", platform, queue, "key-a", default);
            Require(sample is { Games: 10, RankedOpponents: 40, TotalOpponents: 50, MeanRankPosition: 1325, Source: "Riot API" },
                $"{platform}/{queue} must average only the five enemies from ten completed matches.");
            Require(calls.Count(url => url.Contains("/league/v4/")) == 5, "Repeated enemy ranks must share the cache.");
            Require(calls.Any(url => url.Contains("queue=" + queueId)), "The public history must filter the selected queue.");
            Require(calls.All(url => url.StartsWith("https://europe.api.riotgames.com/") || url.StartsWith($"https://{platform}.api.riotgames.com/")),
                "History and rank requests must use the correct regional and platform routing.");
            Require(!calls.Any(url => url.EndsWith("/" + platform.ToUpperInvariant() + "_1")), "Stop after ten eligible matches.");
            calls.Clear();
            await service.ReadAsync("Two Words", "TAG", platform, queue, "key-a", default);
            Require(calls.Count == 2, "A repeat refresh must reuse completed matches and enemy ranks.");
            calls.Clear();
            await service.ReadAsync("Two Words", "TAG", platform, queue, "key-b", default);
            Require(calls.Count(url => url.Contains("/league/v4/")) == 5, "Changing API keys must discard encrypted-identifier caches.");
            wrongIdentity = true;
            Require(await service.ReadAsync("Two Words", "TAG", platform, queue, "key-b", default) == null,
                "A mismatched Riot ID must not produce another account's sample.");
            wrongIdentity = false;
            missingRanks = true;
            Require(await service.ReadAsync("Two Words", "TAG", platform, queue, "key-c", default) == null,
                "An unavailable rank source must preserve the saved sample.");

            Task<JsonElement?> Get(string url, string key, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                calls.Add(url);
                if (url.Contains("/accounts/by-riot-id/"))
                {
                    Require(url.EndsWith("/Two%20Words/TAG"), "Riot ID path components must be encoded.");
                    return Json(new { gameName = "Two Words", tagLine = wrongIdentity ? "OTHER" : "TAG", puuid = "public-self" });
                }
                if (url.Contains("/ids?"))
                    return Json(new[] { "OTHER1_99", platform.ToUpperInvariant() + "_14", platform.ToUpperInvariant() + "_13", platform.ToUpperInvariant() + "_12" }
                        .Concat(Enumerable.Range(1, 12).Reverse().Select(id => platform.ToUpperInvariant() + "_" + id)).ToArray());
                if (url.Contains("/matches/"))
                {
                    string id = Uri.UnescapeDataString(url.Split('/').Last());
                    int number = int.Parse(id.Split('_').Last());
                    return Json(new
                    {
                        metadata = new { matchId = id },
                        info = new
                        {
                            queueId = number == 14 ? 450 : queueId, gameDuration = 1200,
                            participants = Enumerable.Range(1, 10).Select(i => new
                            {
                                puuid = i == 7 ? "public-self" : "enemy-" + i, teamId = i <= 5 ? 100 : 200,
                                gameEndedInEarlySurrender = number == 13
                            }).ToArray()
                        }
                    });
                }
                if (url.Contains("/league/v4/"))
                {
                    if (missingRanks) return Task.FromResult<JsonElement?>(null);
                    int enemy = int.Parse(url.Split('-').Last());
                    Require(enemy is >= 1 and <= 5, "An ally's rank was requested.");
                    if (enemy == 5) return Json(Array.Empty<object>());
                    string[] tiers = ["GOLD", "GOLD", "PLATINUM", "SILVER"];
                    string[] ranks = ["IV", "II", "IV", "I"];
                    // The weekend API can report explicit unranked entries too.
                    return Json(new[] { new { queueType = RankedQueues.ApiKey(queue), tier = tiers[enemy - 1], rank = ranks[enemy - 1], leaguePoints = 0 } });
                }
                throw new Exception("Unexpected public lookup path.");
            }
        }
        await CheckSources();
        await CheckTransport();
        Console.WriteLine("Public opponent ranks, EUW/EUNE routing, fallback, caching and Riot API budget checks passed.");
    }

    private static async Task CheckSources()
    {
        int localCalls = 0, publicCalls = 0;
        bool failLocal = false, failPublic = false;
        var sample = new OpponentRankSummary { MeanRankPosition = 1500 };
        var source = new OpponentRankSources(
            (_, _, region, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                localCalls++;
                if (failLocal) throw new HttpRequestException();
                return Task.FromResult(region == "euw1" ? sample : null);
            },
            (_, _, _, _, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                publicCalls++;
                if (failPublic) throw new RiotApiException(HttpStatusCode.Forbidden);
                return Task.FromResult<OpponentRankSummary?>(sample);
            });
        await source.ReadAsync("Name", "TAG", "euw1", RankedQueue.SoloDuo, true, "key", default);
        Require(localCalls == 1 && publicCalls == 0, "The available local sample must be preferred.");
        var remote = await source.ReadAsync("Name", "TAG", "eun1", RankedQueue.SoloDuo, true, "key", default);
        Require(remote.Summary == sample && publicCalls == 1, "An account outside the client's region must use the public API.");
        await source.ReadAsync("Name", "TAG", "euw1", RankedQueue.SoloDuo, false, "key", default);
        Require(localCalls == 2 && publicCalls == 2, "A closed client must not prevent public refresh.");
        failLocal = true;
        await source.ReadAsync("Name", "TAG", "euw1", RankedQueue.SoloDuo, true, "key", default);
        Require(publicCalls == 3, "Local service errors must fall back to public data.");
        var noKey = await source.ReadAsync("Name", "TAG", "eun1", RankedQueue.SoloDuo, false, "", default);
        Require(noKey.Summary == null && noKey.Status.Contains("Settings") && publicCalls == 3, "Missing keys must provide useful status without a public call.");
        failPublic = true;
        var rejected = await source.ReadAsync("Name", "TAG", "eun1", RankedQueue.SoloDuo, false, "expired", default);
        Require(rejected.Summary == null && rejected.Status.Contains("expired"), "Rejected keys must preserve saved data and explain recovery.");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await source.ReadAsync("Name", "TAG", "eun1", RankedQueue.SoloDuo, false, "key", cancelled.Token); throw new Exception("Cancellation swallowed."); }
        catch (OperationCanceledException) { }
        var account = new Account();
        account.SetOpponentRankStatus(RankedQueue.SoloDuo, "Expired key");
        account.SelectedQueue = RankedQueue.Flex;
        Require(!account.OpponentRankTooltip.Contains("Expired key"), "Refresh statuses must follow their queue.");
        account.SelectedQueue = RankedQueue.SoloDuo;
        Require(account.OpponentRankTooltip.Contains("Expired key"), "Selected queue must show its recovery status.");
        var restored = JsonSerializer.Deserialize<Account>(JsonSerializer.Serialize(account))!;
        Require(restored.OpponentRankRefreshStatus == "", "Transient refresh status must not survive restart.");
    }

    private static async Task CheckTransport()
    {
        var now = DateTimeOffset.UtcNow;
        var waits = new List<TimeSpan>();
        var status = HttpStatusCode.OK;
        int requests = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            requests++;
            Require(request.Headers.Contains("X-Riot-Token") && !request.RequestUri!.Query.Contains("key"), "API keys must use headers.");
            var response = new HttpResponseMessage(status) { Content = new StringContent("[]") };
            if (status == HttpStatusCode.TooManyRequests) response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        }));
        var client = new RiotApiClient(http, () => now, (duration, token) =>
        {
            token.ThrowIfCancellationRequested();
            waits.Add(duration); now += duration; return Task.CompletedTask;
        });
        const string url = "https://eun1.api.riotgames.com/test";
        for (int i = 0; i < 91; i++) await client.GetAsync(url, "key", default);
        Require(waits.Any(wait => wait > TimeSpan.FromSeconds(100)), "The shared two-minute request budget must delay excess calls.");
        status = HttpStatusCode.TooManyRequests;
        try { await client.GetAsync(url, "key", default); throw new Exception("Rate limit swallowed."); }
        catch (RiotApiException) { }
        status = HttpStatusCode.OK;
        waits.Clear();
        await client.GetAsync(url, "key", default);
        Require(waits.Sum(x => x.TotalSeconds) >= 30, "Future calls must honor Retry-After.");
        status = HttpStatusCode.Forbidden;
        int beforeRejected = requests;
        for (int i = 0; i < 2; i++)
        {
            try { await client.GetAsync(url, "expired", default); throw new Exception("Expired key accepted."); }
            catch (RiotApiException) { }
        }
        int before = requests;
        Require(before == beforeRejected + 1, "An expired key must not be retried for every saved account.");
        status = HttpStatusCode.OK;
        await client.GetAsync(url, "replacement", default);
        Require(requests == before + 1, "A replacement key must work without restarting.");
        try { await client.GetAsync("https://example.com/", "key", default); throw new Exception("Credential host accepted."); }
        catch (InvalidOperationException) { }
        Require(requests == before + 1, "The API key must never be sent to another host.");
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(respond(request));
    }
    private static Task<JsonElement?> Json(object value) => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(value));
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
