using System;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services;

internal enum RiotAuthState { Unknown, SignedOut, SignedIn, Verification }
internal sealed record RiotSessionSnapshot(RiotAuthState Auth, string Region = "", string GameName = "", string TagLine = "",
    string Phase = "", bool GameRunning = false, bool LeagueOpen = false)
{
    internal bool CanRestart => !GameRunning && (CanLeave || Phase.Length == 0);
    internal bool CanLeave => !GameRunning && (!LeagueOpen || Phase is "None" or "Lobby" or "EndOfGame" or "PreEndOfGame" or "WaitingForStats");
}
internal sealed record RiotSwitchResult(bool CanLogin, bool AlreadySignedIn, string Message, bool UnverifiedSession = false);

internal sealed class RiotAccountSwitching(
    Func<CancellationToken, Task<RiotSessionSnapshot>> read,
    Func<CancellationToken, Task<bool>> logout,
    Func<CancellationToken, Task<bool>> restart,
    Func<CancellationToken, Task>? pause = null)
{
    internal const string ManualLogout = "Sign out in Riot Client first, then choose the account again. Automatic sign-out could not be confirmed.";

    internal async Task<RiotSwitchResult> PrepareAsync(string targetRegion, string gameName, string tag,
        Action<string>? progress, CancellationToken token)
    {
        var current = await read(token).ConfigureAwait(false);
        string region = RemotePlayerProfileLinks.RegionSlug(current.Region);
        string target = RemotePlayerProfileLinks.RegionSlug(targetRegion);
        if (current.Auth == RiotAuthState.SignedIn && region.Length > 0 && region == target &&
            !string.IsNullOrWhiteSpace(gameName) && !string.IsNullOrWhiteSpace(tag) &&
            string.Equals(current.GameName, gameName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(current.TagLine, tag, StringComparison.OrdinalIgnoreCase))
            return new(false, true, "This account is already signed in.");
        if (!current.CanRestart)
            return new(false, false, "Finish your game or leave matchmaking/champion select before switching accounts.");
        bool changeServer = region.Length > 0 && target.Length > 0 && region != target;
        bool unknownServer = current.Auth == RiotAuthState.SignedIn && (region.Length == 0 || target.Length == 0);
        bool needsRestart = !current.CanLeave || current.Auth is RiotAuthState.Unknown or RiotAuthState.Verification;
        if (current.CanLeave && current.Auth != RiotAuthState.SignedOut &&
            (current.Auth != RiotAuthState.Unknown || current.LeagueOpen))
        {
            progress?.Invoke("Signing out of the current Riot account…");
            bool signedOut = false;
            using var graceful = CancellationTokenSource.CreateLinkedTokenSource(token);
            graceful.CancelAfter(TimeSpan.FromSeconds(12));
            try
            {
                bool requested = await logout(graceful.Token).ConfigureAwait(false);
                for (int attempt = 0; requested && attempt < 12; attempt++)
                {
                    var next = await read(graceful.Token).ConfigureAwait(false);
                    if (next.Auth == RiotAuthState.SignedOut) { signedOut = true; break; }
                    await (pause?.Invoke(graceful.Token) ?? Task.Delay(250, graceful.Token)).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            needsRestart |= !signedOut;
        }
        if (changeServer || unknownServer || needsRestart)
        {
            progress?.Invoke("Closing the current clients and starting the selected account…");
            if (!await restart(token).ConfigureAwait(false))
                return new(false, false, "Close Riot Client fully, then choose the account again. Its restart could not be confirmed.");
        }
        return new(true, false, "Ready to sign in.", false);
    }
}
