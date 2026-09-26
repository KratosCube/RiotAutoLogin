using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RiotAutoLogin.Models
{
    public enum RankedQueue { SoloDuo, RankedFives, Flex }

    public sealed record QueueOption(RankedQueue Key, string Name);

    public static class RankedQueues
    {
        public static IReadOnlyList<QueueOption> Options { get; } = new[]
        {
            new QueueOption(RankedQueue.SoloDuo, "Solo / Duo"),
            new QueueOption(RankedQueue.RankedFives, "Ranked 5s"),
            new QueueOption(RankedQueue.Flex, "Flex")
        };

        public static string ApiKey(RankedQueue queue) => queue switch
        {
            RankedQueue.RankedFives => "RANKED_TEAM_5x5",
            RankedQueue.Flex => "RANKED_FLEX_SR",
            _ => "RANKED_SOLO_5x5"
        };

        public static RankedQueue? FromType(string? type) => type?.ToUpperInvariant() switch
        {
            "RANKED_SOLO_5X5" => RankedQueue.SoloDuo,
            "RANKED_TEAM_5X5" => RankedQueue.RankedFives,
            "RANKED_FLEX_SR" => RankedQueue.Flex,
            _ => null
        };

        // 710 is the revived 2026 Ranked 5s queue, separate from Flex (440)
        // and Clash (700). Prefer the client's queue type when it is available.
        public static RankedQueue? FromId(int id) => id switch
        {
            420 => RankedQueue.SoloDuo,
            710 => RankedQueue.RankedFives,
            440 => RankedQueue.Flex,
            _ => null
        };

        public static Dictionary<RankedQueue, RankData> ParseRanks(JsonElement entries)
        {
            var ranks = new Dictionary<RankedQueue, RankData>();
            foreach (JsonElement entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("queueType", out var type) || FromType(type.GetString()) is not { } queue)
                    continue;

                ranks[queue] = new RankData
                {
                    Tier = entry.TryGetProperty("tier", out var tier) ? tier.GetString() ?? "" : "",
                    Rank = entry.TryGetProperty("rank", out var rank) ? rank.GetString() ?? "" : "",
                    LeaguePoints = ReadNumber(entry, "leaguePoints"),
                    Wins = ReadNumber(entry, "wins"),
                    Losses = ReadNumber(entry, "losses")
                };
            }

            // A successful League-V4 response omits queues without a rank.
            foreach (var option in Options)
                ranks.TryAdd(option.Key, new RankData());
            return ranks;
        }

        private static int ReadNumber(JsonElement entry, string name) =>
            entry.TryGetProperty(name, out var value) && value.TryGetInt32(out int number) ? Math.Max(0, number) : 0;
    }
}
