using System;

namespace RiotAutoLogin.Services
{
    // Session reads can repeat the same timer snapshot. Anchor it to the client
    // timestamp on the PC, or keep the first local deadline for an undated sample.
    public sealed class RemotePhaseClock
    {
        private readonly object _gate = new();
        private string _turn = string.Empty;
        private int _lastRemaining = -1;
        private long _deadline;
        private long _lastRead;

        public int Remaining(string turn, int remainingMs, long clientEpochMs, long utcMs, long tickMs)
        {
            if (remainingMs < 0) return -1;
            long age = clientEpochMs > 0 && clientEpochMs <= utcMs ? utcMs - clientEpochMs : 0;
            long deadline = tickMs + Math.Max(0L, remainingMs - age);
            lock (_gate)
            {
                if (tickMs < _lastRead)
                    return (int)(deadline - tickMs);
                if (turn == _turn && remainingMs == _lastRemaining)
                    deadline = Math.Min(deadline, _deadline);
                _turn = turn;
                _lastRemaining = remainingMs;
                _deadline = deadline;
                _lastRead = tickMs;
                return (int)Math.Max(0L, deadline - tickMs);
            }
        }

        public void Reset(long tickMs)
        {
            lock (_gate)
            {
                if (tickMs < _lastRead) return;
                _lastRead = tickMs;
                _lastRemaining = -1;
                _turn = string.Empty;
            }
        }
    }
}
