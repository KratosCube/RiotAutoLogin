using RiotAutoLogin.Models;
using RiotAutoLogin.Services;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin;

public partial class MainWindow
{
    private async Task<StatsPlayer?> ReadStatsPlayerAsync(CancellationToken token)
    {
        var summoner = await LcuRankLookup.ReadObjectAsync("lol-summoner/v1/current-summoner",
            path => LCUService.ClientRequestAsync("GET", path, cancellationToken: token));
        var locale = await LcuRankLookup.ReadObjectAsync("riotclient/region-locale",
            path => LCUService.ClientRequestAsync("GET", path, cancellationToken: token));
        if (summoner is not { } player || locale is not { } region) return null;
        string name = RiotLocalProtocol.Text(player, "gameName"), tag = RiotLocalProtocol.Text(player, "tagLine");
        string platform = RiotLocalProtocol.Text(region, "region"), puuid = RiotLocalProtocol.Text(player, "puuid");
        return name.Length > 0 && tag.Length > 0 && platform.Length > 0 && puuid.Length > 0 ? new(name, tag, platform, puuid) : null;
    }

    private async Task RefreshFinishedAccountAsync(FinishedStatsGame game, CancellationToken token)
    {
        try
        {
            await Task.Delay(4000, token);
            var player = game.Player;
            var targets = await Dispatcher.InvokeAsync(() => _accounts.Where(a => player.Matches(a)).ToArray());
            if (targets.Length == 0) return;
            var generations = await Dispatcher.InvokeAsync(() =>
            {
                if (game.Queue is { } ladder)
                    foreach (var target in targets) AdvanceOpponentVersion(target, ladder);
                return targets.ToDictionary(a => a, a => game.Queue is { } queue ? OpponentVersion(a, queue) : 0L);
            });
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(4));
            string key = await Task.Run(ApiKeyManager.GetApiKey, timeout.Token);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                timeout.Token.ThrowIfCancellationRequested();
                bool completed = await Dispatcher.InvokeAsync<Task<bool>>(async () =>
                {
                    var account = targets.FirstOrDefault(a => _accounts.Contains(a) && player.Matches(a));
                    if (account == null) return true;
                    if (game.Queue is { } currentQueue && OpponentVersion(account, currentQueue) != generations[account]) return true;
                    bool ownUpdated = await AccountService.UpdateAccountRankAsync(account, account.Region);
                    if (!_accounts.Contains(account) || !player.Matches(account)) return false;
                    OpponentRankSummary? sample = null;
                    string status = "";
                    if (game.Queue is { } queue)
                    {
                        account.SetOpponentRankStatus(queue, "Updating this account after the game…");
                        bool clientOpen = await Task.Run(LCUService.CheckIfLeagueClientIsOpen, timeout.Token);
                        (sample, status) = await ReadOpponentSampleAsync(account, queue, clientOpen, key, timeout.Token, expectedGameId: game.GameId);
                    }
                    if (sample != null)
                        ownUpdated = await AccountService.UpdateAccountRankAsync(account, account.Region);
                    timeout.Token.ThrowIfCancellationRequested();
                    if (!_accounts.Contains(account) || !player.Matches(account)) return false;
                    if (game.Queue is { } completedQueue && OpponentVersion(account, completedQueue) != generations[account]) return true;
                    foreach (var target in targets.Where(a => _accounts.Contains(a) && player.Matches(a)))
                    {
                        if (!ReferenceEquals(target, account)) target.ApplyRanks(account.QueueRanks);
                        if (game.Queue is { } ladder)
                        {
                            if (sample != null) target.ApplyOpponentRanks(ladder, sample);
                            target.SetOpponentRankStatus(ladder, sample != null ? "" :
                                (attempt < 3 ? "Waiting for the completed match to appear. " : "Post-game data is not yet available; saved statistics are shown. ") + status);
                        }
                    }
                    if (sample != null && game.Queue is { } refreshed)
                        foreach (var target in targets) AdvanceOpponentVersion(target, refreshed);
                    RefreshAccountLists(); UpdateTotalGameStats(); SaveAccounts();
                    return game.Queue == null ? ownUpdated : sample != null;
                }).Task.Unwrap();
                if (completed) return;
                if (attempt < 3) await Task.Delay(TimeSpan.FromSeconds(8 * (attempt + 1)), timeout.Token);
            }
        }
        catch (OperationCanceledException)
        {
            await SetPostGameFailureAsync(game, "Post-game refresh timed out. Saved statistics are shown; use Refresh to try again.", token);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Post-game account refresh paused: {ex.Message}");
            await SetPostGameFailureAsync(game, "Post-game refresh is unavailable. Saved statistics are shown; use Refresh to try again.", token);
        }
    }

    private Task SetPostGameFailureAsync(FinishedStatsGame game, string message, CancellationToken token)
    {
        if (token.IsCancellationRequested || Dispatcher.HasShutdownStarted || game.Queue is not { } queue) return Task.CompletedTask;
        return Dispatcher.InvokeAsync(() =>
        {
            foreach (var account in _accounts.Where(a => game.Player.Matches(a)))
                account.SetOpponentRankStatus(queue, message);
        }).Task;
    }
}
