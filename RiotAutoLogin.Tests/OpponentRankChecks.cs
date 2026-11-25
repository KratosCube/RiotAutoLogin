using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using RiotAutoLogin.Models;
using RiotAutoLogin.Services;

internal static class OpponentRankChecks
{
    public static async Task Run()
    {
        var history = new JsonArray();
        foreach (int id in Enumerable.Range(1, 12)) history.Add(Game(id, summary: true));
        history.Add(Game(12, summary: true));
        history.Add(Game(99, queue: 440));
        var remake = Game(13);
        remake["participants"]![6]!["stats"]!["gameEndedInEarlySurrender"] = true;
        history.Add(remake);
        var unfinished = Game(14);
        unfinished["gameDuration"] = 0;
        history.Add(unfinished);
        var calls = new List<string>();
        var service = new LcuOpponentRankService(Read);
        var sample = await service.ReadAsync("Two Words", "TAG", "euw1", RankedQueue.SoloDuo, default);
        Require(sample is { Games: 10, TotalOpponents: 50, RankedOpponents: 40, MeanRankPosition: 1325 },
            "The latest ten complete Solo/Duo games must average enemy ranks and omit unranked players.");
        Require(sample!.Label == "GOLD III", "The average must be expressed on the visible rank ladder.");
        Require(calls.Count(path => path.StartsWith("lol-ranked/")) == 5, "Repeated opponents must reuse their cached rank.");
        Require(calls.Where(path => path.StartsWith("lol-match-history/v1/games/")).SequenceEqual(
            Enumerable.Range(3, 10).Reverse().Select(id => $"lol-match-history/v1/games/{id}")),
            "Summaries must fetch the correct ten details in newest-first order.");

        calls.Clear();
        await service.ReadAsync("Two Words", "TAG", "euw1", RankedQueue.SoloDuo, default);
        Require(!calls.Any(path => path.StartsWith("lol-ranked/")), "A repeat refresh must reuse the rank cache.");
        calls.Clear();
        var cachedSample = JsonSerializer.Deserialize<OpponentRankSummary>(JsonSerializer.Serialize(sample))!;
        var freshClientService = new LcuOpponentRankService(Read);
        var checkedSample = await freshClientService.ReadAsync("Two Words", "TAG", "euw1", RankedQueue.SoloDuo, default, cachedSample);
        Require(checkedSample?.UpdatedUtc == sample.UpdatedUtc && checkedSample.CheckedUtc >= sample.UpdatedUtc &&
            !calls.Any(path => path.StartsWith("lol-ranked/") || path.StartsWith("lol-match-history/v1/games/")),
            "Unchanged client history must reuse a persisted sample without enemy rank/detail reads, even after restart.");
        Require(await freshClientService.ReadAsync("Two Words", "TAG", "euw1", RankedQueue.SoloDuo, default, cachedSample, expectedGameId: 999999) == null,
            "A post-game refresh must wait until that specific match appears.");
        var fullRemake = new LcuOpponentRankService((path, token) =>
        {
            if (path != "lol-match-history/v1/games/12") return Read(path, token);
            var game = Game(12);
            game["participants"]![6]!["stats"]!["gameEndedInEarlySurrender"] = true;
            return Reply(game.ToJsonString());
        });
        Require((await fullRemake.ReadAsync("Two Words", "TAG", "euw1", RankedQueue.SoloDuo, default))?.Games == 10,
            "A remake discovered in match details must be replaced by the next eligible game.");
        calls.Clear();
        Require(await service.ReadAsync("Two Words", "TAG", "eun1", RankedQueue.SoloDuo, default) == null &&
                calls.SequenceEqual(["riotclient/region-locale"]), "A different client region must stop before identity or history requests.");

        var wrongTag = new LcuOpponentRankService((path, token) => path.StartsWith("lol-summoner/")
            ? Reply("""{"alias":{"gameName":"Two Words","tagLine":"OTHER"},"puuid":"self"}""") : Read(path, token));
        Require(await wrongTag.ReadAsync("Two Words", "TAG", "euw1", RankedQueue.SoloDuo, default) == null,
            "A different saved Riot tag must not inherit a player's history.");

        history.Clear();
        history.Add(Game(22, queue: 710));
        var weekend = await service.ReadAsync("Two Words", "TAG", "euw1", RankedQueue.RankedFives, default);
        Require(weekend is { Games: 1, RankedOpponents: 5, MeanRankPosition: 2100 } && weekend.Label == "EMERALD III",
            "A Ranked 5s sample must use each opponent's weekend rank.");

        history.Clear();
        history.Add(Game(30, summary: true));
        var missingDetails = new LcuOpponentRankService((path, token) => path.StartsWith("lol-match-history/v1/games/")
            ? Reply("{}", "404") : Read(path, token));
        Require(await missingDetails.ReadAsync("Two Words", "TAG", "euw1", RankedQueue.SoloDuo, default) == null,
            "Unavailable details must not become an empty successful sample.");

        var incompleteRanks = new LcuOpponentRankService((path, token) => path == "lol-ranked/v1/ranked-stats/player-4"
            ? Reply("{}", "404") : Read(path, token));
        var partial = await incompleteRanks.ReadAsync("Two Words", "TAG", "euw1", RankedQueue.SoloDuo, default);
        Require(partial is { Games: 1, RankedOpponents: 3, TotalOpponents: 5, MeanRankPosition: 1400 },
            "Unavailable ranks must reduce coverage and contribute no fabricated zero rank.");
        var noRanks = new LcuOpponentRankService((path, token) => path.StartsWith("lol-ranked/")
            ? Reply("{}", "404") : Read(path, token));
        Require(await noRanks.ReadAsync("Two Words", "TAG", "euw1", RankedQueue.SoloDuo, default) == null,
            "A missing rank service must preserve the saved summary.");

        var unavailable = new LcuOpponentRankService((path, token) => path.StartsWith("lol-ranked/")
            ? Reply("{}", "429") : Read(path, token));
        try
        {
            await unavailable.ReadAsync("Two Words", "TAG", "euw1", RankedQueue.SoloDuo, default);
            throw new Exception("A rate-limited refresh was treated as a complete sample.");
        }
        catch (HttpRequestException) { }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await service.TryReadAsync("Two Words", "TAG", "euw1", RankedQueue.SoloDuo, cancelled.Token);
            throw new Exception("A cancelled queue lookup kept running.");
        }
        catch (OperationCanceledException) { }

