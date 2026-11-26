using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RiotAutoLogin.Models;

namespace RiotAutoLogin.Services;
public sealed partial class RemotePickServerService
{
    private readonly RemoteReadyCheck _readyCheck = new();
    private readonly SemaphoreSlim _readyCheckGate = new(1, 1);
    private async Task<RemotePickActionResult> RespondReadyCheckAsync(string body, bool accept)
    {
        if (!await _readyCheckGate.WaitAsync(0)) return Failure("A match response is already in progress.");
        try
        {
            using var request = JsonDocument.Parse(body);
            var state = await GetTimerStateAsync();
            if (state.Phase != "ReadyCheck" || state.ReadyCheckId.Length == 0 || state.ReadyCheckId != RemoteLobbyData.Text(request.RootElement, "readyCheckId"))
                return Failure("That ready check has ended. Refresh before responding to another match.");
            if (accept && state.MatchAccepted || !accept && state.MatchDeclined) return new() { Success = true, Message = accept ? "Match accepted." : "Match declined." };
            if (accept ? !state.CanAcceptMatch : !state.CanDeclineMatch) return Failure("The match already has a response.");
            if (!accept) AutoAcceptDeclineGuardService.RegisterRemoteDecline();
            var result = await LCUService.ClientRequestAsync("POST", accept ? "lol-matchmaking/v1/ready-check/accept" : "lol-matchmaking/v1/ready-check/decline");
            return result[0].StartsWith("2", StringComparison.Ordinal) ? new() { Success = true, Message = accept ? "Match accepted." : "Match declined." } : Failure("League Client could not confirm your match response.");
        }
        catch (JsonException) { return Failure("Refresh the current ready check before responding."); }
        finally { _readyCheckGate.Release(); }
    }
    private async Task<RemotePickActionResult> SetPositionsAsync(string body)
    {
        if (!await _lobbyActionGate.WaitAsync(0)) return Failure("Another lobby action is in progress.");
        try
        {
            using var request = JsonDocument.Parse(body);
            string first = RemoteLobbyData.Text(request.RootElement, "firstPreference"), second = RemoteLobbyData.Text(request.RootElement, "secondPreference");
            if (!RemoteLobbyPositions.Valid(first, second)) return Failure("Choose two different roles, or Fill.");
            if (await LCUService.GetCurrentGamePhaseAsync() != "Lobby") return Failure("Cancel matchmaking before changing your positions.");
            using var lobby = await ReadClientJsonAsync("lol-lobby/v2/lobby");
            if (lobby == null || !RemoteLobbyPositions.CanSelect(lobby.RootElement) ||
                !lobby.RootElement.TryGetProperty("gameConfig", out var config) || RemoteLobbyData.Number(config, "queueId") != RemoteLobbyData.Number(request.RootElement, "queueId"))
                return Failure("This lobby no longer supports these position preferences.");
            var reply = await LCUService.ClientRequestAsync("PUT", "lol-lobby/v2/lobby/members/localMember/position-preferences", JsonSerializer.Serialize(new { firstPreference = first, secondPreference = second }));
            if (!reply[0].StartsWith("2", StringComparison.Ordinal)) return Failure("League Client rejected these positions. Check this queue's role requirements.");
            for (int attempt = 0; attempt < 4; attempt++)
            {
                LCUService.InvalidateCachedReads();
                using var current = await ReadClientJsonAsync("lol-lobby/v2/lobby");
                if (current != null && current.RootElement.TryGetProperty("localMember", out var local) &&
                    RemoteLobbyData.Text(local, "firstPositionPreference") == first &&
                    (RemoteLobbyData.Text(local, "secondPositionPreference") == second || first == "FILL" && RemoteLobbyData.Text(local, "secondPositionPreference") == "FILL"))
                    return new() { Success = true, Message = "Positions updated." };
                await Task.Delay(120);
            }
            return Failure("The client has not confirmed your positions. Refresh and try again.");
        }
        catch (JsonException) { return Failure("Choose your position preferences."); }
        finally { _lobbyActionGate.Release(); }
    }
}
