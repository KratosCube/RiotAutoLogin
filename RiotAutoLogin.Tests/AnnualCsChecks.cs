using System.Text.Json;
using RiotAutoLogin.Models;
using RiotAutoLogin.Services;

internal static class AnnualCsChecks
{
    internal static async Task Run()
    {
        var now = DateTimeOffset.Parse("2026-11-25T12:00:00Z");
        var urls = new List<string>();
        var service = new AnnualCsService(Get);
        var account = new Account { GameName = "Farmer", TagLine = "TAG", Region = "eun1" };
        account.AnnualCs.Matches["old"] = new CsMatch { Cs = 999, Seconds = 900, Started = now.AddYears(-2).ToUnixTimeSeconds() };
        var first = await service.StepAsync("Farmer", "TAG", "eun1", "key", account.AnnualCs, now, default);
        Require(first.Scanning && first.Pending.Count > 0 && first.Matches.Count == 24, "Import must checkpoint after 25 requests, excluding one unavailable game.");
        Require(account.AnnualCs.Matches.ContainsKey("old"), "The background step must not mutate the visible saved state.");
        var restored = JsonSerializer.Deserialize<AnnualCs>(JsonSerializer.Serialize(first))!;
        do { restored = await service.StepAsync("Farmer", "TAG", "eun1", "key", restored, now.AddHours(1), default); } while (restored.Scanning);
        account.AnnualCs = restored;
        var duplicate = new Account { GameName = "farmer", TagLine = "tag", Region = "EUN1", AnnualCs = restored.Copy() };
        var (cs, games, rate, partial) = AnnualCs.Totals([account, duplicate], now.AddHours(1));
        Require(cs == 29 * 110 && games == 29 && Math.Abs(rate - 5.5) < .001 && partial,
            "CS must combine lane and jungle, deduplicate games/accounts and expose missing history.");
        Require(urls.Where(u => u.Contains("/ids?")).All(u => u.Contains("endTime=" + now.ToUnixTimeSeconds())),
            "Resuming the scan must retain the original endTime as new games arrive.");
        Require(restored.Unavailable.SetEquals(["EUN1_30"]), "Unavailable history must remain visible as partial coverage.");
        string historyPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var store = new AnnualCsStore(historyPath);
            store.Save(new() { [AnnualCsStore.Identity(account)] = restored });
            Require(store.Load()[AnnualCsStore.Identity(account)].Matches.Count == 29,
                "History checkpoints must survive restart in their separate storage file.");
            Require(!JsonSerializer.Serialize(account).Contains("EUN1_"), "Account startup JSON must not include the large match history.");
        }
        finally { File.Delete(historyPath); }
        account.UpdateRiotId("Different", "TAG", "eun1");
        Require(account.AnnualCs.Matches.Count == 0, "Changing Riot ID must clear the previous account's CS.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await service.StepAsync("Farmer", "TAG", "eun1", "key", restored, now, cancelled.Token); throw new Exception("Cancelled import continued."); }
        catch (OperationCanceledException) { }
        using var positions = JsonDocument.Parse("""{"103":{"recommendedPositions":["MIDDLE","TOP","MIDDLE"]},"1":{"recommendedPositions":["UTILITY"]},"bad":{"recommendedPositions":["TANK"]},"2":{"recommendedPositions":["FIGHTER"]}}""");
        var parsed = RemoteChampionPositions.Parse(positions.RootElement);
        Require(parsed[103].SequenceEqual(["mid", "top"]) && parsed[1].SequenceEqual(["support"]) && !parsed.ContainsKey(2),
            "Champion positions must use client lane recommendations, with multiple positions and safe handling of unknown data.");
        Console.WriteLine("Annual CS import, checkpoint, coverage, identity and champion-position checks passed.");

        Task<JsonElement?> Get(string url, string key, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); urls.Add(url);
            if (url.Contains("/accounts/")) return Json(new { gameName = "Farmer", tagLine = "TAG", puuid = "self" });
            if (url.Contains("/ids?")) return Json(url.Contains("start=0&") ? Enumerable.Range(1, 30).Reverse().Select(i => "EUN1_" + i).Concat(["EUN1_1"]).ToArray() : []);
            if (url.EndsWith("/EUN1_30")) return Task.FromResult<JsonElement?>(null);
            string id = url.Split('/').Last();
            return Json(new { metadata = new { matchId = id }, info = new
            {
                gameCreation = now.AddDays(-10).ToUnixTimeMilliseconds(), gameDuration = 1200,
                participants = new[] { new { puuid = "self", totalMinionsKilled = 100, neutralMinionsKilled = 10 } }
            } });
        }
    }
    private static Task<JsonElement?> Json(object value) => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(value));
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