        history.Clear();
        var wrongPlayer = Game(40);
        wrongPlayer["participantIdentities"]![6]!["player"]!["puuid"] = "different-player";
        history.Add(wrongPlayer);
        var mismatchedDetails = new LcuOpponentRankService((path, token) => path.StartsWith("lol-match-history/v1/games/")
            ? Reply(wrongPlayer.ToJsonString()) : Read(path, token));
        Require(await mismatchedDetails.ReadAsync("Two Words", "TAG", "euw1", RankedQueue.SoloDuo, default) == null,
            "The selected account must actually participate in every sampled game.");

        history.Clear();
        var empty = await service.ReadAsync("Two Words", "TAG", "euw1", RankedQueue.SoloDuo, default);
        Require(empty is { Games: 0, MeanRankPosition: null } && empty.Label == "—", "Empty history must show unknown.");
        Require(OpponentRankSummary.Position(new RankData { Tier = "CHALLENGER", LeaguePoints = 900 }) == 3700 &&
                new OpponentRankSummary { MeanRankPosition = 3700 }.Label == "MASTER+",
            "Apex LP must use the shared Master+ ladder without inventing Grandmaster thresholds.");
        Require(OpponentRankSummary.Position(new RankData { Tier = "UNRANKED" }) == null &&
                OpponentRankSummary.Position(new RankData { Tier = "GOLD", Rank = "INVALID" }) == null,
            "Unknown tiers or divisions must be excluded.");

