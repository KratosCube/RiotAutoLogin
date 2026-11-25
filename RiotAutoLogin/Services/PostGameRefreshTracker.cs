using RiotAutoLogin.Models;

namespace RiotAutoLogin.Services;

internal sealed record StatsPlayer(string GameName, string TagLine, string Region, string LocalPuuid)
{
    internal bool Matches(Account account) => account.MatchesRiotId(GameName, TagLine, account.Region) &&
        RemotePlayerProfileLinks.RegionSlug(Region).Length > 0 &&
        RemotePlayerProfileLinks.RegionSlug(Region) == RemotePlayerProfileLinks.RegionSlug(account.Region);
}
internal sealed record FinishedStatsGame(long GameId, RankedQueue? Queue, StatsPlayer Player);

// Capture the player during the game: switching clients afterwards must never update the wrong card.
internal sealed class PostGameRefreshTracker
{
    private FinishedStatsGame? _active;
    private RankedQueue? _queue;
    private (long Id, string Player, string Region) _completed;
    internal bool NeedsIdentity(long gameId) => _active == null || _active.GameId != gameId;
    internal void Disconnect() { _active = null; _queue = null; }

    internal FinishedStatsGame? Observe(GameflowSnapshot snapshot, StatsPlayer? player = null)
    {
        if (snapshot.IsSpectator) { Disconnect(); return null; }
        if (snapshot.Queue.HasValue) _queue = snapshot.Queue;
        if (snapshot.Phase is "GameStart" or "InProgress" or "Reconnect")
        {
            if (snapshot.GameId <= 0) return null;
            if (player != null && player.LocalPuuid.Length > 0)
                _active = new(snapshot.GameId, snapshot.Queue ?? _queue, player);
            else if (_active != null && _active.GameId != snapshot.GameId) _active = null;
            return null;
        }
        if (snapshot.Phase is not ("WaitingForStats" or "PreEndOfGame" or "EndOfGame" or "Lobby" or "None")) return null;
        var finished = _active;
        Disconnect();
        if (finished == null || _completed == (finished.GameId, finished.Player.LocalPuuid, finished.Player.Region)) return null;
        _completed = (finished.GameId, finished.Player.LocalPuuid, finished.Player.Region);
        return finished;
    }
}
