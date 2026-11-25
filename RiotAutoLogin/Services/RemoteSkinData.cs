using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using RiotAutoLogin.Models;

namespace RiotAutoLogin.Services;

internal static class RemoteSkinData
{
    internal static List<RemoteSkinDto> Read(JsonElement carousel, JsonElement pickable, int championId, int selectedId)
    {
        var result = new List<RemoteSkinDto>();
        if (carousel.ValueKind != JsonValueKind.Array || pickable.ValueKind != JsonValueKind.Array) return result;
        var allowed = pickable.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out _)).Select(v => v.GetInt32()).Where(id => id > 0).ToHashSet();
        foreach (var skin in carousel.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Object))
        {
            Add(skin, false);
            // Preserve the actual selected chroma without making a separate chroma store or inventing availability.
            if (skin.TryGetProperty("childSkins", out var children) && children.ValueKind == JsonValueKind.Array)
                foreach (var child in children.EnumerateArray().Where(c => RemoteLobbyData.Number(c, "id") == selectedId)) Add(child, true);
        }
        return result.GroupBy(s => s.Id).Select(g => g.First()).ToList();
        void Add(JsonElement skin, bool chroma)
        {
            int id = RemoteLobbyData.Number(skin, "id");
            if (RemoteLobbyData.Number(skin, "championId") != championId || id <= 0 || (!allowed.Contains(id) && id != selectedId)) return;
            result.Add(new() { Id = id, Name = RemoteLobbyData.Text(skin, "name"), IsAvailable = allowed.Contains(id) && !RemoteLobbyData.Flag(skin, "disabled"),
                ImageUrl = AssetUrl(RemoteLobbyData.Text(skin, "splashPath")), ThumbnailUrl = AssetUrl(RemoteLobbyData.Text(skin, "tilePath")), IsChroma = chroma });
        }
    }
    internal static string AssetUrl(string path)
    {
        const string prefix = "/lol-game-data/assets/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";
        string relative = path[prefix.Length..].Replace('\\', '/').ToLowerInvariant();
        if (!(relative.EndsWith(".jpg") || relative.EndsWith(".jpeg") || relative.EndsWith(".png") || relative.EndsWith(".webp"))) return "";
        var parts = relative.Split('/');
        if (parts.Any(p => p.Length == 0 || p is "." or ".." || p.IndexOfAny([':', '?', '#', '%']) >= 0)) return "";
        return "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/" + string.Join("/", parts.Select(Uri.EscapeDataString));
    }
}
