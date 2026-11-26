using System;
using System.Linq;
using System.Text.Json;
using RiotAutoLogin.Models;

namespace RiotAutoLogin.Services;

internal static class RemoteDraftActions
{
    internal static void Read(JsonElement root, RemotePickState state)
    {
        state.IsMyTurn = false; state.ActionId = state.ActionType = state.PickActionId = state.TimerActionKey = "";
        state.ActionGroupIndex = -1; state.IsPickLocked = false;
        if (!root.TryGetProperty("localPlayerCellId", out _) || !root.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array) return;
        int self = RemoteLobbyData.Number(root, "localPlayerCellId"), groupIndex = 0, localPicks = 0, completedPicks = 0;
        bool foundOpenPick = false;
        bool planning = state.ChampSelectPhase.Contains("PLANNING", StringComparison.OrdinalIgnoreCase);
        bool blocked = planning || state.ChampSelectPhase.Contains("FINAL", StringComparison.OrdinalIgnoreCase) || state.ChampSelectPhase.Contains("GAME_START", StringComparison.OrdinalIgnoreCase);
        foreach (var group in actions.EnumerateArray())
        {
            if (group.ValueKind != JsonValueKind.Array) { groupIndex++; continue; }
            foreach (var action in group.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.Object))
            {
                string type = RemoteLobbyData.Text(action, "type").ToLowerInvariant(), id = RemoteDraftInteractions.Scalar(action, "id");
                bool active = RemoteLobbyData.Flag(action, "isInProgress"), complete = RemoteLobbyData.Flag(action, "completed");
                if (!blocked && active && !complete && state.TimerActionKey.Length == 0)
                { state.ActionGroupIndex = groupIndex; state.TimerActionKey = $"group:{groupIndex}|type:{type}"; }
                if (!action.TryGetProperty("actorCellId", out _) || RemoteLobbyData.Number(action, "actorCellId") != self) continue;
                if (type == "pick")
                {
                    localPicks++; if (complete) completedPicks++;
                    if (state.PickActionId.Length == 0 || !complete && !foundOpenPick) state.PickActionId = id;
                    if (!complete) foundOpenPick = true;
                    if (!blocked && active && !complete && RemoteLobbyData.Number(action, "championId") is > 0 and var hover)
                    { state.HoveredChampionId = hover; state.HoveredChampionSource = "ActivePick"; }
                }
                if (blocked || !active || complete) continue;
                if (type == "ban") state.HoveredBanChampionId = RemoteLobbyData.Number(action, "championId");
                state.IsMyTurn = true; state.ActionId = id; state.ActionType = type; state.ActionGroupIndex = groupIndex;
            }
            groupIndex++;
        }
        state.IsPickLocked = !planning && localPicks > 0 && localPicks == completedPicks && state.SelectedChampionId > 0;
        if (blocked) { state.HoveredBanChampionId = 0; state.TimerActionKey = $"phase:{state.ChampSelectPhase}"; }
        else if (state.TimerActionKey.Length == 0) state.TimerActionKey = $"phase:{state.ChampSelectPhase}|action:{state.ActionType}|id:{state.ActionId}";
    }
}
