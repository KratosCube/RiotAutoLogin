using RiotAutoLogin.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services;

public sealed partial class RemotePickServerService
{
    private readonly SemaphoreSlim _catalogGate = new(1, 1);
    private sealed record QueueCatalogCache(JsonElement Json);
    private QueueCatalogCache? _queueCatalog;
    private DateTime _nextQueueCatalog;
    private int _clientPid;

    private void RefreshClientContext()
    {
        int pid = LCUService.ClientProcessId;
        if (_clientPid == pid) return;
        _clientPid = pid;
        _queueCatalog = null; _nextQueueCatalog = DateTime.MinValue;
        _playerProfiles.Clear(); _profileRegion = ""; _profileRegionExpiresUtc = DateTime.MinValue;
        InvalidateLoadout();
        _recommendedRuneCache = null; _unsupportedRuneEndpoints.Clear();
        RemoteChampionAvailabilityService.Reset();
    }

    private async Task<bool> EnsureQueueCatalogAsync(bool fresh = false)
    {
        await _catalogGate.WaitAsync();
        try
        {
            if (!fresh && DateTime.UtcNow < _nextQueueCatalog) return _queueCatalog != null;
            _nextQueueCatalog = DateTime.UtcNow.AddSeconds(30);
            int pid = LCUService.ClientProcessId;
            var reply = await LCUService.ClientRequestAsync("GET", "lol-game-queues/v1/queues");
            if (reply[0] != "200") return false;
            try
            {
                using var json = JsonDocument.Parse(reply[1]);
                if (json.RootElement.ValueKind != JsonValueKind.Array || pid != LCUService.ClientProcessId) return false;
                _queueCatalog = new(json.RootElement.Clone()); return true;
            }
            catch (JsonException) { return false; }
        }
        finally { _catalogGate.Release(); }
    }

    private void ApplyKnownQueueMode(RemotePickState state)
    {
        if (_queueCatalog is not { } catalog) return;
        var queue = catalog.Json.EnumerateArray().FirstOrDefault(q => RemoteLobbyData.Number(q, "id") == state.QueueId);
        state.GameMode = RemoteLobbyData.Text(queue, "gameMode");
        if (state.MapId <= 0) state.MapId = RemoteLobbyData.Number(queue, "mapId");
        if (state.GameMode == "CHERRY") state.IsRandomChampionMode = false;
        if (RemoteLobbyData.IsAram(state.QueueId, state.GameMode)) state.IsRandomChampionMode = true;
        if (state.GameMode == "CHERRY") { state.QueueName = "Arena"; state.ChampSelectMode = "Arena"; }
        else if (RemoteLobbyData.Text(queue, "name") is { Length: > 0 } name) state.QueueName = name;
    }

    private async Task AddLobbyDetailsAsync(RemotePickState state)
    {
        if (state.Phase is not ("Lobby" or "None" or "Matchmaking" or "ReadyCheck")) return;
        await EnsureQueueCatalogAsync();
        if (state.Phase == "None") state.CanChangeQueue = true;
        ApplyKnownQueueMode(state);
        if (_queueCatalog is { } catalog) state.Queues = RemoteLobbyData.Queues(catalog.Json, state.PartySize);
        if (state.LobbyMembers.Count == 0) return;
        string region = GetProfileRegion();
        var keys = state.LobbyMembers.Where(m => m.TagLine.Length == 0).Select(m =>
            m.Puuid.Length > 0 ? "p:" + m.Puuid : m.SummonerId.Length > 0 && m.SummonerId.All(char.IsDigit) ? "s:" + m.SummonerId : "").Where(k => k.Length > 0).Distinct();
        await Task.WhenAll(keys.Select(ResolvePlayerProfileAsync));
        foreach (var member in state.LobbyMembers)
        {
            string key = member.Puuid.Length > 0 ? "p:" + member.Puuid : "s:" + member.SummonerId;
            string name = member.PlayerName, tag = member.TagLine;
            if (tag.Length == 0 && _playerProfiles.TryGetValue(key, out var cached) && cached.ExpiresUtc > DateTime.UtcNow && cached.GameName.Length > 0)
            { name = cached.GameName; tag = cached.TagLine; member.PlayerName = name; member.TagLine = tag; }
            member.ProfileUrl = RemotePlayerProfileLinks.Create(region, name, tag);
        }
    }

