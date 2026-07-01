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
            Closed += (_, _) => StopAutomaticAccountInfoRefresh();
            Task.Run(() => MonitorAutomaticAccountInfoRefreshAsync(_autoStatsRefreshCts.Token));
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

                    string phase = await LCUService.GetCurrentGamePhaseAsync();

                    if (phase is "GameStart" or "InProgress")
                    {
                        _hasSeenActiveGameForStatsRefresh = true;
                    }
                    else if (_hasSeenActiveGameForStatsRefresh && IsPostGameStatsRefreshPhase(phase))
                    {
                        _hasSeenActiveGameForStatsRefresh = false;
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
            return phase is "PreEndOfGame" or "WaitingForStats" or "EndOfGame" or "Lobby" or "None";
        }

        private async Task RefreshAccountInfoAutomaticallyAsync(string reason)
        {
            if (_autoStatsRefreshRunning)
                return;

            if ((DateTime.UtcNow - _lastAutomaticStatsRefreshUtc).TotalMinutes < 2)
                return;

            if (_accounts == null || _accounts.Count == 0)
                return;

            _autoStatsRefreshRunning = true;
            _lastAutomaticStatsRefreshUtc = DateTime.UtcNow;

            try
            {
                Console.WriteLine($"Automatic account info refresh started: {reason}");

                await UpdateAllAccountsAsync();

                await Dispatcher.InvokeAsync(() =>
                {
                    RefreshAccountLists();
                    UpdateQuickLoginViewport();
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
