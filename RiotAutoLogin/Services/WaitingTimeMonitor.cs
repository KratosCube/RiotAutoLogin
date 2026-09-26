using RiotAutoLogin.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services
{
    public sealed class WaitingTimeMonitor : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly GameflowTimeClassifier _classifier = new();
        private Task? _task;
        private WaitingTimeLedger? _ledger;
        private (DateTimeOffset utc, long tick, long lastTick, string account, RankedQueue? queue)? _pendingLoading;
        public string Status { get; private set; } = "Starting time tracking…";
        public string? PersistenceError => _ledger?.PersistenceError;
        public DateTimeOffset? FirstRecordedUtc => _ledger?.FirstRecordedUtc;

        public void Start()
        {
            CancellationToken token = _cts.Token;
            _task ??= Task.Run(() => MonitorAsync(token));
        }

        public WaitingTimeTotals GetTotals(RankedQueue? queue, bool todayOnly) =>
            _ledger?.GetTotals(queue, todayOnly, DateTime.Now) ?? default;

        private async Task MonitorAsync(CancellationToken token)
        {
            string account = "";
            string previousPhase = "None";
            long identityCheckedAt = 0, savedAt = 0;
            try
            {
                _ledger = new WaitingTimeLedger(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RiotClientAutoLogin", "waiting-times.json"));
                while (!token.IsCancellationRequested)
                {
                    int delay = 1000;
                    try
                    {
                        if (!LCUService.CheckIfLeagueClientIsOpen())
                        {
                            _ledger.Pause();
                            _classifier.Reset();
                            _pendingLoading = null;
                            account = "";
                            previousPhase = "None";
                            Status = "Waiting for League Client";
                            delay = 5000;
                        }
                        else
                        {
                            string? json = await LCUService.GetGameflowSessionAsync(token).ConfigureAwait(false);
                            if (json == null)
                            {
                                _ledger.Pause();
                                _pendingLoading = null;
                                Status = "Tracking paused · client unavailable";
                            }
                            else
                            {
                                var session = GameflowSnapshot.Parse(json);
                                long now = Environment.TickCount64;
                                if (account.Length == 0 || previousPhase != session.Phase || now - identityCheckedAt >= 15000)
                                {
                                    string[] identity = await LCUService.ClientRequestAsync("GET", "lol-summoner/v1/current-summoner", cancellationToken: token).ConfigureAwait(false);
                                    string nextAccount = "";
                                    if (identity[0] == "200")
                                    {
                                        using var doc = JsonDocument.Parse(identity[1]);
                                        if (doc.RootElement.TryGetProperty("puuid", out var id)) nextAccount = id.GetString() ?? "";
                                    }
                                    if (account != nextAccount)
                                    {
                                        _ledger.Pause();
                                        _classifier.Reset();
                                        _pendingLoading = null;
                                    }
                                    account = nextAccount;
                                    identityCheckedAt = now;
                                }
                                double? gameTime = null;
                                if (session.Phase is "GameStart" or "InProgress" && _classifier.NeedsGameClock)
                                    gameTime = await LiveGameClockService.ReadAsync(token).ConfigureAwait(false);
                                var activity = _classifier.Classify(session, gameTime);
                                if (account.Length == 0)
                                {
                                    _ledger.Pause();
                                    Status = "Tracking paused · waiting for your account";
                                }
                                else
                                {
                                    RecordObservation(account, session.Queue, activity, gameTime);
                                    Status = activity switch
                                    {
                                        TimedActivity.Queue => "Tracking · Queue (including ready check)",
                                        TimedActivity.ChampSelect => "Tracking · Champion select",
                                        TimedActivity.Loading => "Loading · duration saved when the game clock starts",
                                        TimedActivity.InGame => "Tracking · In game",
                                        _ => "Ready · waiting for your next queue"
                                    };
                                }
                                if (previousPhase != session.Phase || now - savedAt >= 30000)
                                {
                                    _ledger.Save();
                                    savedAt = now;
                                }
                                previousPhase = session.Phase;
                                if (activity is TimedActivity.None or TimedActivity.InGame) delay = 5000;
                            }
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                    catch (Exception ex)
                    {
                        _ledger.Pause();
                        _pendingLoading = null;
                        Status = "Tracking paused · waiting for client data";
                        Debug.WriteLine($"Time tracking: {ex.Message}");
                        delay = 5000;
                    }
                    await Task.Delay(delay, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Status = "Time history unavailable";
                Debug.WriteLine($"Time history: {ex.Message}");
            }
            finally { _ledger?.Save(); }
        }

        private void RecordObservation(string account, RankedQueue? queue, TimedActivity activity, double? gameTime)
        {
            DateTimeOffset utc = DateTimeOffset.UtcNow;
            long tick = Environment.TickCount64;
            if (_pendingLoading is { } previous &&
                (account != previous.account || queue != previous.queue || tick - previous.lastTick > 10000 ||
                 Math.Abs((utc - previous.utc).TotalSeconds - (tick - previous.tick) / 1000.0) >= 2))
                _pendingLoading = null;

            if (activity == TimedActivity.Loading)
            {
                // Keep loading provisional until the clock confirms when play
                // actually began. An inaccessible Live Client API must not turn
                // an entire match into a fabricated loading-screen statistic.
                var start = _pendingLoading ?? (utc, tick, tick, account, queue);
                _pendingLoading = (start.Item1, start.Item2, tick, account, queue);
                _ledger!.Observe(utc, tick, account, queue, TimedActivity.None);
                return;
            }
            if (_pendingLoading is { } pending && activity == TimedActivity.InGame && gameTime.HasValue)
                _ledger!.RecordConfirmedGameStart(pending.utc, utc, account, pending.queue, gameTime.Value);
            _pendingLoading = null;
            _ledger!.Observe(utc, tick, account, queue, activity);
        }

        public void Dispose()
        {
            _cts.Cancel();
            _ledger?.Save();
            if (_task != null) _ = _task.ContinueWith(_ => _cts.Dispose(), TaskScheduler.Default);
            else _cts.Dispose();
        }
    }
}
