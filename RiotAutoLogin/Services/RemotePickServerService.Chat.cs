using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RiotAutoLogin.Models;

namespace RiotAutoLogin.Services;
public sealed partial class RemotePickServerService
{
    private readonly SemaphoreSlim _chatSendGate = new(1, 1);
    private async Task<(RemotePickState State, string Room)> ReadChatContextAsync()
    {
        var state = new RemotePickState();
        if (await LCUService.GetCurrentGamePhaseAsync() != "ChampSelect") return (state, "");
        using var session = await ReadClientJsonAsync("lol-champ-select/v1/session");
        if (session == null) return (state, "");
        state.IsInChampSelect = true; state.DraftId = RemoteDraftInteractions.DraftId(session.RootElement);
        ParseLocalPlayerSelection(session.RootElement, state); ParseTimer(session.RootElement, state); ParseActions(session.RootElement, state);
        ParseQueueAndMode(session.RootElement, state); ApplyKnownQueueMode(state);
        if (!state.IsPickLocked || state.IsRandomChampionMode || state.DraftId.Length == 0) return (state, "");
        ParseTeamSlots(session.RootElement, state); FillCachedPlayerProfiles(state);
        using var conversations = await ReadClientJsonAsync("lol-chat/v1/conversations", JsonValueKind.Array);
        return (state, conversations == null ? "" : RemoteChatData.FindRoom(session.RootElement, conversations.RootElement));
    }
    private async Task<RemoteChatDto> GetChatAsync()
    {
        var (state, room) = await ReadChatContextAsync();
        var chat = new RemoteChatDto { DraftId = state.DraftId, RoomId = room };
        if (room.Length == 0) return chat;
        using var messages = await ReadClientJsonAsync($"lol-chat/v1/conversations/{Uri.EscapeDataString(room)}/messages", JsonValueKind.Array);
        if (messages == null) return chat;
        chat.IsAvailable = true; chat.Messages = RemoteChatData.Messages(messages.RootElement, state.AllySlots);
        return chat;
    }
    private async Task<RemotePickActionResult> SendChatAsync(string body)
    {
        if (!await _chatSendGate.WaitAsync(0)) return Failure("A message is already being sent.");
        try
        {
            using var request = JsonDocument.Parse(body);
            string message = RemoteLobbyData.Text(request.RootElement, "message");
            if (!RemoteChatData.ValidBody(message)) return Failure("Write a message of 1–500 characters.");
            var (state, room) = await ReadChatContextAsync();
            if (room.Length == 0 || state.DraftId != RemoteLobbyData.Text(request.RootElement, "draftId") || room != RemoteLobbyData.Text(request.RootElement, "roomId"))
                return Failure("That team chat has ended. Refresh before sending to another lobby.");
            LCUService.InvalidateCachedReads();
            using var latest = await ReadClientJsonAsync("lol-champ-select/v1/session");
            using var conversations = await ReadClientJsonAsync("lol-chat/v1/conversations", JsonValueKind.Array);
            if (latest == null || conversations == null || RemoteDraftInteractions.DraftId(latest.RootElement) != state.DraftId ||
                RemoteChatData.FindRoom(latest.RootElement, conversations.RootElement) != room || await LCUService.GetCurrentGamePhaseAsync() != "ChampSelect")
                return Failure("Champion select has ended. The message was not sent.");
            var response = await LCUService.ClientRequestAsync("POST", $"lol-chat/v1/conversations/{Uri.EscapeDataString(room)}/messages", JsonSerializer.Serialize(new { body = message, type = "chat" }));
            return response[0].StartsWith("2", StringComparison.Ordinal) ? new() { Success = true, Message = "Message sent." } : Failure("League Client did not confirm your message. Your text has been kept.");
        }
        catch (JsonException) { return Failure("Invalid chat message."); }
        finally { _chatSendGate.Release(); }
    }
}