        var account = new Account { GameName = "Two Words", TagLine = "TAG", Region = "euw1" };
        account.ApplyOpponentRanks(RankedQueue.SoloDuo, sample);
        account.ApplyOpponentRanks(RankedQueue.RankedFives, weekend!);
        account = JsonSerializer.Deserialize<Account>(JsonSerializer.Serialize(account))!;
        account.MigrateLegacyRank();
        Require(account.OpponentRankLabel == "GOLD III" && account.DisplayOpponents!.RankedOpponents == 40,
            "Saved samples must survive restart.");
        var changed = new List<string?>();
        account.PropertyChanged += (_, change) => changed.Add(change.PropertyName);
        account.SelectedQueue = RankedQueue.RankedFives;
        account.RefreshRankDisplay();
        Require(account.OpponentRankLabel == "EMERALD III" && changed.Contains(nameof(Account.OpponentRankLabel)),
            "Switching queues must update the displayed opponent average.");
        account.UpdateRiotId("two words", "tag", "EUW1");
        Require(account.QueueOpponentRanks.Count == 2, "A cosmetic identity edit must preserve the sample.");
        account.UpdateRiotId("Two Words", "NEW", "euw1");
        Require(account.OpponentRankLabel == "—", "A changed account must discard the previous player's sample.");
        var legacy = JsonSerializer.Deserialize<Account>("""{"GameName":"Old","QueueOpponentRanks":null}""")!;
        legacy.MigrateLegacyRank();
        Require(legacy.OpponentRankLabel == "—", "Older accounts must remain readable.");
        await CheckAccountFailureIsolation();
        Console.WriteLine("Opponent rank history, coverage, cache, queue, identity and saved-account checks passed.");

