using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RiotAutoLogin.Models;

namespace RiotAutoLogin.Services;

// Planning declares through the local pending PICK action, even while that action is inactive.
// my-selection is the skin/spell resource; it has no championPickIntent write field.
internal static class RemoteChampionHover
{
    internal const string SessionPath = "lol-champ-select/v1/session";
    internal static RemotePickState Read(JsonElement session)
    {
        var state = new RemotePickState { IsInChampSelect = true, DraftId = RemoteDraftInteractions.DraftId(session), QueueId = RemoteLobbyData.Number(session, "queueId") };
        if (session.TryGetProperty("timer", out var timer)) state.ChampSelectPhase = RemoteLobbyData.Text(timer, "phase");
        int self = RemoteLobbyData.Number(session, "localPlayerCellId");
        if (session.TryGetProperty("myTeam", out var team) && team.ValueKind == JsonValueKind.Array)
            foreach (var player in team.EnumerateArray())
                if (player.TryGetProperty("cellId", out _) && RemoteLobbyData.Number(player, "cellId") == self)
                {
                    state.SelectedChampionId = RemoteLobbyData.Number(player, "championId");
                    state.PickIntentChampionId = state.HoveredChampionId = RemoteLobbyData.Number(player, "championPickIntent");
                    if (state.HoveredChampionId > 0) state.HoveredChampionSource = "PickIntent";
                    break;
                }
        RemoteDraftActions.Read(session, state);
        return state;
    }

    internal static async Task<RemotePickActionResult> ApplyAsync(int championId, string expectedDraft,
        Func<Task<string>> phase, Func<string, string, string?, Task<string[]>> request,
        Action invalidate, Func<int, Task> delay, string gameMode = "")
    {
        if (championId <= 0) return Fail("Invalid champion.");
        if (await phase() != "ChampSelect") return Fail("You are not in champion select.");
        invalidate();
        var reply = await request("GET", SessionPath, null);
        using var session = Document(reply);
        if (session == null) return Fail("Cannot read the current champion select session.");
        var state = await ReadState(session.RootElement);
        if (expectedDraft.Length > 0 && expectedDraft != state.DraftId) return Fail("The draft has changed. Choose your champion again.");
        if (RemoteRandomSelection.IsRandom(session.RootElement, state.QueueId, gameMode)) return Fail("Choose a champion card or take a champion from the bench.");
        if (state.DraftId.Length == 0 || state.IsPickLocked || !RemoteDraftInteractions.ValidId(state.PickActionId) || !OpenPick(session.RootElement, state.PickActionId) ||
            (!state.IsPlanning && !state.CanPick && (state.ChampSelectPhase != "BAN_PICK" || state.CanBan)))
            return Fail("The client is not accepting a pick hover in this phase.");
        if (Banned(session.RootElement, championId)) return Fail("This champion is banned.");
        if (state.HoveredChampionId == championId) return Confirm(state);
        string actionId = state.PickActionId;
        var result = await request("PATCH", $"{SessionPath}/actions/{actionId}", JsonSerializer.Serialize(new { championId, completed = false }));
        if (!result[0].StartsWith("2", StringComparison.Ordinal)) return Fail($"League Client rejected this hover (status {result[0]}).");
        // Mutation acknowledgement alone is not confirmation. Read the same pending action back.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (attempt > 0) await delay(60);
            invalidate();
            using var observed = Document(await request("GET", SessionPath, null));
            if (observed == null) continue;
            var current = await ReadState(observed.RootElement);
            if (current.DraftId != state.DraftId || current.PickActionId != actionId || current.IsPickLocked || !OpenPick(observed.RootElement, actionId))
                return Fail("The draft or pick action has changed. Refresh before hovering again.");
            if (current.HoveredChampionId == championId)
            {
                if (await phase() != "ChampSelect") return Fail("Champion select has ended.");
                return Confirm(current);
            }
        }
        return new() { Success = false, ConfirmedChampionId = state.HoveredChampionId, ConfirmedChampionSource = state.HoveredChampionSource,
            Message = "League Client has not confirmed this hover. Try again while the hover phase is open." };

        async Task<RemotePickState> ReadState(JsonElement root)
        {
            var value = Read(root);
            if (value.ChampSelectPhase.Length == 0)
            {
                using var timer = Document(await request("GET", SessionPath + "/timer", null));
                if (timer != null)
                {
                    value.ChampSelectPhase = RemoteLobbyData.Text(timer.RootElement, "phase");
                    RemoteDraftActions.Read(root, value);
                }
            }
            return value;
        }
    }
    private static JsonDocument? Document(string[] reply)
    {
        if (reply.Length < 2 || reply[0] != "200") return null;
        try { var doc = JsonDocument.Parse(reply[1]); if (doc.RootElement.ValueKind == JsonValueKind.Object) return doc; doc.Dispose(); }
        catch (JsonException) { }
        return null;
    }
    private static bool OpenPick(JsonElement session, string actionId)
    {
        if (!session.TryGetProperty("localPlayerCellId", out _) || !session.TryGetProperty("actions", out var groups) || groups.ValueKind != JsonValueKind.Array) return false;
        int self = RemoteLobbyData.Number(session, "localPlayerCellId");
        return groups.EnumerateArray().Where(g => g.ValueKind == JsonValueKind.Array).SelectMany(g => g.EnumerateArray())
            .Any(a => RemoteDraftInteractions.Scalar(a, "id") == actionId && RemoteLobbyData.Text(a, "type") == "pick" &&
                a.TryGetProperty("actorCellId", out _) && RemoteLobbyData.Number(a, "actorCellId") == self && !RemoteLobbyData.Flag(a, "completed"));
    }
    private static bool Banned(JsonElement session, int championId)
    {
        if (!session.TryGetProperty("bans", out var bans) || bans.ValueKind != JsonValueKind.Object) return false;
        foreach (var side in bans.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Array))
            if (side.Value.EnumerateArray().Any(id => id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out int n) && n == championId)) return true;
        return false;
    }
    private static RemotePickActionResult Confirm(RemotePickState state) => new() { Success = true, ConfirmedChampionId = state.HoveredChampionId,
        ConfirmedChampionSource = state.HoveredChampionSource, Message = "Hover confirmed in League Client." };
    private static RemotePickActionResult Fail(string message) => new() { Success = false, Message = message };
}
