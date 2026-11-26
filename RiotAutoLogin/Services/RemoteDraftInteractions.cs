using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using RiotAutoLogin.Models;

namespace RiotAutoLogin.Services;

internal static class RemoteDraftInteractions
{
    internal static string DraftId(JsonElement session)
    {
        string game = Scalar(session, "gameId");
        return game.Length > 0 && game != "0" ? game : RemoteLobbyData.Text(session, "id");
    }
    internal static List<RemoteSwapDto> ReadSwaps(JsonElement session, string field, string kind, JsonElement fallback = default)
    {
        var result = new List<RemoteSwapDto>();
        if (!session.TryGetProperty(field, out var source) || source.ValueKind != JsonValueKind.Array) source = fallback;
        if (source.ValueKind != JsonValueKind.Array) return result;
        int self = RemoteLobbyData.Number(session, "localPlayerCellId");
        var team = session.TryGetProperty("myTeam", out var players) && players.ValueKind == JsonValueKind.Array ? players.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
        foreach (var swap in source.EnumerateArray().Where(s => s.ValueKind == JsonValueKind.Object))
        {
            string id = Scalar(swap, "id"), status = RemoteLobbyData.Text(swap, "state").ToUpperInvariant();
            int cell = RemoteLobbyData.Number(swap, "cellId");
            if (!ValidId(id) || !swap.TryGetProperty("cellId", out _) || cell == self || !team.Any(p => RemoteLobbyData.Number(p, "cellId") == cell)) continue;
            if (status is not ("AVAILABLE" or "RECEIVED" or "SENT")) continue;
            result.Add(new() { Id = id, Kind = kind, CellId = cell, State = status });
        }
        return result.GroupBy(s => s.Id).Select(g => g.First()).ToList();
    }
    internal static string Scalar(JsonElement root, string field) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(field, out var value)
        ? value.ValueKind == JsonValueKind.Number ? value.GetRawText() : value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "" : "";
    internal static bool ValidId(string id) => id.Length is > 0 and <= 19 && id.All(char.IsAsciiDigit) && long.TryParse(id, out _);
    internal static bool Allows(RemoteSwapDto swap, string action) => (swap.State, action) is
        ("AVAILABLE", "request") or ("RECEIVED", "accept") or ("RECEIVED", "decline") or ("SENT", "cancel");
}
