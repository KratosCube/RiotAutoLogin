using System.Text.Json;
using RiotAutoLogin.Models;
using RiotAutoLogin.Services;

internal static class RemotePlanningChatChecks
{
    internal static void Run()
    {
        using var planning = JsonDocument.Parse("""{"localPlayerCellId":0,"actions":[[{"id":9,"type":"ban","actorCellId":0,"isInProgress":true,"completed":false,"championId":2}],[{"id":12,"type":"pick","actorCellId":0,"isInProgress":false,"completed":false}]]}""");
        var state = new RemotePickState { IsInChampSelect = true, ChampSelectPhase = "PLANNING", SelectedChampionId = 1, HoveredChampionId = 1 };
        RemoteDraftActions.Read(planning.RootElement, state);
        Require(state.IsPlanning && !state.CanBan && !state.CanPick && !state.IsMyTurn && state.ActionId == "" && state.PickActionId == "12" && state.HoveredChampionId == 1 && state.HoveredBanChampionId == 0,
            "A pre-activated future ban must not override the planning/hover phase.");
        state.ChampSelectPhase = "BAN_PICK"; RemoteDraftActions.Read(planning.RootElement, state);
        Require(state.CanBan && state.ActionId == "9" && state.HoveredBanChampionId == 2, "The same action becomes available when the actual ban phase starts.");
        state.ChampSelectPhase = "FINALIZATION"; RemoteDraftActions.Read(planning.RootElement, state);
        Require(!state.CanBan && !state.CanPick, "Finalization must not expose a stale action.");
        using var locked = JsonDocument.Parse("""{"localPlayerCellId":0,"actions":[[{"id":12,"type":"pick","actorCellId":0,"isInProgress":false,"completed":true,"championId":1}]]}""");
        RemoteDraftActions.Read(locked.RootElement, state);
        Require(state.IsPickLocked, "A completed own pick enables the post-pick view.");
        using var hover = JsonDocument.Parse(locked.RootElement.GetRawText().Replace("\"completed\":true", "\"completed\":false"));
        RemoteDraftActions.Read(hover.RootElement, state);
        Require(!state.IsPickLocked, "A nonzero champion ID alone does not prove lock-in.");

        using var lobby = JsonDocument.Parse("""{"gameConfig":{"showPositionSelector":true},"localMember":{"isLeader":false}}""");
        Require(RemoteLobbyPositions.CanSelect(lobby.RootElement), "Party members can set their own positions without leader privileges.");
        foreach (string special in new[] { "\"isCustom\":true,", "\"showQuickPlaySlotSelection\":true," })
        {
            using var other = JsonDocument.Parse(lobby.RootElement.GetRawText().Replace("\"showPositionSelector\":true", special + "\"showPositionSelector\":true"));
            Require(!RemoteLobbyPositions.CanSelect(other.RootElement), "Custom/Quickplay lobbies must not receive ordinary role preferences.");
        }
        Require(RemoteLobbyPositions.Valid("MIDDLE", "UTILITY") && RemoteLobbyPositions.Valid("FILL", "UNSELECTED") && !RemoteLobbyPositions.Valid("MIDDLE", "MIDDLE") && !RemoteLobbyPositions.Valid("TOP", "UNSELECTED"), "Position selection must validate two distinct roles or Fill.");
        var ready = new RemoteReadyCheck();
        string round = ready.Observe("ReadyCheck", 5, "None", "game1");
        Require(round == ready.Observe("ReadyCheck", 5, "None", "game1"), "Polling must retain the current ready-check identity.");
        Require(round != ready.Observe("ReadyCheck", 5, "None", "game2"), "A different game must reject an old ready-check response, even if the phase is unchanged.");
        round = ready.Observe("ReadyCheck", 5, "Declined", "game2");
        Require(round != ready.Observe("ReadyCheck", 5, "None", "game3"), "A new match after decline needs a new response identity.");
        ready.Observe("Lobby", 5, "");
        Require(round != ready.Observe("ReadyCheck", 5, "None"), "Leaving and re-entering ReadyCheck resets the response identity.");

        using var session = JsonDocument.Parse("""{"chatDetails":{"multiUserChatId":"new-room@champ-select.example","multiUserChatPassword":"never-return","mucJwtDto":{"channelClaim":"new","domain":"champ-select.example","jwt":"secret"}}}""");
        using var rooms = JsonDocument.Parse("""[{"id":"dm","pid":"new-room@champ-select.example","type":"chat"},{"id":"old","pid":"old-room@champ-select.example","type":"championSelect"},{"id":"current","pid":"new-room@champ-select.example","type":"championSelect"}]""");
        Require(RemoteChatData.FindRoom(session.RootElement, rooms.RootElement) == "current", "An opaque conversation ID must resolve via the current draft's channel, never a private message or stale room.");
        using var noHint = JsonDocument.Parse("{\"chatDetails\":{}}");
        Require(RemoteChatData.FindRoom(noHint.RootElement, rooms.RootElement) == "", "A matching conversation type alone must not authorize a stale room.");
        using var mucOnly = JsonDocument.Parse("""{"chatDetails":{"mucJwtDto":{"channelClaim":"new","domain":"champ-select.example","jwt":"secret"}}}""");
        using var mucRooms = JsonDocument.Parse("""[{"id":"opaque","type":"championSelect","mucJwtDto":{"channelClaim":"new","domain":"champ-select.example"}}]""");
        Require(RemoteChatData.FindRoom(mucOnly.RootElement, mucRooms.RootElement) == "opaque", "Modern MUC claims must bind to the actual current room without exposing the JWT.");
        var team = new[] { new RemoteTeamSlotDto { PlayerPuuid="self", IsLocalPlayer=true }, new RemoteTeamSlotDto { PlayerPuuid="hidden", RiotGameName="Private name", IsPlayerNameVisible=false } };
        using var messages = JsonDocument.Parse("""[{"id":"1","type":"chat","body":"<img src=x onerror=alert(1)>","fromPuuid":"hidden","timestamp":"2026-11-26T00:00:00Z"},{"id":"2","type":"system","body":"noise"},{"id":"3","type":"chat","body":"hello","fromPuuid":"self"}]""");
        var parsed = RemoteChatData.Messages(messages.RootElement, team);
        Require(parsed.Count == 2 && parsed.Single(m => m.Id == "1").Author == "Ally 2" && parsed.Single(m => m.Id == "3").Author == "You" && parsed.All(m => m.Author != "Private name"), "Chat must retain ranked anonymity, label self and omit system noise.");
        Require(RemoteChatData.ValidBody("hello 😀") && !RemoteChatData.ValidBody("  ") && !RemoteChatData.ValidBody(new string('x',501)) && !RemoteChatData.ValidBody("bad\0message"), "Only bounded, nonempty chat text may be sent.");
        Console.WriteLine("Remote planning-before-ban, confirmed lock-in, lobby positions, ready checks and scoped team-chat checks passed.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
