using RiotAutoLogin.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin
{
    public partial class MainWindow
    {
        private CancellationTokenSource? _autoStatsRefreshCts;
        private bool _autoStatsRefreshRunning;
        private bool _hasSeenActiveGameForStatsRefresh;
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
                        await RefreshAccountInfoAutomaticallyAsync("startup");
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
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!LCUService.CheckIfLeagueClientIsOpen())
                    {
                        _hasSeenActiveGameForStatsRefresh = false;
                        await Task.Delay(5000, cancellationToken);
                        continue;
                    }

                    string? json = await LCUService.GetGameflowSessionAsync(cancellationToken);
                    if (json == null)
                    {
                        await Task.Delay(5000, cancellationToken);
                        continue;
                    }
                    string phase = Models.GameflowSnapshot.Parse(json).Phase;

                    if (phase is "GameStart" or "InProgress")
                    {
                        _hasSeenActiveGameForStatsRefresh = true;
                    }
                    else if (_hasSeenActiveGameForStatsRefresh && IsPostGameStatsRefreshPhase(phase))
                    {
                        _hasSeenActiveGameForStatsRefresh = false;
                        await Task.Delay(4000, cancellationToken);
                        await RefreshAccountInfoAutomaticallyAsync($"post-game phase {phase}");
                    }

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

        private static bool IsPostGameStatsRefreshPhase(string phase)
        {
            return phase is "EndOfGame" or "Lobby" or "None";
        }

        private async Task RefreshAccountInfoAutomaticallyAsync(string reason)
        {
            if (_autoStatsRefreshRunning)
                return;

            if ((DateTime.UtcNow - _lastAutomaticStatsRefreshUtc).TotalSeconds < 5)
                return;

            if (_accounts == null || _accounts.Count == 0)
                return;

            _autoStatsRefreshRunning = true;
            _lastAutomaticStatsRefreshUtc = DateTime.UtcNow;

            try
            {
                Console.WriteLine($"Automatic account info refresh started: {reason}");

                await UpdateAllAccountsAsync(onlyStale: reason == "startup");
                if (reason != "startup")
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
                _autoStatsRefreshRunning = false;
            }
        }
    }
}
