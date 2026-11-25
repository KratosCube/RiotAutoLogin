using RiotAutoLogin.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services;

internal sealed class PublicOpponentRankService(Func<string, string, CancellationToken, Task<JsonElement?>> get)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, JsonElement> _matches = new();
    private readonly Dictionary<(string Platform, string Puuid), (DateTime Updated, Dictionary<RankedQueue, RankData> Ranks)> _ranks = new();
    private string _keyId = "";

    internal static (string Platform, string Cluster)? Route(string region) =>
        RemotePlayerProfileLinks.RegionSlug(region) switch
        {
            "euw" => ("euw1", "europe"), "eune" => ("eun1", "europe"),
            "tr" => ("tr1", "europe"), "ru" => ("ru", "europe"),
            "na" => ("na1", "americas"), "br" => ("br1", "americas"),
            "lan" => ("la1", "americas"), "las" => ("la2", "americas"),
            "kr" => ("kr", "asia"), "jp" => ("jp1", "asia"),
            "oce" => ("oc1", "sea"), "ph" => ("ph2", "sea"), "sg" => ("sg2", "sea"),
            "th" => ("th2", "sea"), "tw" => ("tw2", "sea"), "vn" => ("vn2", "sea"),
            _ => null
        };

    internal async Task<OpponentRankSummary?> ReadAsync(string name, string tag, string region,
        RankedQueue queue, string key, CancellationToken token, OpponentRankSummary? previous = null, bool force = false, long expectedGameId = 0)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(tag) ||
            string.IsNullOrWhiteSpace(key) || Route(region) is not { } route) return null;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            // Public API PUUIDs are key-specific and must never be shared with LCU lookups.
            string keyId = RiotApiClient.KeyId(key);
            if (_keyId != keyId) { _matches.Clear(); _ranks.Clear(); _keyId = keyId; }
            string cluster = $"https://{route.Cluster}.api.riotgames.com";
            var identity = await get($"{cluster}/riot/account/v1/accounts/by-riot-id/{Uri.EscapeDataString(name.Trim())}/{Uri.EscapeDataString(tag.Trim())}", key, token).ConfigureAwait(false);
            if (identity is not { } account ||
                !string.Equals(Text(account, "gameName"), name.Trim(), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Text(account, "tagLine"), tag.Trim(), StringComparison.OrdinalIgnoreCase) ||
                Text(account, "puuid") is not { Length: > 0 } puuid) return null;
            int queueId = queue switch { RankedQueue.RankedFives => 710, RankedQueue.Flex => 440, _ => 420 };
            string historyUrl = $"{cluster}/lol/match/v5/matches/by-puuid/{Uri.EscapeDataString(puuid)}/ids";
            string expected = OpponentRankSummary.MatchKey(region, expectedGameId);
            if (!force && previous?.LatestMatchId is { Length: > 0 } && (expected.Length == 0 || previous.LatestMatchId == expected))
            {
                var probe = await get($"{historyUrl}?start=0&count=1&queue={queueId}", key, token).ConfigureAwait(false);
                if (probe is { ValueKind: JsonValueKind.Array } latest && latest.GetArrayLength() > 0 &&
                    latest[0].ValueKind == JsonValueKind.String && MatchKey(latest[0].GetString()!, region, route.Platform) == previous.LatestMatchId)
                    return previous.CheckedNow();
            }
            var history = await get($"{historyUrl}?start=0&count=100&queue={queueId}", key, token).ConfigureAwait(false);
            if (history is not { ValueKind: JsonValueKind.Array } ids) return null;
            var recent = ids.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!).Distinct().Take(100).ToArray();
            if (expected.Length > 0 && !recent.Any(id => MatchKey(id, region, route.Platform) == expected)) return null;
            string newest = recent.FirstOrDefault(id => MatchKey(id, region, route.Platform).Length > 0) ?? "";
            bool latestAvailable = false, complete = true;
            var result = new OpponentRankSummary { Source = "Riot API" };
            double total = 0;
            int reported = 0;
            foreach (string matchId in recent)
            {
                if (result.Games == OpponentRankSummary.SampleLimit) break;
                token.ThrowIfCancellationRequested();
                // Match IDs encode the platform. Never read a transferred account's old-region games as this region.
                if (!matchId.StartsWith(route.Platform + "_", StringComparison.OrdinalIgnoreCase)) continue;
                string cacheId = route.Cluster + "/" + matchId;
                if (!_matches.TryGetValue(cacheId, out var match))
                {
                    var response = await get($"{cluster}/lol/match/v5/matches/{Uri.EscapeDataString(matchId)}", key, token).ConfigureAwait(false);
                    if (response is not { } detail) { complete = false; continue; }
                    match = detail;
                    if (Finished(match, matchId))
                    {
                        if (_matches.Count >= 500) _matches.Clear();
                        _matches[cacheId] = match;
                    }
                }
                if (matchId == newest) latestAvailable = Finished(match, matchId) && Number(match.GetProperty("info"), "queueId") == queueId;
                if (!Finished(match, matchId)) { complete = false; continue; }
                if (!Completed(match, matchId, queueId)) continue;
                var players = match.GetProperty("info").GetProperty("participants").EnumerateArray().ToArray();
                var self = players.Where(p => Text(p, "puuid") == puuid).ToArray();
                if (self.Length != 1) { complete = false; continue; }
                int team = Number(self[0], "teamId");
                if (team is not (100 or 200) || players.Length != 10 ||
                    players.Select(p => Text(p, "puuid")).Distinct().Count() != 10 ||
                    players.Any(p => Text(p, "puuid").Length == 0) ||
                    players.Count(p => Number(p, "teamId") == 100) != 5 ||
                    players.Count(p => Number(p, "teamId") == 200) != 5) { complete = false; continue; }
                result.Games++;
                result.TotalOpponents += 5;
                foreach (var enemy in players.Where(p => Number(p, "teamId") != team))
                {
                    string enemyId = Text(enemy, "puuid");
                    var cacheKey = (route.Platform, enemyId);
                    if (!_ranks.TryGetValue(cacheKey, out var cached) || DateTime.UtcNow - cached.Updated >= TimeSpan.FromMinutes(15))
                    {
                        var entries = await get($"https://{route.Platform}.api.riotgames.com/lol/league/v4/entries/by-puuid/{Uri.EscapeDataString(enemyId)}", key, token).ConfigureAwait(false);
                        if (entries is not { ValueKind: JsonValueKind.Array } array) { complete = false; continue; }
                        cached = (DateTime.UtcNow, RankedQueues.ParseRanks(array));
                        if (_ranks.Count >= 1000) _ranks.Clear();
                        _ranks[cacheKey] = cached;
                    }
                    if (!cached.Ranks.TryGetValue(queue, out var rank)) continue;
                    reported++;
                    if (OpponentRankSummary.Position(rank) is not { } position) continue;
                    total += position;
                    result.RankedOpponents++;
                }
            }
            if (recent.Length > 0 && result.Games == 0 || result.Games > 0 && reported == 0) return null;
            result.MeanRankPosition = result.RankedOpponents > 0 ? total / result.RankedOpponents : null;
            result.LatestMatchId = complete && latestAvailable ? MatchKey(newest, region, route.Platform) : "";
            result.CheckedUtc = result.UpdatedUtc = DateTime.UtcNow;
            return result;
        }
        finally { _gate.Release(); }
    }

    private static string MatchKey(string id, string region, string platform) =>
        id.StartsWith(platform + "_", StringComparison.OrdinalIgnoreCase) && long.TryParse(id[(platform.Length + 1)..], out long gameId)
        ? OpponentRankSummary.MatchKey(region, gameId) : "";

    private static bool Finished(JsonElement match, string id) =>
        match.ValueKind == JsonValueKind.Object && match.TryGetProperty("metadata", out var metadata) &&
        Text(metadata, "matchId") == id && match.TryGetProperty("info", out var info) &&
        Number(info, "gameDuration") > 0 && info.TryGetProperty("participants", out var players) &&
        players.ValueKind == JsonValueKind.Array;

    private static bool Completed(JsonElement match, string id, int queue) =>
        Finished(match, id) && Number(match.GetProperty("info"), "queueId") == queue &&
        match.GetProperty("info").TryGetProperty("participants", out var players) &&
        !players.EnumerateArray().Any(p => p.ValueKind == JsonValueKind.Object &&
            p.TryGetProperty("gameEndedInEarlySurrender", out var remake) && remake.ValueKind == JsonValueKind.True);

    private static string Text(JsonElement value, string field) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(field, out var item) &&
        item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
    private static int Number(JsonElement value, string field) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(field, out var item) &&
        item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out int number) ? number : 0;
}
