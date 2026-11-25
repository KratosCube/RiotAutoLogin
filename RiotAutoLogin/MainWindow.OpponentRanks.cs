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
    private readonly PublicOpponentRankService _publicOpponentRanks = new(RiotApiClient.Shared.GetAsync);
    private CancellationTokenSource? _opponentRefreshCts;
    private Task? _opponentRefreshTask;
    private Models.RankedQueue _opponentRefreshQueue;

    private void StartOpponentRankRefresh(bool force = false)
    {
        if (!_startupInitialized || _accounts.Count == 0 || _lifetimeCts.IsCancellationRequested) return;
        var queue = SelectedStatsQueue;
        if (!force && _opponentRefreshTask is { IsCompleted: false } && _opponentRefreshQueue == queue) return;
        _opponentRefreshCts?.Cancel();
        foreach (var account in _accounts) account.SetOpponentRankStatus(queue, "");
        var source = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _opponentRefreshCts = source;
        _opponentRefreshQueue = queue;
        _opponentRefreshTask = RefreshAsync();

        async Task RefreshAsync()
        {
            try
            {
                await Task.Delay(1000, source.Token);
                var lookup = new OpponentRankSources(_opponentRanks.TryReadAsync, _publicOpponentRanks.ReadAsync);
                string key = await Task.Run(ApiKeyManager.GetApiKey, source.Token);
                foreach (var account in _accounts.ToArray())
                {
                    source.Token.ThrowIfCancellationRequested();
                    if (!force && account.QueueOpponentRanks.TryGetValue(queue, out var saved) &&
                        DateTime.UtcNow - saved.UpdatedUtc < TimeSpan.FromMinutes(15))
                        continue;
                    string name = account.GameName, tag = account.TagLine, region = account.Region;
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(source.Token);
                    timeout.CancelAfter(TimeSpan.FromMinutes(4));
                    Models.OpponentRankSummary? summary;
                    string status;
                    account.SetOpponentRankStatus(queue, "Refreshing opponent ranks in the background…");
                    try
                    {
                        bool clientOpen = await Task.Run(LCUService.CheckIfLeagueClientIsOpen, timeout.Token);
                        (summary, status) = await lookup.ReadAsync(name, tag, region, queue, clientOpen, key, timeout.Token);
                    }
                    catch (OperationCanceledException) when (!source.IsCancellationRequested)
                    {
                        if (_accounts.Contains(account) && account.MatchesRiotId(name, tag, region))
                            account.SetOpponentRankStatus(queue, "Refresh timed out. Saved statistics are retained; try refreshing again.");
                        continue;
                    }
                    finally
                    {
                        if (source.IsCancellationRequested && ReferenceEquals(_opponentRefreshCts, source) &&
                            _accounts.Contains(account) && account.MatchesRiotId(name, tag, region))
                            account.SetOpponentRankStatus(queue, "");
                    }
                    if (!source.IsCancellationRequested && _accounts.Contains(account) && account.MatchesRiotId(name, tag, region))
                        account.SetOpponentRankStatus(queue, status);
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
