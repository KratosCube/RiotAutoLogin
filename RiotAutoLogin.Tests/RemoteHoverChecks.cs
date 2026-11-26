using System.Text.Json;
using System.Text.Json.Nodes;
using RiotAutoLogin.Services;

internal static class RemoteHoverChecks
{
    // Captures the LCU contract, not the web route: future pick action is inactive, future ban is marked active,
    // and the myTeam mirror deliberately lags behind the declaration stored in the action.
    private const string Planning = """{"gameId":123456,"queueId":420,"localPlayerCellId":0,"timer":{"phase":"PLANNING","adjustedTimeLeftInPhase":8000},"myTeam":[{"cellId":0,"championId":0,"championPickIntent":0}],"bans":{"myTeamBans":[],"theirTeamBans":[]},"actions":[[{"id":9,"actorCellId":0,"type":"ban","championId":0,"isInProgress":true,"completed":false}],[{"id":22,"actorCellId":0,"type":"pick","championId":0,"isInProgress":false,"completed":false}],[{"id":23,"actorCellId":1,"type":"pick","championId":0,"isInProgress":false,"completed":false}]]}""";
    internal static async Task Run()
    {
        var client = JsonNode.Parse(Planning)!;
        var writes = new List<(string Path, string Body)>(); int invalidations = 0, delayCalls = 0;
        Task Delay(int _) { delayCalls++; return Task.CompletedTask; }
        Task<string[]> Transport(string method, string path, string? body)
        {
            if (method == "GET" && path == RemoteChampionHover.SessionPath) return Task.FromResult(new[] { "200", client.ToJsonString() });
            Require(method == "PATCH" && path == "lol-champ-select/v1/session/actions/22", "Planning must PATCH the inactive local PICK action, never the ban, another player or my-selection.");
            using var payload = JsonDocument.Parse(body!);
            Require(payload.RootElement.TryGetProperty("completed", out var completed) && completed.ValueKind == JsonValueKind.False && !payload.RootElement.TryGetProperty("championPickIntent", out _), "Hover must never lock in or send unsupported my-selection intent fields.");
            writes.Add((path,body!)); client["actions"]![1]![0]!["championId"] = payload.RootElement.GetProperty("championId").GetInt32();
            return Task.FromResult(new[] { "204", "" });
        }
        foreach (int id in new[] { 1, 17, 99 })
        {
            var result = await RemoteChampionHover.ApplyAsync(id,"123456",() => Task.FromResult("ChampSelect"),Transport,() => invalidations++,Delay);
            Require(result.Success && result.ConfirmedChampionId == id && result.ConfirmedChampionSource == "PlanningPickAction", "Every planning hover change must be read back from the actual inactive action, even before myTeam updates.");
            using var observed = JsonDocument.Parse(client.ToJsonString());
            var state = RemoteChampionHover.Read(observed.RootElement);
            Require(state.HoveredChampionId == id && state.PickIntentChampionId == id && !state.CanBan && !state.CanPick && !state.IsPickLocked, "Full/live state must preserve the confirmed planning intent without enabling ban or lock-in.");
        }
        Require(writes.Count == 3 && delayCalls == 0 && invalidations == 6, "A healthy declaration needs one write and an immediate fresh read, without slow fallback probes.");
        var same = await RemoteChampionHover.ApplyAsync(99,"123456",() => Task.FromResult("ChampSelect"),Transport,() => {},Delay);
        Require(same.Success && writes.Count == 3, "Already confirmed intent is idempotent.");
        var stale = await RemoteChampionHover.ApplyAsync(1,"different-game",() => Task.FromResult("ChampSelect"),Transport,() => {},Delay);
        Require(!stale.Success && writes.Count == 3, "An old browser draft must never change the next game's intent.");

        client = JsonNode.Parse(Planning)!; int fakeWrites = 0;
        Task<string[]> Ignored(string method,string path,string? body)
        {
            if (method == "PATCH") { fakeWrites++; return Task.FromResult(new[] { "200", "{}" }); }
            return Task.FromResult(new[] { "200", client.ToJsonString() });
        }
        var ignored = await RemoteChampionHover.ApplyAsync(17,"123456",() => Task.FromResult("ChampSelect"),Ignored,() => {},Delay);
        Require(!ignored.Success && fakeWrites == 1 && ignored.ConfirmedChampionId == 0, "A 200 acknowledgement that changes nothing must not create a fictional hover.");
        client["timer"]!["phase"] = "BAN_PICK";
        var ban = await RemoteChampionHover.ApplyAsync(17,"123456",() => Task.FromResult("ChampSelect"),Ignored,() => {},Delay);
        Require(!ban.Success && fakeWrites == 1, "A queued planning click must not mutate an active ban turn.");
        client = JsonNode.Parse(Planning)!; client["actions"]![1]![0]!["completed"] = true;
        var done = await RemoteChampionHover.ApplyAsync(17,"123456",() => Task.FromResult("ChampSelect"),Ignored,() => {},Delay);
        Require(!done.Success && fakeWrites == 1, "Even a missing myTeam champion cannot make a completed action eligible for hover.");

        client = JsonNode.Parse(Planning)!;
        Task<string[]> ChangedDraft(string method,string path,string? body)
        {
            if (method == "PATCH") { client["gameId"] = 456789; return Task.FromResult(new[] { "204", "" }); }
            return Task.FromResult(new[] { "200", client.ToJsonString() });
        }
        Require(!(await RemoteChampionHover.ApplyAsync(17,"123456",() => Task.FromResult("ChampSelect"),ChangedDraft,() => {},Delay)).Success, "A response from a changed draft must not confirm an old hover.");
        client = JsonNode.Parse(Planning)!;
        Task<string[]> MirrorOnly(string method,string path,string? body)
        {
            if (method == "PATCH") { client["myTeam"]![0]!["championPickIntent"] = 17; return Task.FromResult(new[] { "204", "" }); }
            return Task.FromResult(new[] { "200", client.ToJsonString() });
        }
        var mirror = await RemoteChampionHover.ApplyAsync(17,"123456",() => Task.FromResult("ChampSelect"),MirrorOnly,() => {},Delay);
        Require(mirror.Success && mirror.ConfirmedChampionSource == "PickIntent", "Clients that mirror the accepted declaration directly in myTeam remain supported.");
        client = JsonNode.Parse(Planning)!; client["timer"]!["phase"] = "";
        Task<string[]> SeparateTimer(string method, string path, string? body)
        {
            if (method == "GET" && path == RemoteChampionHover.SessionPath + "/timer") return Task.FromResult(new[] { "200", "{\"phase\":\"PLANNING\"}" });
            return Transport(method,path,body);
        }
        Require((await RemoteChampionHover.ApplyAsync(17,"123456",() => Task.FromResult("ChampSelect"),SeparateTimer,() => {},Delay)).Success,
            "A client exposing the phase through its timer endpoint must still support planning intent.");
        client = JsonNode.Parse(Planning)!; client["queueId"] = 9876;
        int beforeModeChecks = writes.Count;
        Require(!(await RemoteChampionHover.ApplyAsync(17,"123456",() => Task.FromResult("ChampSelect"),Transport,() => {},Delay, _ => "ARAM")).Success && writes.Count == beforeModeChecks,
            "The same cached client queue mode must protect random variants even when their ID is unfamiliar.");
        client["queueId"] = 1700; client["benchEnabled"] = true;
        Require((await RemoteChampionHover.ApplyAsync(17,"123456",() => Task.FromResult("ChampSelect"),Transport,() => {},Delay, _ => "CHERRY")).Success,
            "Arena must retain normal pick declaration even if a shared client capability flag resembles ARAM.");
        Console.WriteLine("Remote hover LCU transcript checks passed: inactive planning pick action, repeated changes, fresh read-back, ignored acknowledgement and stale/completed-action guards.");
    }
    private static void Require(bool value,string message) { if (!value) throw new Exception(message); }
}
