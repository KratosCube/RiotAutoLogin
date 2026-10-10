using System;

namespace RiotAutoLogin.Models
{
    public readonly record struct LoadingAttempt(
        DateTimeOffset StartedUtc, long StartedTick, long LastTick,
        string Account, RankedQueue? Queue, long GameId);

    // Loading is provisional until the live game clock confirms when play began.
    // Brief LCU outages during launch are common, so a missing poll alone does
    // not discard the attempt. A long gap, a clock jump or a new game does.
    public sealed class LoadingAttemptTracker
    {
        private LoadingAttempt? _pending;

        public void Reset() => _pending = null;

        public LoadingAttempt? Observe(DateTimeOffset utc, long tick, string account,
            RankedQueue? queue, long gameId, TimedActivity activity, double? gameTime)
        {
            if (_pending is { } previous &&
                (previous.Account != account ||
                 (gameId != 0 && previous.GameId != 0 && gameId != previous.GameId) ||
                 (queue.HasValue && previous.Queue.HasValue && queue != previous.Queue) ||
                 tick < previous.LastTick || tick - previous.LastTick > 60000 ||
                 Math.Abs((utc - previous.StartedUtc).TotalSeconds -
                     (tick - previous.StartedTick) / 1000.0) >= 2))
                _pending = null;

            if (activity == TimedActivity.Loading)
            {
                var start = _pending ?? new LoadingAttempt(utc, tick, tick, account, queue, gameId);
                _pending = start with
                {
                    LastTick = tick,
                    Queue = queue ?? start.Queue,
                    GameId = gameId != 0 ? gameId : start.GameId
                };
                return null;
            }

            var confirmed = activity == TimedActivity.InGame && gameTime > 0 ? _pending : null;
            _pending = null;
            return confirmed;
        }
    }
}
