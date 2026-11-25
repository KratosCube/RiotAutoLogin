using System;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services;

internal enum RiotAuthState { Unknown, SignedOut, SignedIn, Verification }
internal sealed record RiotSessionSnapshot(RiotAuthState Auth, string Region = "", string GameName = "", string TagLine = "",
    string Phase = "", bool GameRunning = false, bool LeagueOpen = false)
{
    internal bool CanLeave => !GameRunning && (!LeagueOpen || Phase is "None" or "Lobby" or "EndOfGame");
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
        if (!current.CanLeave)
            return new(false, false, "Finish your game or leave matchmaking/champion select before switching accounts.");
        if (current.Auth == RiotAuthState.Verification)
            return new(false, false, "Complete or cancel the current Riot sign-in/verification, then choose the account again.");
        if (current.Auth == RiotAuthState.Unknown && current.LeagueOpen)
            return new(false, false, ManualLogout);

        bool changeServer = region.Length > 0 && target.Length > 0 && region != target;
        bool unknownServer = current.Auth == RiotAuthState.SignedIn && (region.Length == 0 || target.Length == 0);
        if (current.Auth == RiotAuthState.SignedIn)
        {
            progress?.Invoke("Signing out of the current Riot account…");
            if (!await logout(token).ConfigureAwait(false)) return new(false, false, ManualLogout);
            bool signedOut = false;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                token.ThrowIfCancellationRequested();
                var next = await read(token).ConfigureAwait(false);
                if (next.Auth == RiotAuthState.SignedOut) { signedOut = true; break; }
                await (pause?.Invoke(token) ?? Task.Delay(250, token)).ConfigureAwait(false);
            }
            if (!signedOut) return new(false, false, ManualLogout);
        }
        if (changeServer || unknownServer)
        {
            progress?.Invoke("Restarting Riot Client for the selected server…");
            if (!await restart(token).ConfigureAwait(false))
                return new(false, false, "Close Riot Client fully, then choose the account again. Its restart could not be confirmed.");
        }
        return new(true, false, "Ready to sign in.", current.Auth == RiotAuthState.Unknown);
    }
}
