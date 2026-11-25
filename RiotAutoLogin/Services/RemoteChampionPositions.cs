using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace RiotAutoLogin.Services;

internal static class RemoteChampionPositions
{
    internal static Dictionary<int, string[]> Parse(JsonElement root)
    {
        var result = new Dictionary<int, string[]>();
        if (root.ValueKind != JsonValueKind.Object) return result;
        foreach (var champion in root.EnumerateObject())
        {
            if (!int.TryParse(champion.Name, out int id) || id <= 0 || champion.Value.ValueKind != JsonValueKind.Object ||
                !champion.Value.TryGetProperty("recommendedPositions", out var positions) || positions.ValueKind != JsonValueKind.Array) continue;
            var normalized = positions.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String)
                .Select(p => Normalize(p.GetString())).Where(p => p.Length > 0).Distinct().ToArray();
            if (normalized.Length > 0) result[id] = normalized;
        }
        return result;
    }

    private static string Normalize(string? position) => position?.ToUpperInvariant() switch
    {
        "TOP" => "top", "JUNGLE" => "jungle", "MIDDLE" or "MID" => "mid",
        "BOTTOM" or "BOT" or "ADC" => "bot", "UTILITY" or "SUPPORT" => "support", _ => ""
    };
}
