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
        private readonly GameflowQueueTracker _queueTracker = new();
        private readonly LoadingAttemptTracker _loadingAttempt = new();
        private Task? _task;
        private WaitingTimeLedger? _ledger;
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
            long identityCheckedAt = 0, lastIdentityConfirmedAt = long.MinValue, savedAt = 0;
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
                            _queueTracker.Reset();
                            _loadingAttempt.Reset();
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
                                // LCU can briefly disappear as the game launches.
                                // The pending attempt expires on a long gap or a
                                // different account/game once data returns.
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
                                        lastIdentityConfirmedAt = now;
                                    }
                                    else if (identity[0] != "404" && account.Length > 0 &&
                                             session.Phase is "GameStart" or "InProgress" or "Reconnect" &&
                                             now - lastIdentityConfirmedAt < 30000)
                                        nextAccount = account;
                                    if (account != nextAccount)
                                    {
                                        _ledger.Pause();
                                        _classifier.Reset();
                                        _queueTracker.Reset();
                                        _loadingAttempt.Reset();
                                    }
                                    account = nextAccount;
                                    identityCheckedAt = identity[0] == "200" ? now : now - 14000;
                                }
                                RankedQueue? queue = _queueTracker.Observe(session);
                                bool? gameStartedEvent = null;
                                if (session.Phase is "GameStart" or "InProgress" && _classifier.NeedsGameStartEvent)
                                    gameStartedEvent = await LiveGameStartService.ReadAsync(token).ConfigureAwait(false);
                                var activity = _classifier.Classify(session, gameStartedEvent);
                                if (account.Length == 0)
                                {
                                    _ledger.Pause();
                                    _classifier.Reset();
                                    _queueTracker.Reset();
                                    _loadingAttempt.Reset();
                                    Status = "Tracking paused · waiting for your account";
                                }
                                else
                                {
                                    if (activity == TimedActivity.Loading && gameStartedEvent is null && _loadingAttempt.HasPending)
                                    {
                                        // Keep the first launch observation, but do not extend an
                                        // unverified loading attempt while the API is unavailable.
                                        _ledger.Pause();
                                        Status = "Loading · waiting for game start signal";
                                    }
                                    else
                                    {
                                        RecordObservation(account, queue, session.GameId, activity);
                                        Status = activity switch
                                        {
                                            TimedActivity.Queue => "Tracking · Queue (including ready check)",
                                            TimedActivity.ChampSelect => "Tracking · Champion select",
                                            TimedActivity.Loading => "Loading · waiting for the game to start",
                                            TimedActivity.InGame => "Tracking · In game",
                                            _ => "Ready · waiting for your next queue"
                                        };
                                    }
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

        private void RecordObservation(string account, RankedQueue? queue, long gameId,
            TimedActivity activity)
        {
            DateTimeOffset utc = DateTimeOffset.UtcNow;
            long tick = Environment.TickCount64;
            var confirmed = _loadingAttempt.Observe(utc, tick, account, queue, gameId, activity);
            if (activity == TimedActivity.Loading)
            {
                // Keep loading provisional until the GameStart event confirms play.
                // An inaccessible Live Client API cannot confirm a boundary.
                _ledger!.Observe(utc, tick, account, queue, TimedActivity.None);
                return;
            }
            if (confirmed is { } pending)
                _ledger!.RecordConfirmedGameStart(pending.StartedUtc, utc, account, pending.Queue);
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
