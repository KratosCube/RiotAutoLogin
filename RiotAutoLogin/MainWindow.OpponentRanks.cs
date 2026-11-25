using RiotAutoLogin.Services;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin;

public partial class MainWindow
{
    private readonly LcuOpponentRankService _opponentRanks = new(
        (path, token) => LCUService.ClientRequestAsync("GET", path, cancellationToken: token));
    private CancellationTokenSource? _opponentRefreshCts;
    private Task? _opponentRefreshTask;
    private Models.RankedQueue _opponentRefreshQueue;

    private void StartOpponentRankRefresh(bool force = false)
    {
        if (!_startupInitialized || _accounts.Count == 0 || _lifetimeCts.IsCancellationRequested) return;
        var queue = SelectedStatsQueue;
        if (!force && _opponentRefreshTask is { IsCompleted: false } && _opponentRefreshQueue == queue) return;
        _opponentRefreshCts?.Cancel();
        var source = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _opponentRefreshCts = source;
        _opponentRefreshQueue = queue;
        _opponentRefreshTask = RefreshAsync();

        async Task RefreshAsync()
        {
            try
            {
                await Task.Delay(1000, source.Token);
                if (!await Task.Run(LCUService.CheckIfLeagueClientIsOpen, source.Token)) return;
                foreach (var account in _accounts.ToArray())
                {
                    source.Token.ThrowIfCancellationRequested();
                    if (!force && account.QueueOpponentRanks.TryGetValue(queue, out var saved) &&
                        DateTime.UtcNow - saved.UpdatedUtc < TimeSpan.FromMinutes(15))
                        continue;
                    string name = account.GameName, tag = account.TagLine, region = account.Region;
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(45));
                    Models.OpponentRankSummary? summary;
                    try { summary = await _opponentRanks.TryReadAsync(name, tag, region, queue, timeout.Token); }
                    catch (OperationCanceledException) when (!source.IsCancellationRequested) { continue; }
                    if (summary == null || source.IsCancellationRequested || !_accounts.Contains(account) ||
                        !account.MatchesRiotId(name, tag, region)) continue;
                    account.ApplyOpponentRanks(queue, summary);
                    SaveAccounts();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Opponent rank refresh stopped: {ex.Message}");
            }
            finally
            {
                if (ReferenceEquals(_opponentRefreshCts, source)) _opponentRefreshCts = null;
                source.Dispose();
            }
        }
    }
}
