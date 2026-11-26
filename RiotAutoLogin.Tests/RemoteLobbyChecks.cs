using System.Text.Json;
using RiotAutoLogin.Models;
using RiotAutoLogin.Services;

internal static class RemoteLobbyChecks
{
    internal static async Task Run()
    {
        using var lobby = JsonDocument.Parse("""{"gameConfig":{"queueId":400},"localMember":{"puuid":"self","isLeader":true,"allowedChangeActivity":true},"members":[{"puuid":"self","gameName":"My Name","tagLine":"EUW","isLeader":true},{"puuid":"friend","summonerName":"A Friend","firstPositionPreference":"MIDDLE"}]}""");
        var members = RemoteLobbyData.Members(lobby.RootElement);
        Require(members.Count == 2 && members[0].IsLocalPlayer && members[0].IsLeader && members[1].PlayerName == "A Friend", "Lobby members and the local leader must be visible before draft.");
        Require(RemoteLobbyData.CanChange(lobby.RootElement), "An eligible lobby leader may change queues.");
        using var member = JsonDocument.Parse("""{"localMember":{"isLeader":false,"allowedChangeActivity":false},"gameConfig":{"queueId":420}}""");
        Require(!RemoteLobbyData.CanChange(member.RootElement), "A regular member cannot change the party queue.");
        using var custom = JsonDocument.Parse("""{"localMember":{"isLeader":true},"gameConfig":{"isCustom":true}}""");
        Require(!RemoteLobbyData.CanChange(custom.RootElement), "A custom lobby must not silently become a matchmade lobby.");
        using var catalog = JsonDocument.Parse("""[{"id":420,"gameMode":"CLASSIC","queueAvailability":"Available","isEnabled":true,"allowablePremadeSizes":[1,2]},{"id":400,"gameMode":"CLASSIC","queueAvailability":"Available","allowablePremadeSizes":[1,2,3,4,5]},{"id":9876,"name":"Arena","gameMode":"CHERRY","queueAvailability":"PlatformDisabled"},{"id":450,"gameMode":"ARAM","queueAvailability":"Available"},{"id":2400,"name":"ARAM Mayhem","gameMode":"KIWI","queueAvailability":"Available"}]""");
        var options = RemoteLobbyData.Queues(catalog.RootElement, 3);
        Require(!options.Single(q => q.Id == 420).IsAvailable && options.Single(q => q.Id == 400).IsAvailable && !options.Single(q => q.Id == 9876).IsAvailable && options.Single(q => q.Id == 2400).IsAvailable,
            "Catalog availability, actual Arena IDs and party sizes must control queue selection.");

        using var cards = JsonDocument.Parse("""{"localPlayerCellId":0,"allowSubsetChampionPicks":true,"benchEnabled":true,"allowRerolling":false,"rerollsRemaining":0,"benchChampions":[{"championId":22},{"championId":22},{"championId":-1}],"actions":[[{"actorCellId":0,"type":"pick","completed":false}]]}""");
        var state = new RemotePickState { IsInChampSelect = true, IsRandomChampionMode = true, SelectedChampionId = 1, PickActionId = "10" };
        RemoteRandomSelection.Read(cards.RootElement, state, new[] { 1, 2, -1 });
        Require(state.CanPick && state.CardChampionIds.SequenceEqual([1, 2]) && state.BenchChampionIds.SequenceEqual([22]) && !state.CanReroll,
            "ARAM cards must use the client's actual subset and expose no fictional reroll.");
        Require(RemoteRandomSelection.CanTakeBench(state, 22) && !RemoteRandomSelection.CanTakeBench(state, 999), "Only a currently available bench champion may be taken.");
        using var complete = JsonDocument.Parse(cards.RootElement.GetRawText().Replace("\"completed\":false", "\"completed\":true"));
        RemoteRandomSelection.Read(complete.RootElement, state, new[] { 1, 2 });
        Require(!state.CanPick, "A completed card choice must not show another lock-in action.");
        Require(RemoteRandomSelection.IsRandom(cards.RootElement, 450, "ARAM") && !RemoteRandomSelection.IsRandom(cards.RootElement, 1700, "CHERRY"),
            "Arena is not ARAM just because the client exposes a subset flag.");
        Require(RemoteRandomSelection.IsRandom(cards.RootElement, 2400, "KIWI"), "Mayhem must use random selection despite its distinct game mode.");
        Require(new RemoteChampionDto { Id = 1, IsOwnedOrFree = false }.IsDisabled, "Disabled champion serialization must be a pure ownership check.");

        long tick = 0; int reads = 0;
        var cache = new ClientReadCache(() => tick);
        var scope = new object();
        var pending = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string[]> Load() { reads++; return pending.Task; }
        var one = cache.ReadAsync(scope, "session", 150, Load, default);
        var two = cache.ReadAsync(scope, "session", 150, Load, default);
        pending.SetResult(["200", "snapshot"]);
        await Task.WhenAll(one, two);
        Require(reads == 1, "Concurrent live reads must share a single client request.");
        await cache.ReadAsync(scope, "session", 150, Load, default);
        Require(reads == 1, "A still-fresh live snapshot must not be read again.");
        tick = 151;
        await cache.ReadAsync(scope, "session", 150, () => { reads++; return Task.FromResult(new[] { "200", "new" }); }, default);
        Require(reads == 2, "Expired snapshots must be refreshed.");
        cache.Clear();
        await cache.ReadAsync(scope, "session", 150, () => { reads++; return Task.FromResult(new[] { "200", "mutated" }); }, default);
        await cache.ReadAsync(new object(), "session", 150, () => { reads++; return Task.FromResult(new[] { "200", "other client" }); }, default);
        Require(reads == 4, "Mutations and a different client must not reuse the old snapshot.");
        var cancelled = new CancellationToken(true);
        try { await cache.ReadAsync(scope, "cancelled", 150, Load, cancelled); throw new Exception("Cancellation ignored."); }
        catch (OperationCanceledException) { }
        var oldReply = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldRead = cache.ReadAsync(scope, "pending", 150, () => oldReply.Task, default);
        cache.Clear();
        await cache.ReadAsync(scope, "pending", 150, () => Task.FromResult(new[] { "200", "after mutation" }), default);
        oldReply.SetResult(["200", "before mutation"]); await oldRead;
        var latest = await cache.ReadAsync(scope, "pending", 150, () => throw new Exception("Fresh result was lost."), default);
        Require(latest[1] == "after mutation", "An old pending request must never refill the cache after mutation.");
        int failures = 0;
        Task<string[]> FailingRead() { failures++; return Task.FromResult(new[] { "503", "unavailable" }); }
        await cache.ReadAsync(scope, "failed", 150, FailingRead, default);
        await cache.ReadAsync(scope, "failed", 150, FailingRead, default);
        Require(failures == 2, "Transient client failures must not be cached.");
        Console.WriteLine("Remote lobby members, authoritative queues, ARAM cards/bench and coalesced live-read checks passed.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