        Task<string[]> Read(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            calls.Add(path);
            if (path == "riotclient/region-locale") return Reply("""{"region":"EUW"}""");
            if (path == "lol-summoner/v1/alias/lookup?gameName=Two%20Words&tagLine=TAG")
                return Reply("""{"alias":{"gameName":"Two Words","tagLine":"TAG"},"puuid":"self"}""");
            if (path == "lol-match-history/v1/products/lol/self/matches?begIndex=0&endIndex=100")
                return Reply(new JsonObject { ["platformId"] = "EUW1", ["games"] = new JsonObject { ["games"] = history.DeepClone() } }.ToJsonString());
            if (path.StartsWith("lol-match-history/v1/games/"))
                return Reply(Game(long.Parse(path.Split('/').Last())).ToJsonString());
            if (path.StartsWith("lol-ranked/v1/ranked-stats/player-"))
            {
                int id = int.Parse(path.Split('-').Last());
                Require(id is >= 1 and <= 5, "An ally's rank was requested.");
                string[] tiers = ["GOLD", "GOLD", "PLATINUM", "SILVER", "NONE"];
                string[] divisions = ["IV", "II", "IV", "I", "NA"];
                return Reply(JsonSerializer.Serialize(new
                {
                    queues = new[]
                    {
                        new { queueType = "RANKED_SOLO_5x5", tier = tiers[id - 1], division = divisions[id - 1], leaguePoints = 0 },
                        new { queueType = "RANKED_PREMADE_5x5", tier = "EMERALD", division = "III", leaguePoints = 0 }
                    }
                }));
            }
            throw new Exception($"Unexpected opponent stats request {path}");
        }
    }

    private static async Task CheckAccountFailureIsolation()
    {
        foreach (var (clientRegion, platform, otherPlatform) in new[]
                 { ("EUW", "EUW1", "EUN1"), ("EUNE", "EUN1", "EUW1") })
        {
            var accounts = new[]
            {
                new Account { GameName = "First", TagLine = "TAG", Region = platform },
                new Account { GameName = "OtherRegion", TagLine = "TAG", Region = otherPlatform },
                new Account { GameName = "Unavailable", TagLine = "TAG", Region = platform },
                new Account { GameName = "Last", TagLine = "TAG", Region = platform }
            };
            var saved = new OpponentRankSummary { MeanRankPosition = 900, Games = 3 };
            accounts[1].ApplyOpponentRanks(RankedQueue.SoloDuo, saved);
            accounts[2].ApplyOpponentRanks(RankedQueue.SoloDuo, saved);
            var visited = new List<string>();
            var service = new LcuOpponentRankService(ReadBatch);
            foreach (var account in accounts)
            {
                var summary = await service.TryReadAsync(account.GameName, account.TagLine, account.Region,
                    RankedQueue.SoloDuo, default);
                if (summary != null) account.ApplyOpponentRanks(RankedQueue.SoloDuo, summary);
            }

            Require(visited.SequenceEqual(["First", "Unavailable", "Last"]),
                $"The {clientRegion} batch must skip another region and continue after a failed account.");
            Require(accounts[0].DisplayOpponents is { Games: 1, RankedOpponents: 5 } &&
                    accounts[3].DisplayOpponents is { Games: 1, RankedOpponents: 5 },
                $"Both available {clientRegion} accounts must receive opponent statistics.");
            Require(accounts[0].DisplayRank == null && accounts[3].DisplayRank == null,
                "An account's own rank must not be required to load its opponent sample.");
            Require(ReferenceEquals(saved, accounts[1].DisplayOpponents) &&
                    ReferenceEquals(saved, accounts[2].DisplayOpponents),
                "Skipped and failed accounts must retain their last successful sample.");

            Task<string[]> ReadBatch(string path, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (path == "riotclient/region-locale")
                    return Reply(JsonSerializer.Serialize(new { region = clientRegion }));
                foreach (var account in accounts)
                {
                    if (path == $"lol-summoner/v1/alias/lookup?gameName={account.GameName}&tagLine=TAG")
                    {
                        visited.Add(account.GameName);
                        return account.GameName == "Unavailable" ? Reply("{}", "500") :
                            Reply(JsonSerializer.Serialize(new
                            {
                                alias = new { gameName = account.GameName, tagLine = account.TagLine },
                                puuid = account.GameName
                            }));
                    }
                    if (path == $"lol-match-history/v1/products/lol/{account.GameName}/matches?begIndex=0&endIndex=100")
                    {
                        var game = Game(50, platform: platform);
                        game["participantIdentities"]![6]!["player"]!["puuid"] = account.GameName;
                        return Reply(new JsonObject
                        {
                            ["platformId"] = platform,
                            ["games"] = new JsonObject { ["games"] = new JsonArray(game) }
                        }.ToJsonString());
                    }
                }
                if (path.StartsWith("lol-ranked/v1/ranked-stats/player-"))
                    return Reply("""{"queues":[{"queueType":"RANKED_SOLO_5x5","tier":"GOLD","division":"III","leaguePoints":0}]}""");
                throw new Exception($"Unexpected account batch request {path}");
            }
        }
    }

    private static JsonObject Game(long id, int queue = 420, bool summary = false, string platform = "EUW1")
    {
        var identities = new JsonArray();
        var players = new JsonArray();
        foreach (int participant in Enumerable.Range(1, 10))
        {
            if (summary && participant != 7) continue;
            identities.Add(new JsonObject
            {
                ["participantId"] = participant,
                ["player"] = new JsonObject { ["puuid"] = participant == 7 ? "self" : $"player-{participant}", ["currentPlatformId"] = platform }
            });
            players.Add(new JsonObject
            {
                ["participantId"] = participant, ["teamId"] = participant <= 5 ? 100 : 200,
                ["stats"] = new JsonObject { ["gameEndedInEarlySurrender"] = false }
            });
        }
        return new JsonObject
        {
            ["gameId"] = id, ["gameCreation"] = id * 1000, ["gameDuration"] = 1200, ["queueId"] = queue,
            ["platformId"] = platform, ["participantIdentities"] = identities, ["participants"] = players
        };
    }

    private static Task<string[]> Reply(string json, string status = "200") => Task.FromResult(new[] { status, json });
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
