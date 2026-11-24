using System;

namespace RiotAutoLogin.Services
{
    public readonly record struct RemotePhaseObservation(string Phase, double ElapsedSeconds);

    // LCU does not supply a remaining time for a custom lobby. Track elapsed time
    // from the first phase observation with a monotonic clock instead.
    public sealed class RemotePhaseClock
    {
        private readonly object _gate = new();
        private string _phase = string.Empty;
        private long _startedAtMs;
        private long _lastSeenAtMs;

        public RemotePhaseObservation Observe(string phase, long nowMs)
        {
            lock (_gate)
            {
                // A single failed gameflow read should not restart a running lobby clock.
                if (phase == "None" && (_phase is "Lobby" or "Matchmaking") &&
                    nowMs >= _lastSeenAtMs && nowMs - _lastSeenAtMs < 5000)
                    return new RemotePhaseObservation(_phase, Math.Max(0, (nowMs - _startedAtMs) / 1000.0));

                if (_phase != phase || nowMs < _startedAtMs)
                {
                    _phase = phase;
                    _startedAtMs = nowMs;
                }
                _lastSeenAtMs = nowMs;

                return new RemotePhaseObservation(phase, phase is "Lobby" or "Matchmaking"
                    ? Math.Max(0, (nowMs - _startedAtMs) / 1000.0) : -1);
            }
        }
    }
}
