using RiotAutoLogin.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin
{
    public partial class MainWindow
    {
        private CancellationTokenSource? _autoStatsRefreshCts;
        private int _autoStatsRefreshRunning;
        private readonly PostGameRefreshTracker _postGameRefresh = new();
        private DateTime _lastAutomaticStatsRefreshUtc = DateTime.MinValue;

        private void StartAutomaticAccountInfoRefresh()
        {
            if (_autoStatsRefreshCts != null)
                return;

            _autoStatsRefreshCts = new CancellationTokenSource();
            CancellationToken token = _autoStatsRefreshCts.Token;
            Closed += (_, _) => StopAutomaticAccountInfoRefresh();
            Task.Run(() => RefreshAccountInfoAfterStartupAsync(token));
            Task.Run(() => MonitorAutomaticAccountInfoRefreshAsync(token));
        }

        private void StopAutomaticAccountInfoRefresh()
        {
            try
            {
                _autoStatsRefreshCts?.Cancel();
                _autoStatsRefreshCts?.Dispose();
                _autoStatsRefreshCts = null;
            }
            catch { }
        }

        private async Task RefreshAccountInfoAfterStartupAsync(CancellationToken cancellationToken)
        {
            try
            {
                for (int attempt = 0; attempt < 12 && !cancellationToken.IsCancellationRequested; attempt++)
                {
                    await Task.Delay(2500, cancellationToken);

                    if (_accounts != null && _accounts.Count > 0)
                    {
                        await RefreshAccountInfoAutomaticallyAsync("startup", onlyStale: true);
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Startup account info refresh failed: {ex.Message}");
            }
        }

        private async Task MonitorAutomaticAccountInfoRefreshAsync(CancellationToken cancellationToken)
        {
            bool refreshOnConnect = true;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!LCUService.CheckIfLeagueClientIsOpen())
                    {
                        _postGameRefresh.Disconnect();
                        refreshOnConnect = true;
                        await Task.Delay(5000, cancellationToken);
                        continue;
                    }

                    if (refreshOnConnect)
                    {
                        var summoner = await LCUService.ClientRequestAsync("GET", "lol-summoner/v1/current-summoner", cancellationToken: cancellationToken);
                        if (summoner[0] == "200" && await RefreshAccountInfoAutomaticallyAsync("client connected", onlyStale: true))
                            refreshOnConnect = false;
                    }

                    string? json = await LCUService.GetGameflowSessionAsync(cancellationToken);
                    if (json == null)
                    {
                        await Task.Delay(5000, cancellationToken);
                        continue;
                    }
                    var snapshot = Models.GameflowSnapshot.Parse(json);
                    StatsPlayer? player = null;
                    if (snapshot.Phase is "GameStart" or "InProgress" or "Reconnect" && _postGameRefresh.NeedsIdentity(snapshot.GameId))
                        player = await ReadStatsPlayerAsync(cancellationToken);
                    if (_postGameRefresh.Observe(snapshot, player) is { } finished)
                        _ = RefreshFinishedAccountAsync(finished, cancellationToken);

                    await Task.Delay(5000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Automatic account info refresh monitor error: {ex.Message}");
                    await Task.Delay(10000, cancellationToken);
                }
            }
        }

        private async Task<bool> RefreshAccountInfoAutomaticallyAsync(string reason, bool onlyStale = false)
        {
            if (Interlocked.CompareExchange(ref _autoStatsRefreshRunning, 1, 0) != 0)
                return false;

            try
            {
                if ((DateTime.UtcNow - _lastAutomaticStatsRefreshUtc).TotalSeconds < 5)
                    return false;
                if (_accounts.Count == 0)
                    return true;
                _lastAutomaticStatsRefreshUtc = DateTime.UtcNow;
                Console.WriteLine($"Automatic account info refresh started: {reason}");

                await UpdateAllAccountsAsync(onlyStale);
                if (!onlyStale)
                {
                    var accounts = await Dispatcher.InvokeAsync(() => new System.Collections.Generic.List<Models.Account>(_accounts));
                    await UIService.TrySyncGreyscreenStatsAsync(accounts);
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    RefreshAccountLists();
                    UpdateTotalGameStats();
                    SaveAccounts();
                });

                Console.WriteLine("Automatic account info refresh completed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Automatic account info refresh failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _autoStatsRefreshRunning, 0);
            }
            return true;
        }
    }
}
