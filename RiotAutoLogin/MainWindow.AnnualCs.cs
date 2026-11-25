using RiotAutoLogin.Models;
using RiotAutoLogin.Services;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace RiotAutoLogin;

public partial class MainWindow
{
    private CancellationTokenSource? _csRefresh;
    private void SyncCs_Click(object sender, RoutedEventArgs e)
    {
        if (_csRefresh != null) { _csRefresh.Cancel(); return; }
        _ = RefreshAnnualCsAsync();
    }

    private async Task RefreshAnnualCsAsync()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _csRefresh = source;
        btnSyncCs.Content = "Stop sync";
        try
        {
            string key = await Task.Run(ApiKeyManager.GetApiKey, source.Token);
            if (string.IsNullOrWhiteSpace(key)) { txtStatsContext.Text = "Save a Riot API key in Settings to import the last 12 months."; return; }
            var service = new AnnualCsService(RiotApiClient.Shared.GetAsync);
            foreach (var account in _accounts.ToArray())
            {
                string name = account.GameName, tag = account.TagLine, region = account.Region;
                try
                {
                    do
                    {
                        txtStatsContext.Text = $"Importing {name} · {account.AnnualCs.Matches.Count:N0} games saved";
                        var next = await service.StepAsync(name, tag, region, key, account.AnnualCs, DateTimeOffset.UtcNow, source.Token);
                        source.Token.ThrowIfCancellationRequested();
                        if (!_accounts.Contains(account) || !account.MatchesRiotId(name, tag, region)) break;
                        account.AnnualCs = next;
                        SaveAccounts();
                        UIService.UpdateAnnualCsStats(this, _accounts);
                    } while (account.AnnualCs.Scanning);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { farmStatsPage.ToolTip = $"Import paused for {name}: {ex.Message}. Saved progress can be resumed."; }
            }
            txtStatsContext.Text = "Available history · all accounts · all queues · last 12 months";
        }
        catch (OperationCanceledException) { txtStatsContext.Text = "Sync paused · saved progress will resume next time"; }
        finally
        {
            _csRefresh = null;
            btnSyncCs.Content = "Sync history";
            UIService.UpdateAnnualCsStats(this, _accounts);
        }
    }
}
