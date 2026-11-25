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

        public static int Id(RankedQueue queue) => queue switch { RankedQueue.RankedFives => 710, RankedQueue.Flex => 440, _ => 420 };

        public static string ApiKey(RankedQueue queue) => queue switch
        {
            RankedQueue.RankedFives => "RANKED_PREMADE_5x5",
            RankedQueue.Flex => "RANKED_FLEX_SR",
            _ => "RANKED_SOLO_5x5"
        };

        public static RankedQueue? FromType(string? type) => type?.ToUpperInvariant() switch
        {
            "RANKED_SOLO_5X5" => RankedQueue.SoloDuo,
            "RANKED_PREMADE_5X5" => RankedQueue.RankedFives,
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
                ReadEntry(ranks, entry);

            // Keep the established League-V4 semantics for the two standard ladders.
            // A missing weekend ladder can still be available from the League client.
            ranks.TryAdd(RankedQueue.SoloDuo, new RankData { Tier = "UNRANKED" });
            ranks.TryAdd(RankedQueue.Flex, new RankData { Tier = "UNRANKED" });
            return ranks;
        }

        public static Dictionary<RankedQueue, RankData> ParseClientRanks(JsonElement snapshot)
        {
            var ranks = new Dictionary<RankedQueue, RankData>();
            if (snapshot.ValueKind != JsonValueKind.Object)
                return ranks;
            if (snapshot.TryGetProperty("queues", out var queues) && queues.ValueKind == JsonValueKind.Array)
                foreach (var entry in queues.EnumerateArray())
                    ReadEntry(ranks, entry);
            if (snapshot.TryGetProperty("queueMap", out var map) && map.ValueKind == JsonValueKind.Object)
                foreach (var entry in map.EnumerateObject())
                    ReadEntry(ranks, entry.Value, entry.Name);
            return ranks;
        }

        private static void ReadEntry(Dictionary<RankedQueue, RankData> ranks, JsonElement entry, string? mapKey = null)
        {
            if (entry.ValueKind != JsonValueKind.Object)
                return;
            string? type = ReadText(entry, "queueType");
            if (FromType(mapKey ?? type) is not { } queue ||
                (mapKey != null && type != null && FromType(type) != queue) ||
                ReadText(entry, "tier") is not { } tier)
                return;

            tier = tier.ToUpperInvariant();
            string division = ReadText(entry, "division") ?? ReadText(entry, "rank") ?? "";
            ranks[queue] = new RankData
            {
                Tier = tier is "" or "NONE" or "NA" ? "UNRANKED" : tier,
                Rank = division is "NA" or "NONE" ? "" : division,
                LeaguePoints = ReadNumber(entry, "leaguePoints"),
                Wins = ReadNumber(entry, "wins"),
                Losses = ReadNumber(entry, "losses")
            };
        }

        private static string? ReadText(JsonElement entry, string name) =>
            entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static int ReadNumber(JsonElement entry, string name) =>
            entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? Math.Max(0, number) : 0;
    }
}