    private async Task<RemotePickActionResult> ChangeQueueAsync(string body)
    {
        if (!await _lobbyActionGate.WaitAsync(0)) return Failure("A lobby action is already in progress.");
        try
        {
            int id;
            try { using var request = JsonDocument.Parse(body); id = RemoteLobbyData.Number(request.RootElement, "queueId"); }
            catch (JsonException) { return Failure("Choose an available queue."); }
            string phase = await LCUService.GetCurrentGamePhaseAsync();
            if (phase is not ("None" or "Lobby")) return Failure("Cancel matchmaking before changing queues.");
            int partySize = 1;
            using var lobby = await ReadClientObjectAsync("lol-lobby/v2/lobby");
            if (phase == "Lobby")
            {
                if (lobby == null || !RemoteLobbyData.CanChange(lobby.RootElement)) return Failure("The party leader can change queues in a normal lobby.");
                partySize = RemoteLobbyData.Members(lobby.RootElement).Count(m => !m.IsSpectator);
            }
            if (!await EnsureQueueCatalogAsync(fresh: true)) return Failure("The client cannot confirm available queues right now.");
            if (_queueCatalog is not { } catalog || !RemoteLobbyData.Queues(catalog.Json, partySize).Any(q => q.Id == id && q.IsAvailable))
                return Failure("This queue is unavailable or does not support the current party.");
            // Recheck the phase after reading the queue catalog: never replace an active search or draft.
            LCUService.InvalidateCachedReads();
            if (await LCUService.GetCurrentGamePhaseAsync() != phase) return Failure("The lobby has changed. Try again.");
            var result = await LCUService.ClientRequestAsync("POST", "lol-lobby/v2/lobby", JsonSerializer.Serialize(new { queueId = id }));
            return result[0].StartsWith("2", StringComparison.Ordinal)
                ? new() { Success = true, Message = "Queue changed." }
                : Failure("League Client could not change the queue. Check level, rank and party requirements in the client.");
        }
        finally { _lobbyActionGate.Release(); }
    }

    private async Task<RemotePickActionResult> SwapBenchAsync(string body)
    {
        var request = ParseChampionRequest(body);
        if (request == null) return Failure("Invalid champion.");
        if (!await _lobbyActionGate.WaitAsync(0)) return Failure("Another action is in progress.");
        try
        {
            var state = await GetTimerStateAsync();
            if (!RemoteRandomSelection.CanTakeBench(state, request.ChampionId)) return Failure("That champion is no longer on the bench.");
            var result = await LCUService.ClientRequestAsync("POST", $"lol-champ-select/v1/session/bench/swap/{request.ChampionId}");
            return new() { Success = result[0].StartsWith("2", StringComparison.Ordinal), Message = result[0].StartsWith("2", StringComparison.Ordinal) ? "Champion taken from the bench." : "League Client rejected the bench swap. The champion may already have been taken." };
        }
        finally { _lobbyActionGate.Release(); }
    }

    private async Task<RemotePickActionResult> RerollAsync()
    {
        if (!await _lobbyActionGate.WaitAsync(0)) return Failure("Another action is in progress.");
        try
        {
            var state = await GetTimerStateAsync();
            if (!state.IsInChampSelect || !state.CanReroll) return Failure("Rerolls are not available in this session.");
            var result = await LCUService.ClientRequestAsync("POST", "lol-champ-select/v1/session/my-selection/reroll");
            return new() { Success = result[0].StartsWith("2", StringComparison.Ordinal), Message = result[0].StartsWith("2", StringComparison.Ordinal) ? "Champion rerolled." : "League Client rejected the reroll." };
        }
        finally { _lobbyActionGate.Release(); }
    }
}
