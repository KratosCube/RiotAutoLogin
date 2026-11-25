using System.Text.Json;
using RiotAutoLogin.Models;
using RiotAutoLogin.Services;

internal static class MatchRefreshChecks
{
    internal static async Task Run()
    {
        var calls = new List<string>();
        long latest = 1001;
        bool missingRank = false;
        Task<JsonElement?> Get(string url, string key, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); calls.Add(url);
            if (url.Contains("/accounts/")) return Json(new { gameName = "Player", tagLine = "TAG", puuid = "public-self" });
            if (url.Contains("/ids?")) return Json(new[] { "EUW1_" + latest });
            if (url.Contains("/league/")) return missingRank ? Task.FromResult<JsonElement?>(null) : Json(new[] {
                new { queueType = "RANKED_SOLO_5x5", tier = "GOLD", rank = "II", leaguePoints = 0 } });
            if (url.Contains("/matches/")) return Json(new { metadata = new { matchId = url.Split('/').Last() }, info = new {
                queueId = 420, gameDuration = 1800, participants = Enumerable.Range(0, 10).Select(i => new {
                    puuid = i == 0 ? "public-self" : "p-" + i, teamId = i < 5 ? 100 : 200, gameEndedInEarlySurrender = false }).ToArray() } });
            throw new Exception("Unexpected match refresh URL: " + url);
        }
        var first = await new PublicOpponentRankService(Get).ReadAsync("Player", "TAG", "euw1", RankedQueue.SoloDuo, "key", default);
        Require(first is { Games: 1, RankedOpponents: 5, LatestMatchId: "euw:1001" }, "A complete sample must save its latest match checkpoint.");
        first = JsonSerializer.Deserialize<OpponentRankSummary>(JsonSerializer.Serialize(first))!;
        calls.Clear();
        var reused = await new PublicOpponentRankService(Get).ReadAsync("Player", "TAG", "euw1", RankedQueue.SoloDuo, "key", default, first);
        Require(calls.Count == 2 && calls.Any(c => c.Contains("count=1&")) && reused?.UpdatedUtc == first.UpdatedUtc && reused.CheckedUtc >= first.UpdatedUtc,
            "Unchanged history must reuse the persisted sample after restart, with only identity and latest-ID requests.");
        calls.Clear();
        latest = 1002;
        var changed = await new PublicOpponentRankService(Get).ReadAsync("Player", "TAG", "euw1", RankedQueue.SoloDuo, "key", default, first, expectedGameId: 1002);
        Require(changed?.LatestMatchId == "euw:1002" && calls.Any(c => c.EndsWith("/EUW1_1002")), "A new game must calculate a fresh sample.");
        Require(await new PublicOpponentRankService(Get).ReadAsync("Player", "TAG", "euw1", RankedQueue.SoloDuo, "key", default, first, expectedGameId: 1003) == null,
            "Delayed post-game history must not be mistaken for the completed game's sample.");
        calls.Clear();
        await new PublicOpponentRankService(Get).ReadAsync("Player", "TAG", "euw1", RankedQueue.SoloDuo, "key", default, changed, force: true);
        Require(calls.Any(c => c.Contains("/league/")) && !calls.Any(c => c.Contains("count=1&")), "Manual refresh must recalculate unchanged history.");
        missingRank = true;
        Require(await new PublicOpponentRankService(Get).ReadAsync("Player", "TAG", "euw1", RankedQueue.SoloDuo, "key", default) == null,
            "A failed rank service must preserve the previous sample.");

        var player = new StatsPlayer("Player", "TAG", "EUW", "local-puuid");
        var other = new StatsPlayer("Different", "TAG", "EUNE", "other-local-puuid");
        var tracker = new PostGameRefreshTracker();
        tracker.Observe(new("ChampSelect", RankedQueue.SoloDuo, 0, false));
        tracker.Observe(new("GameStart", null, 1002, false), player);
        tracker.Observe(new("InProgress", null, 1002, false));
        var finished = tracker.Observe(new("WaitingForStats", null, 1002, false), other);
        Require(finished is { GameId: 1002, Queue: RankedQueue.SoloDuo } && finished.Player == player,
            "Post-game refresh must use the player and queue captured during the game, despite a subsequent client/account switch.");
        Require(tracker.Observe(new("EndOfGame", null, 1002, false)) == null, "Post-game phases must trigger only once.");
        tracker.Observe(new("InProgress", RankedQueue.Flex, 1003, false), other);
        Require(tracker.Observe(new("Lobby", null, 0, false))?.Player == other, "The next game must update its own account.");
        tracker.Observe(new("InProgress", RankedQueue.SoloDuo, 1004, false), player);
        tracker.Disconnect();
        Require(tracker.Observe(new("None", null, 0, false)) == null, "A disconnected client is not proof a game finished.");
        tracker.Observe(new("InProgress", RankedQueue.SoloDuo, 1005, true), player);
        Require(tracker.Observe(new("EndOfGame", null, 1005, true)) == null, "Spectating must never update the spectator's account as a played game.");
        Console.WriteLine("Latest-match checkpoint reuse, manual refresh, delayed history and targeted post-game identity checks passed.");
    }
    private static Task<JsonElement?> Json(object value) => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(value));
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
