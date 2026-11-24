using RiotAutoLogin.Models;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services;

internal static class LcuRankLookup
{
    internal static async Task<Dictionary<RankedQueue, RankData>> ReadAsync(string gameName, string tagLine,
        string region, Func<string, Task<string[]>> get)
    {
        async Task<JsonElement?> ReadObject(string path)
        {
            string[] response = await get(path).ConfigureAwait(false);
            if (response[0] != "200") return null;
            try
            {
                using var document = JsonDocument.Parse(response[1]);
                return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
            }
            catch (JsonException) { return null; }
        }

        string platform = RemotePlayerProfileLinks.RegionSlug(region);
        var locale = await ReadObject("riotclient/region-locale").ConfigureAwait(false);
        if (platform.Length == 0 || locale == null || RemotePlayerProfileLinks.RegionSlug(Text(locale.Value, "region")) != platform)
            return new();

        // Resolve the Riot ID inside this client. Public API and local PUUIDs are separate namespaces.
        string path = $"lol-summoner/v1/alias/lookup?gameName={Uri.EscapeDataString(gameName)}&tagLine={Uri.EscapeDataString(tagLine)}";
        var identity = await ReadObject(path).ConfigureAwait(false);
        if (identity == null || !identity.Value.TryGetProperty("alias", out var alias) ||
            !string.Equals(Text(alias, "gameName"), gameName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Text(alias, "tagLine"), tagLine, StringComparison.OrdinalIgnoreCase))
            return new();
        string? puuid = Text(identity.Value, "puuid");
        if (string.IsNullOrWhiteSpace(puuid)) return new();

        var snapshot = await ReadObject($"lol-ranked/v1/ranked-stats/{Uri.EscapeDataString(puuid)}").ConfigureAwait(false);
        return snapshot is { } stats ? RankedQueues.ParseClientRanks(stats) : new();
    }

    private static string? Text(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var field) &&
        field.ValueKind == JsonValueKind.String ? field.GetString() : null;
}
