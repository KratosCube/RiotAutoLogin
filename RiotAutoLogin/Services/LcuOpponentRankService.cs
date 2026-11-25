using RiotAutoLogin.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services;

internal sealed class LcuOpponentRankService(Func<string, CancellationToken, Task<string[]>> get)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<(string Region, string Puuid), CachedRanks> _rankCache = new();
    private sealed record CachedRanks(DateTime UpdatedUtc, Dictionary<RankedQueue, RankData> Ranks);

    // A failed account must not abort the remaining accounts in the UI's refresh batch.
    // Cancellation still propagates so changing queues or closing the app stops the batch.
    internal async Task<OpponentRankSummary?> TryReadAsync(string gameName, string tagLine, string region,
        RankedQueue queue, CancellationToken token)
    {
        try { return await ReadAsync(gameName, tagLine, region, queue, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"Opponent rank lookup failed for one account: {ex.Message}");
            return null;
        }
    }

    internal async Task<OpponentRankSummary?> ReadAsync(string gameName, string tagLine, string region,
        RankedQueue queue, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            async Task<string[]> Request(string path)
            {
                token.ThrowIfCancellationRequested();
                var response = await get(path, token).ConfigureAwait(false);
                if (response.Length < 2 || response[0] is not ("200" or "404"))
                    throw new HttpRequestException("League Client could not provide opponent statistics.");
                return response;
            }

            string? puuid = await LcuRankLookup.ResolvePuuidAsync(gameName, tagLine, region, Request).ConfigureAwait(false);
            if (puuid == null) return null;
            string platform = RemotePlayerProfileLinks.RegionSlug(region);
            var history = await LcuRankLookup.ReadObjectAsync(
                $"lol-match-history/v1/products/lol/{Uri.EscapeDataString(puuid)}/matches?begIndex=0&endIndex=100", Request).ConfigureAwait(false);
            if (history == null || !SamePlatform(history.Value, platform) ||
                !history.Value.TryGetProperty("games", out var games))
                return null;
            if (games.ValueKind == JsonValueKind.Object)
            {
                if (!games.TryGetProperty("games", out games)) return null;
            }
            if (games.ValueKind != JsonValueKind.Array) return null;

            var recent = games.EnumerateArray()
                .OrderByDescending(game => Number(game, "gameCreation"))
                .ThenByDescending(game => Number(game, "gameId"))
                .DistinctBy(game => Number(game, "gameId"))
                .Take(100)
                .Where(game => CompletedInQueue(game, queue))
                .ToArray();
            var result = new OpponentRankSummary();
            double totalPosition = 0;
            int reportedOpponents = 0;
            foreach (var game in recent)
            {
                if (result.Games == OpponentRankSummary.SampleLimit) break;
                token.ThrowIfCancellationRequested();
                long gameId = Number(game, "gameId");
                var opponents = Opponents(game, puuid, platform);
                if (opponents == null)
                {
                    // History summaries can contain only the selected player.
                    var detail = await LcuRankLookup.ReadObjectAsync($"lol-match-history/v1/games/{gameId}", Request).ConfigureAwait(false);
                    if (detail == null || Number(detail.Value, "gameId") != gameId || !CompletedInQueue(detail.Value, queue))
                        continue;
                    opponents = Opponents(detail.Value, puuid, platform);
                }
                if (opponents == null) continue;
                result.Games++;
                result.TotalOpponents += 5;
                foreach (string opponent in opponents)
                {
                    var key = (platform, opponent);
                    if (!_rankCache.TryGetValue(key, out var cached) ||
                        DateTime.UtcNow - cached.UpdatedUtc >= TimeSpan.FromMinutes(15))
                    {
                        var ranks = await LcuRankLookup.ReadObjectAsync(
                            $"lol-ranked/v1/ranked-stats/{Uri.EscapeDataString(opponent)}", Request).ConfigureAwait(false);
                        if (ranks == null) continue;
                        cached = new(DateTime.UtcNow, RankedQueues.ParseClientRanks(ranks.Value));
                        if (_rankCache.Count >= 1000) _rankCache.Clear();
                        _rankCache[key] = cached;
                    }
                    if (!cached.Ranks.TryGetValue(queue, out var rank)) continue;
                    reportedOpponents++;
                    if (OpponentRankSummary.Position(rank) is not { } position) continue;
                    totalPosition += position;
                    result.RankedOpponents++;
                }
            }

            // A reconnect or missing details must not replace a saved sample with an apparent empty history.
            if (recent.Length > 0 && result.Games == 0) return null;
            if (result.Games > 0 && reportedOpponents == 0) return null;
            var locale = await LcuRankLookup.ReadObjectAsync("riotclient/region-locale", Request).ConfigureAwait(false);
            if (locale == null || RemotePlayerProfileLinks.RegionSlug(Text(locale.Value, "region")) != platform)
                return null;
            result.MeanRankPosition = result.RankedOpponents > 0 ? totalPosition / result.RankedOpponents : null;
            result.UpdatedUtc = DateTime.UtcNow;
            return result;
        }
        finally { _gate.Release(); }
    }

    private static bool CompletedInQueue(JsonElement game, RankedQueue queue) =>
        Number(game, "gameId") > 0 && Number(game, "gameDuration") > 0 &&
        Number(game, "queueId") <= int.MaxValue && RankedQueues.FromId((int)Number(game, "queueId")) == queue &&
        !Elements(game, "participants").Any(player =>
            player.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Object &&
            stats.TryGetProperty("gameEndedInEarlySurrender", out var remake) && remake.ValueKind == JsonValueKind.True);

    private static List<string>? Opponents(JsonElement game, string puuid, string platform)
    {
        if (!SamePlatform(game, platform)) return null;
        var identities = Elements(game, "participantIdentities").ToArray();
        var players = Elements(game, "participants").ToArray();
        long ownId = identities.Where(identity => identity.TryGetProperty("player", out var player) &&
                Text(player, "puuid") == puuid)
            .Select(identity => Number(identity, "participantId")).FirstOrDefault();
        long team = players.Where(player => Number(player, "participantId") == ownId)
            .Select(player => Number(player, "teamId")).FirstOrDefault();
        if (ownId <= 0 || team is not (100 or 200) || players.Length != 10 ||
            players.Any(player => Number(player, "participantId") <= 0) ||
            players.Select(player => Number(player, "participantId")).Distinct().Count() != 10 ||
            players.Count(player => Number(player, "teamId") == team) != 5 ||
            players.Count(player => Number(player, "teamId") == 300 - team) != 5)
            return null;

        var opponents = new List<string>();
        foreach (var participant in players.Where(player => Number(player, "teamId") == 300 - team))
        {
            long id = Number(participant, "participantId");
            var identity = identities.FirstOrDefault(item => Number(item, "participantId") == id);
            if (identity.ValueKind != JsonValueKind.Object || !identity.TryGetProperty("player", out var player))
                continue;
            string opponent = Text(player, "puuid");
            string currentPlatform = Text(player, "currentPlatformId");
            if (opponent.Length > 0 && opponent != puuid &&
                (currentPlatform.Length == 0 || RemotePlayerProfileLinks.RegionSlug(currentPlatform) == platform))
                opponents.Add(opponent);
        }
        return opponents.Distinct(StringComparer.Ordinal).ToList();
    }

    private static bool SamePlatform(JsonElement value, string platform) =>
        Text(value, "platformId") is "" || RemotePlayerProfileLinks.RegionSlug(Text(value, "platformId")) == platform;

    private static IEnumerable<JsonElement> Elements(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var array) &&
        array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object) : [];

    private static string Text(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var field) &&
        field.ValueKind == JsonValueKind.String ? field.GetString() ?? "" : "";

    private static long Number(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var field) &&
        field.ValueKind == JsonValueKind.Number && field.TryGetInt64(out long number) ? number : 0;
}
