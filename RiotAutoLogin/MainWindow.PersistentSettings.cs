using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using RiotAutoLogin.Services;

namespace RiotAutoLogin;

public partial class MainWindow
{
    private readonly LeagueConfigSettingsService _leagueConfigSettings = new();
    private bool _suppressPersistentSettingsEvents;

    private async Task RefreshPersistentSettingsAsync()
    {
        tglPersistentSettings.IsEnabled = false;
        try
        {
            ShowLeagueConfigState(await Task.Run(_leagueConfigSettings.Refresh));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            try { ShowLeagueConfigState(await Task.Run(_leagueConfigSettings.Inspect)); }
            catch { tglPersistentSettings.IsEnabled = false; }
            txtPersistentSettingsStatus.Text = $"Could not check League settings: {ex.Message}";
        }
    }

    private void ShowLeagueConfigState(LeagueConfigState state)
    {
        _suppressPersistentSettingsEvents = true;
        try
        {
            txtLeagueConfigPath.Text = state.Directory ?? "League Config not found";
            tglPersistentSettings.IsChecked = state.AllReadOnly;
            tglPersistentSettings.Content = state.AllReadOnly ? "ON" : "OFF";
            tglPersistentSettings.IsEnabled = state.Files.Length > 0;
            txtPersistentSettingsStatus.Text = state.Directory == null
                ? "Choose your League of Legends Config folder to manage its player settings."
                : $"Files: {string.Join(", ", state.Files)}. " + (state.AllReadOnly
                    ? "Read-only is on; changes made in League may not be saved."
                    : "Read-only is off; League can save changes to these files.");
        }
        finally { _suppressPersistentSettingsEvents = false; }
    }

    private async void tglPersistentSettings_Checked(object sender, RoutedEventArgs e) =>
        await SetPersistentSettingsAsync(true);

    private async void tglPersistentSettings_Unchecked(object sender, RoutedEventArgs e) =>
        await SetPersistentSettingsAsync(false);

    private async Task SetPersistentSettingsAsync(bool enabled)
    {
        if (_suppressPersistentSettingsEvents || !IsLoaded) return;
        tglPersistentSettings.IsEnabled = false;
        try
        {
            ShowLeagueConfigState(await Task.Run(() => _leagueConfigSettings.SetEnabled(enabled)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            try { ShowLeagueConfigState(await Task.Run(_leagueConfigSettings.Inspect)); }
            catch { tglPersistentSettings.IsEnabled = false; }
            txtPersistentSettingsStatus.Text = $"Could not change League settings: {ex.Message}";
        }
    }

    private async void btnChooseLeagueConfig_Click(object sender, RoutedEventArgs e)
    {
        using var picker = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose League of Legends\\Config (or the League of Legends folder)",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(txtLeagueConfigPath.Text) ? txtLeagueConfigPath.Text : string.Empty
        };
        if (picker.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        try
        {
            ShowLeagueConfigState(await Task.Run(() => _leagueConfigSettings.ChooseDirectory(picker.SelectedPath)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            txtPersistentSettingsStatus.Text = ex.Message;
        }
    }
}
