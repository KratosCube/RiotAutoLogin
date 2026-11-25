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
    private readonly AnnualCsStore _csStore = new(AnnualCsStore.DefaultPath);
    private System.Collections.Generic.Dictionary<string, AnnualCs> _csHistory = new();
    private Task? _csLoad;
    private bool _csLoadFailed;
    private Task LoadAnnualCsAsync() => _csLoad ??= LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            _csHistory = await Task.Run(_csStore.Load, _lifetimeCts.Token);
            foreach (var account in _accounts)
                if (_csHistory.TryGetValue(AnnualCsStore.Identity(account), out var saved)) account.AnnualCs = saved.Copy();
        }
        catch (Exception ex)
        {
            _csLoadFailed = true;
            SetCsContext($"Saved history could not be loaded: {ex.Message}");
        }
        if (farmStatsPage.IsVisible) UIService.UpdateAnnualCsStats(this, _accounts);
    }
    private string _csMessage = "Available history · all accounts · all queues · last 12 months";
    private void SetCsContext(string message)
    {
        _csMessage = message;
        if (farmStatsPage.IsVisible) txtStatsContext.Text = message;
    }
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
            await LoadAnnualCsAsync();
            if (_csLoadFailed) return;
            string key = await Task.Run(ApiKeyManager.GetApiKey, source.Token);
            if (string.IsNullOrWhiteSpace(key)) { SetCsContext("Save a Riot API key in Settings to import the last 12 months."); return; }
            var service = new AnnualCsService(RiotApiClient.Shared.GetAsync);
            foreach (var account in _accounts.ToArray())
            {
                string name = account.GameName, tag = account.TagLine, region = account.Region;
                try
                {
                    do
                    {
                        SetCsContext($"Importing {name} · {account.AnnualCs.Matches.Count:N0} games saved");
                        var next = await service.StepAsync(name, tag, region, key, account.AnnualCs, DateTimeOffset.UtcNow, source.Token);
                        source.Token.ThrowIfCancellationRequested();
                        if (!_accounts.Contains(account) || !account.MatchesRiotId(name, tag, region)) break;
                        account.AnnualCs = next;
                        _csHistory[AnnualCsStore.Identity(account)] = next.Copy();
                        var savedIds = _accounts.Select(AnnualCsStore.Identity).ToHashSet();
                        var snapshot = _csHistory.Where(pair => savedIds.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value.Copy());
                        await Task.Run(() => _csStore.Save(snapshot), source.Token);
                        UIService.UpdateAnnualCsStats(this, _accounts);
                    } while (account.AnnualCs.Scanning);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { farmStatsPage.ToolTip = $"Import paused for {name}: {ex.Message}. Saved progress can be resumed."; }
            }
            SetCsContext("Available history · all accounts · all queues · last 12 months");
        }
        catch (OperationCanceledException) { SetCsContext("Sync paused · saved progress will resume next time"); }
        finally
        {
            _csRefresh = null;
            btnSyncCs.Content = "Sync history";
            UIService.UpdateAnnualCsStats(this, _accounts);
        }
    }
}
