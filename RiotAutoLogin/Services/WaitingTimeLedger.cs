using RiotAutoLogin.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RiotAutoLogin.Services
{
    public sealed class WaitingTimeDay
    {
        public string Day { get; set; } = "";
        public string AccountId { get; set; } = "";
        public RankedQueue? Queue { get; set; }
        public double QueueSeconds { get; set; }
        public double ChampSelectSeconds { get; set; }
        public double LoadingSeconds { get; set; }
        public double InGameSeconds { get; set; }
    }

    public sealed class WaitingTimeHistory
    {
        public int Version { get; set; } = 1;
        public DateTimeOffset? FirstRecordedUtc { get; set; }
        public List<WaitingTimeDay> Days { get; set; } = new();
    }

    public readonly record struct WaitingTimeTotals(double QueueSeconds, double ChampSelectSeconds, double LoadingSeconds, double InGameSeconds)
    {
        public double WaitingSeconds => QueueSeconds + ChampSelectSeconds + LoadingSeconds;
    }

    public sealed class WaitingTimeLedger
    {
        private readonly object _gate = new();
        private readonly string _path;
        private readonly WaitingTimeHistory _history;
        private readonly Dictionary<string, WaitingTimeDay> _days;
        private (DateTimeOffset utc, long tick, string account, RankedQueue? queue, TimedActivity activity)? _last;
        private bool _dirty;

        public DateTimeOffset? FirstRecordedUtc => _history.FirstRecordedUtc;
        public string? PersistenceError { get; private set; }

        public WaitingTimeLedger(string path)
        {
            _path = path;
            _history = new WaitingTimeHistory();
            try
            {
                if (File.Exists(path))
                    _history = JsonSerializer.Deserialize<WaitingTimeHistory>(File.ReadAllText(path)) ?? new();
                if (_history.Version != 1 || _history.Days == null)
                    throw new JsonException("Unsupported time history format.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Keep the original around if recovery is needed; never silently
                // replace a malformed history with a new empty file.
                PersistenceError = "Previous time history could not be loaded.";
                if (File.Exists(path)) File.Copy(path, path + ".recovery-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"), false);
                _history = new WaitingTimeHistory();
            }
            _days = _history.Days.ToDictionary(Key);
        }

        private static string Key(WaitingTimeDay day) => $"{day.Day}/{day.AccountId}/{day.Queue?.ToString() ?? "Other"}";

        public void Pause() { lock (_gate) _last = null; }

        public void Observe(DateTimeOffset utc, long monotonicMilliseconds, string account, RankedQueue? queue, TimedActivity activity)
        {
            lock (_gate)
            {
                if (_last is { } last && last.account == account && !string.IsNullOrEmpty(account))
                {
                    double seconds = (monotonicMilliseconds - last.tick) / 1000.0;
                    double wallSeconds = (utc - last.utc).TotalSeconds;
                    // Do not count sleep, an unavailable client, a stalled poll,
                    // clock corrections, or time spent outside this app's lifetime.
                    if (seconds > 0 && seconds <= 10 && Math.Abs(wallSeconds - seconds) < 2 && last.activity != TimedActivity.None)
                    {
                        var startDate = last.utc.LocalDateTime.Date;
                        var endDate = utc.LocalDateTime.Date;
                        if (startDate == endDate)
                            Add(last.utc, last.account, last.queue, last.activity, seconds);
                        else
                        {
                            var midnight = new DateTimeOffset(DateTime.SpecifyKind(endDate, DateTimeKind.Local));
                            double before = Math.Clamp((midnight - last.utc).TotalSeconds, 0, seconds);
                            Add(last.utc, last.account, last.queue, last.activity, before);
                            Add(utc, last.account, last.queue, last.activity, seconds - before);
                        }
                    }
                }
                _last = (utc, monotonicMilliseconds, account, queue, activity);
            }
        }

        private void Add(DateTimeOffset utc, string account, RankedQueue? queue, TimedActivity activity, double seconds)
        {
            if (seconds <= 0) return;
            var day = new WaitingTimeDay { Day = utc.LocalDateTime.ToString("yyyy-MM-dd"), AccountId = account, Queue = queue };
            string key = Key(day);
            if (!_days.TryGetValue(key, out var existing))
            {
                _days.Add(key, day);
                _history.Days.Add(day);
            }
            else day = existing;
            switch (activity)
            {
                case TimedActivity.Queue: day.QueueSeconds += seconds; break;
                case TimedActivity.ChampSelect: day.ChampSelectSeconds += seconds; break;
                case TimedActivity.Loading: day.LoadingSeconds += seconds; break;
                case TimedActivity.InGame: day.InGameSeconds += seconds; break;
            }
            _history.FirstRecordedUtc ??= utc;
            _dirty = true;
        }

        public void RecordConfirmedGameStart(DateTimeOffset launch, DateTimeOffset observed, string account, RankedQueue? queue, double gameTimeSeconds)
        {
            lock (_gate)
            {
                double elapsed = (observed - launch).TotalSeconds;
                if (elapsed <= 0 || elapsed > 3600 || !double.IsFinite(gameTimeSeconds) || gameTimeSeconds < 0) return;
                // If the first successful clock read is late, subtract already
                // elapsed gameplay rather than calling all of it loading time.
                double inGame = Math.Min(elapsed, gameTimeSeconds);
                AddSpan(launch, account, queue, TimedActivity.Loading, elapsed - inGame);
                AddSpan(observed.AddSeconds(-inGame), account, queue, TimedActivity.InGame, inGame);
            }
        }

        private void AddSpan(DateTimeOffset start, string account, RankedQueue? queue, TimedActivity activity, double seconds)
        {
            while (seconds > 0)
            {
                var midnight = new DateTimeOffset(DateTime.SpecifyKind(start.LocalDateTime.Date.AddDays(1), DateTimeKind.Local));
                double part = Math.Min(seconds, (midnight - start).TotalSeconds);
                if (part <= 0) break;
                Add(start, account, queue, activity, part);
                start = start.AddSeconds(part);
                seconds -= part;
            }
        }

        public WaitingTimeTotals GetTotals(RankedQueue? queue, bool todayOnly, DateTime today)
        {
            lock (_gate)
            {
                string date = today.ToString("yyyy-MM-dd");
                var days = _history.Days.Where(d => (!queue.HasValue || d.Queue == queue) && (!todayOnly || d.Day == date)).ToArray();
                return new(days.Sum(d => d.QueueSeconds), days.Sum(d => d.ChampSelectSeconds), days.Sum(d => d.LoadingSeconds), days.Sum(d => d.InGameSeconds));
            }
        }

        public void Save()
        {
            lock (_gate)
            {
                if (!_dirty) return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    string temporary = _path + ".tmp";
                    File.WriteAllText(temporary, JsonSerializer.Serialize(_history));
                    File.Move(temporary, _path, true);
                    _dirty = false;
                    PersistenceError = null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    PersistenceError = "Time history could not be saved. Check the application data folder.";
                }
            }
        }
    }
}
