using RiotAutoLogin.Services;

internal static class RiotSwitchChecks
{
    internal static async Task Run()
    {
        async Task<(RiotSwitchResult Result, int Logouts, int Restarts)> Switch(RiotSessionSnapshot initial,
            string target = "euw1", bool logoutWorks = true, bool verifies = true, bool restartWorks = true,
            string name = "Other", string tag = "TAG")
        {
            int logouts = 0, restarts = 0;
            var policy = new RiotAccountSwitching(token =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(logouts > 0 && verifies ? initial with { Auth = RiotAuthState.SignedOut } : initial);
            }, token => { token.ThrowIfCancellationRequested(); logouts++; return Task.FromResult(logoutWorks); },
               token => { token.ThrowIfCancellationRequested(); restarts++; return Task.FromResult(restartWorks); },
               _ => Task.CompletedTask);
            var result = await policy.PrepareAsync(target, name, tag, null, default);
            return (result, logouts, restarts);
        }
        var signedIn = new RiotSessionSnapshot(RiotAuthState.SignedIn, "EUW", "Current", "TAG", "Lobby", false, true);
        var same = await Switch(signedIn);
        Require(same.Result.CanLogin && same.Logouts == 1 && same.Restarts == 0, "Same-region switching must sign out once and reuse the client.");
        var other = await Switch(signedIn, "EUN1");
        Require(other.Result.CanLogin && other.Logouts == 1 && other.Restarts == 1, "Cross-region switching must confirm sign-out before restarting.");
        var already = await Switch(signedIn, name: "Current");
        Require(already.Result.AlreadySignedIn && already.Logouts == 0 && already.Restarts == 0, "Selecting the signed-in Riot ID must leave its session alone.");
        var rejected = await Switch(signedIn, logoutWorks: false);
        Require(rejected.Result.CanLogin && rejected.Logouts == 1 && rejected.Restarts == 1,
            "A failed logout must restart both clients before credentials.");
        var unverified = await Switch(signedIn, "eun1", verifies: false);
        Require(unverified.Result.CanLogin && unverified.Restarts == 1, "Unconfirmed logout must fall back to a complete restart.");
        var failedRestart = await Switch(signedIn, "eun1", restartWorks: false);
        Require(!failedRestart.Result.CanLogin, "An unconfirmed server restart must stop the sign-in.");
        foreach (string phase in new[] { "ChampSelect", "Matchmaking", "ReadyCheck", "InProgress", "Reconnect" })
        {
            var blocked = await Switch(signedIn with { Phase = phase });
            Require(!blocked.Result.CanLogin && blocked.Logouts == 0 && blocked.Restarts == 0, "Active or unknown League phases must not be interrupted.");
        }
        var game = await Switch(signedIn with { GameRunning = true });
        Require(!game.Result.CanLogin && game.Logouts == 0, "A running game must block switching even if the lobby snapshot is stale.");
        var unknown = await Switch(new(RiotAuthState.Unknown, LeagueOpen: true));
        Require(unknown.Result.CanLogin && unknown.Logouts == 1 && unknown.Restarts == 1, "An unsupported Riot auth API must try LCU logout before restarting the existing clients.");
        var unknownForm = await Switch(new(RiotAuthState.Unknown));
        Require(unknownForm.Result.CanLogin && unknownForm.Restarts == 1 && !unknownForm.Result.UnverifiedSession, "Unknown standalone sessions must be closed before a fresh sign-in.");
        var verification = await Switch(new(RiotAuthState.Verification));
        Require(verification.Result.CanLogin && verification.Restarts == 1, "Selecting another account during verification must restart that sign-in session.");
        var restartDenied = await Switch(signedIn, logoutWorks: false, restartWorks: false);
        Require(!restartDenied.Result.CanLogin, "Failed process shutdown must block credential entry.");
        foreach (string phase in new[] { "PreEndOfGame", "WaitingForStats" })
        {
            var postGame = await Switch(signedIn with { Phase = phase });
            Require(postGame.Result.CanLogin, "A stuck post-game client may be switched after the game process has exited.");
            var stillRunning = await Switch(signedIn with { Phase = phase, GameRunning = true });
            Require(!stillRunning.Result.CanLogin && stillRunning.Restarts == 0, "A post-game phase alone must not override the running-game guard.");
        }
        var noPhase = await Switch(signedIn with { Phase = "" });
        Require(noPhase.Result.CanLogin && noPhase.Restarts == 1, "An unavailable phase API may restart a client while no game process is running.");
        var signedOut = await Switch(new(RiotAuthState.SignedOut));
        Require(signedOut.Result.CanLogin && signedOut.Logouts == 0 && signedOut.Restarts == 0, "Fresh sign-ins must keep the normal launch path.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var cancelPolicy = new RiotAccountSwitching(token => { token.ThrowIfCancellationRequested(); return Task.FromResult(signedIn); },
            _ => Task.FromResult(true), _ => Task.FromResult(true));
        try { await cancelPolicy.PrepareAsync("eun1", "Other", "TAG", null, cancelled.Token); throw new Exception("Cancelled switch continued."); }
        catch (OperationCanceledException) { }

        var auth = RiotLocalProtocol.Connection("LeagueClientUx.exe --app-port=5000 --remoting-auth-token=lcu-only --riotclient-app-port=6000 --riotclient-auth-token=riot-token", true);
        Require(RiotLocalProtocol.Connection("\"--riotclient-app-port=6000\" \"--riotclient-auth-token=riot-token\"", true) is { Port: 6000, Token: "riot-token" },
            "Whole quoted command-line arguments must discover the Riot API.");
        Require(auth is { Port: 6000, Token: "riot-token" }, "LCU ports and credentials must never be mistaken for the Riot Client API.");
        Require(RiotLocalProtocol.Connection("--app-port=5000 --remoting-auth-token=lcu-only", true) == null, "LCU-only process credentials are insufficient.");
        Require(RiotLocalProtocol.Connection("--app-port=\"12345\" --remoting-auth-token=\"quoted-token\"", false)?.Port == 12345, "Quoted Riot flags must parse.");
        Require(RiotLocalProtocol.Connection("--app-port=65536 --remoting-auth-token=test", false) == null, "Invalid loopback ports must be rejected.");
        Require(!auth!.ToString().Contains("riot-token"), "Diagnostic text must not expose the local API token.");
        Require(RiotLocalProtocol.Auth("200", """{"type":"authenticated"}""") == RiotAuthState.SignedIn &&
            RiotLocalProtocol.Auth("200", """{"type":"auth"}""") == RiotAuthState.SignedOut &&
            RiotLocalProtocol.Auth("200", """{"type":"multifactor"}""") == RiotAuthState.Verification &&
            RiotLocalProtocol.Auth("404", "{}") == RiotAuthState.Unknown && RiotLocalProtocol.Auth("200", "not-json") == RiotAuthState.Unknown,
            "Only explicit session states can confirm sign-in or sign-out.");
        Console.WriteLine("Account switching, logout verification, game guards, region restart and local auth protocol checks passed.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
