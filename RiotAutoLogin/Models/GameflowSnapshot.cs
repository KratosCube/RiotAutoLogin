using System.Text.Json;

namespace RiotAutoLogin.Models
{
    public sealed record GameflowSnapshot(string Phase, RankedQueue? Queue, long GameId, bool IsSpectator)
    {
        public static GameflowSnapshot Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string phase = root.TryGetProperty("phase", out var p) ? p.GetString() ?? "None" : "None";
            RankedQueue? queue = null;
            long gameId = 0;
            bool spectator = false;
            if (root.TryGetProperty("gameData", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("gameId", out var id)) long.TryParse(id.ToString(), out gameId);
                spectator = data.TryGetProperty("isSpectator", out var s) && s.ValueKind == JsonValueKind.True;
                if (data.TryGetProperty("queue", out var q) && q.ValueKind == JsonValueKind.Object)
                {
                    if (q.TryGetProperty("type", out var type)) queue = RankedQueues.FromType(type.GetString());
                    if (queue == null && q.TryGetProperty("id", out var queueId) && queueId.TryGetInt32(out int number)) queue = RankedQueues.FromId(number);
                }
            }
            return new GameflowSnapshot(phase, queue, gameId, spectator);
        }
    }

    public enum TimedActivity { None, Queue, ChampSelect, Loading, InGame }

    // The LCU's InProgress phase includes loading. A positive Live Client game
    // clock confirms play; a missing endpoint on a mid-game attach proves nothing.
    public sealed class GameflowTimeClassifier
    {
        private string _previousPhase = "None";
        private long _gameId;
        private bool _observedLaunch;
        private bool _gameStarted;

        public bool NeedsGameClock => !_gameStarted;

        public TimedActivity Classify(GameflowSnapshot snapshot, double? gameTime)
        {
            bool active = snapshot.Phase is "GameStart" or "InProgress" or "Reconnect";
            if (!active || (snapshot.GameId != 0 && _gameId != 0 && snapshot.GameId != _gameId))
            {
                _observedLaunch = false;
                _gameStarted = false;
            }
            if (active && (_previousPhase == "ChampSelect" || snapshot.Phase == "GameStart")) _observedLaunch = true;
            if (active && gameTime > 0) _gameStarted = true;
            if (snapshot.GameId != 0) _gameId = snapshot.GameId;
            _previousPhase = snapshot.Phase;

            if (snapshot.IsSpectator) return TimedActivity.None;
            return snapshot.Phase switch
            {
                "Matchmaking" or "ReadyCheck" => TimedActivity.Queue,
                "ChampSelect" => TimedActivity.ChampSelect,
                "GameStart" or "InProgress" when _gameStarted => TimedActivity.InGame,
                "GameStart" or "InProgress" when _observedLaunch => TimedActivity.Loading,
                _ => TimedActivity.None
            };
        }

        public void Reset()
        {
            _previousPhase = "None";
            _gameId = 0;
            _observedLaunch = _gameStarted = false;
        }
    }
}
