using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using RiotAutoLogin.Models;

namespace RiotAutoLogin.Services;

internal static class RemoteRandomSelection
{
    internal static bool IsRandom(JsonElement session, int queueId, string mode) =>
        !mode.Equals("CHERRY", StringComparison.OrdinalIgnoreCase) &&
        (mode.Equals("ARAM", StringComparison.OrdinalIgnoreCase) || queueId is 450 or 720 ||
         RemoteLobbyData.Flag(session, "benchEnabled") || RemoteLobbyData.Flag(session, "allowRerolling"));

    internal static void Read(JsonElement session, RemotePickState state, IEnumerable<int> cards)
    {
        state.BenchEnabled = RemoteLobbyData.Flag(session, "benchEnabled");
        state.BenchChampionIds = session.TryGetProperty("benchChampions", out var bench) && bench.ValueKind == JsonValueKind.Array
            ? bench.EnumerateArray().Select(c => c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out int id) ? id : RemoteLobbyData.Number(c, "championId")).Where(id => id > 0).Distinct().ToList() : new();
        state.CardChampionIds = RemoteLobbyData.Flag(session, "allowSubsetChampionPicks") ? cards.Where(id => id > 0).Distinct().ToList() : new();
        state.RerollsRemaining = Math.Max(0, RemoteLobbyData.Number(session, "rerollsRemaining"));
        state.CanReroll = state.IsRandomChampionMode && RemoteLobbyData.Flag(session, "allowRerolling") && state.RerollsRemaining > 0;
        bool openPick = false;
        if (session.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
            foreach (var group in actions.EnumerateArray().Where(g => g.ValueKind == JsonValueKind.Array))
                foreach (var action in group.EnumerateArray())
                    if (RemoteLobbyData.Number(action, "actorCellId") == RemoteLobbyData.Number(session, "localPlayerCellId") &&
                        RemoteLobbyData.Text(action, "type") == "pick" && !RemoteLobbyData.Flag(action, "completed")) openPick = true;
        state.CanSelectCard = state.IsRandomChampionMode && state.CardChampionIds.Count > 0 && openPick && state.PickActionId.Length > 0;
        if (state.IsRandomChampionMode)
            state.AvailableChampionIds = state.CardChampionIds.Concat(state.BenchEnabled ? state.BenchChampionIds : Enumerable.Empty<int>())
                .Append(state.SelectedChampionId).Where(id => id > 0).Distinct().ToList();
    }

    internal static bool CanTakeBench(RemotePickState state, int championId) => state.IsInChampSelect && state.IsRandomChampionMode &&
        state.BenchEnabled && championId > 0 && state.BenchChampionIds.Contains(championId) && championId != state.SelectedChampionId;
}
