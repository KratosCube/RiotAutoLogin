using System.Text.Json;
using RiotAutoLogin.Models;
using RiotAutoLogin.Services;

internal static class RemoteInteractionChecks
{
    internal static async Task Run()
    {
        using var session = JsonDocument.Parse("""{"gameId":123456789,"localPlayerCellId":0,"myTeam":[{"cellId":0},{"cellId":2},{"cellId":6}],"positionSwaps":[{"id":12,"cellId":2,"state":"AVAILABLE"},{"id":13,"cellId":6,"state":"RECEIVED"},{"id":14,"cellId":0,"state":"AVAILABLE"},{"id":15,"cellId":9,"state":"RECEIVED"},{"id":"../x","cellId":2,"state":"AVAILABLE"}]}""");
        Require(RemoteDraftInteractions.DraftId(session.RootElement) == "123456789", "Numeric game identity must survive serialization.");
        var swaps = RemoteDraftInteractions.ReadSwaps(session.RootElement, "positionSwaps", "position");
        Require(swaps.Count == 2 && swaps[0].Id == "12" && RemoteDraftInteractions.Allows(swaps[0], "request") && !RemoteDraftInteractions.Allows(swaps[0], "accept"), "Only a current, available teammate contract may be requested.");
        Require(RemoteDraftInteractions.Allows(swaps[1], "accept") && RemoteDraftInteractions.Allows(swaps[1], "decline") && !RemoteDraftInteractions.Allows(swaps[1], "cancel"), "Incoming swaps cannot be treated as outgoing requests.");
        Require(RemoteDraftInteractions.Allows(new() { State = "SENT" }, "cancel") && !RemoteDraftInteractions.Allows(new() { State = "ACCEPTED" }, "request"), "Only an outgoing pending request may be cancelled.");
        Require(!RemoteDraftInteractions.ValidId("-1") && !RemoteDraftInteractions.ValidId("１２") && !RemoteDraftInteractions.ValidId("9999999999999999999"), "Invalid contract IDs must never enter client paths.");
        using var skins = JsonDocument.Parse("""[{"id":1000,"championId":1,"name":"Annie","splashPath":"/lol-game-data/assets/ASSETS/Characters/Annie/Skins/Base/Images/annie.jpg"},{"id":1001,"championId":1,"name":"Gothic Annie"},{"id":1002,"championId":1,"name":"Locked skin"},{"id":1003,"championId":1,"disabled":true},{"id":2001,"championId":2,"name":"Another champion"}]""");
        using var pickable = JsonDocument.Parse("[1000,1001,1003,2001]");
        var gallery = RemoteSkinData.Read(skins.RootElement, pickable.RootElement, 1, 1000);
        Require(gallery.Count == 3 && gallery.All(s => s.Id != 1002 && s.Id != 2001) && !gallery.Single(s => s.Id == 1003).IsAvailable, "Only actual client-pickable skins for the current champion may be offered.");
        Require(gallery[0].ImageUrl.EndsWith("assets/characters/annie/skins/base/images/annie.jpg"), "Client asset paths must map to the expected image.");
        Require(RemoteSkinData.Read(skins.RootElement, default, 1, 1000).Count == 0, "Unknown pickable state must not expose a guessed selection.");
        foreach (string path in new[] { "https://untrusted.example/x.jpg", "/lol-game-data/assets/../secret.jpg", "/lol-game-data/assets/a.svg", "/lol-game-data/assets/%2e%2e/a.jpg", "/lol-game-data/assets/a.jpg?x=1" })
            Require(RemoteSkinData.AssetUrl(path) == "", "Only safe image asset paths may reach the browser.");

        var posts = new List<string>(); int probes = 0;
        Task<string[]> Post(string endpoint) { posts.Add(endpoint); return Task.FromResult(new[] { "204", "" }); }
        Task Delay(int _) => Task.CompletedTask;
        Task<DraftExitProbe> UiOnly() => Task.FromResult(++probes == 1 ? new DraftExitProbe("ChampSelect", "match", true, true) : new("None", "", false, true));
        var uiReset = await RemoteDraftExit.RunAsync("match", UiOnly, Post, Delay, false);
        Require(!uiReset.Success && posts.Count == 1 && posts[0].Contains("quitV2") && !posts[0].Contains("session/dodge"), "A local UI reset with a live server draft must never be reported as dodge.");
        probes = 0; posts.Clear();
        Task<DraftExitProbe> Left() => Task.FromResult(++probes == 1 ? new DraftExitProbe("ChampSelect", "match", true, true) : new("Lobby", "", false, false));
        Require((await RemoteDraftExit.RunAsync("match", Left, Post, Delay, false)).Success && probes == 9, "Dodge needs two seconds of consecutive confirmed departure from client and server.");
        probes = 0; posts.Clear();
        Task<DraftExitProbe> Started() => Task.FromResult(++probes == 1 ? new DraftExitProbe("ChampSelect", "match", true, true) : new("GameStart", "match", true, false));
        Require(!(await RemoteDraftExit.RunAsync("match", Started, Post, Delay, false)).Success && posts.Count == 1, "Launching a game is not successful dodge and must not trigger another quit.");
        posts.Clear();
        Require(!(await RemoteDraftExit.RunAsync("old", () => Task.FromResult(new DraftExitProbe("ChampSelect", "new", true, true)), Post, Delay, false)).Success && posts.Count == 0, "A changed draft must not be dodged from an old confirmation.");
        probes = 0; posts.Clear();
        Task<DraftExitProbe> Unknown() => Task.FromResult(++probes == 1 ? new DraftExitProbe("ChampSelect", "match", true, true) : new("None", "", false, null));
        Require(!(await RemoteDraftExit.RunAsync("match", Unknown, Post, Delay, false)).Success, "A failed server read cannot prove that the server draft ended.");
        probes = 0; posts.Clear();
        Task<DraftExitProbe> FallbackProbe() => Task.FromResult(++probes <= 2 ? new DraftExitProbe("ChampSelect", "match", true, true) : new("Lobby", "", false, false));
        Task<string[]> Unsupported(string path) { posts.Add(path); return Task.FromResult(new[] { posts.Count == 1 ? "404" : "204", "" }); }
        Require((await RemoteDraftExit.RunAsync("match", FallbackProbe, Unsupported, Delay, false)).Success && posts.Count == 2 && posts[1].EndsWith("/session/quit"), "An unsupported RPC may use the server-backed quit only after rechecking the same draft.");
        probes = 0; posts.Clear();
        Task<string[]> Rejected(string path) { posts.Add(path); return Task.FromResult(new[] { "403", "rejected" }); }
        Require(!(await RemoteDraftExit.RunAsync("match", () => Task.FromResult(new DraftExitProbe("ChampSelect", "match", true, true)), Rejected, Delay, false)).Success && posts.Count == 1, "A rejected quit must not fall back to a local gameflow notification.");
        Console.WriteLine("Remote skin ownership/assets, role/order swap contracts and server-confirmed dodge checks passed.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
