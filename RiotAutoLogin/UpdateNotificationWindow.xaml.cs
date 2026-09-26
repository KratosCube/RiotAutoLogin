using System;
using System.IO;
using System.Threading;
using System.Windows;
using RiotAutoLogin.Models;
using RiotAutoLogin.Services;

namespace RiotAutoLogin
{
    public partial class UpdateNotificationWindow : Window
    {
        private readonly UpdateInfo _updateInfo;
        private readonly UpdateService _updateService;
        private readonly CancellationTokenSource _downloadCts = new();
        private string? _downloadedFilePath;
        private bool _downloaded;
        private bool _downloading;
        private bool _installStarted;
        private bool _closed;

        public event Action<bool>? NotificationPreferenceChanged;

        public UpdateNotificationWindow(UpdateInfo updateInfo, UpdateService updateService)
        {
            InitializeComponent();
            _updateInfo = updateInfo;
            _updateService = updateService;
            txtUpdateInfo.Text = $"Current version: v{FormatVersion(updateInfo.CurrentVersion)}\n" +
                $"Available version: v{FormatVersion(updateInfo.LatestVersion)}";
            txtChangelog.Text = string.IsNullOrWhiteSpace(updateInfo.Changelog) ? "No changelog available." : updateInfo.Changelog;
            chkFutureUpdateNotifications.IsChecked = updateService.NotificationsEnabled;
            txtDeliveryInfo.Text = updateInfo.Delivery switch
            {
                UpdateDelivery.Installer => "One-time setup enables smaller future updates. Your accounts and settings are kept. Use the new Riot Auto Login shortcut afterwards.",
                UpdateDelivery.Package when updateInfo.UsesDelta => "Only changes are downloaded when possible. If a patch cannot be applied, the full package is used.",
                UpdateDelivery.Package => "A full package is needed for this update. Future updates can reuse it.",
                _ => "This release uses the standalone updater."
            };
            if (updateInfo.FileSize is { } size)
                txtFileSize.Text = $"{(updateInfo.UsesDelta ? "Estimated download" : "Download")}: {size / 1048576.0:F1} MB";
            if (updateInfo.UsesDelta && updateInfo.FullDownloadSize is { } full)
                txtFileSize.ToolTip = $"Full fallback: {full / 1048576.0:F1} MB";
            _updateService.UpdateProgressChanged += OnUpdateProgressChanged;
        }

        private async void btnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_downloaded)
            {
                _installStarted = true;
                btnDownload.IsEnabled = false;
                btnLater.IsEnabled = false;
                btnClose.IsEnabled = false;
                btnDownload.Content = "Installing...";
                bool started = await _updateService.InstallUpdateAsync(_updateInfo, _downloadedFilePath!);
                if (!started)
                {
                    _installStarted = false;
                    btnDownload.Content = "Install & Restart";
                    btnDownload.IsEnabled = btnLater.IsEnabled = btnClose.IsEnabled = true;
                }
                return;
            }

            _downloading = true;
            btnDownload.IsEnabled = false;
            btnDownload.Content = "Downloading...";
            btnLater.Content = "Cancel";
            progressPanel.Visibility = Visibility.Visible;
            try
            {
                var directory = Path.Combine(Path.GetTempPath(), "RiotAutoLogin", "Updates", Guid.NewGuid().ToString("N"));
                _downloadedFilePath = Path.Combine(directory, _updateInfo.Delivery == UpdateDelivery.Installer ? "setup.zip" : "update.exe");
                _downloaded = await _updateService.DownloadUpdateAsync(_updateInfo, _downloadedFilePath, _downloadCts.Token);
                if (_closed) return;
                btnDownload.Content = _downloaded ? "Install & Restart" : "Retry download";
                btnDownload.IsEnabled = true;
                btnLater.Content = "Later";
            }
            catch (OperationCanceledException) when (_downloadCts.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (!_closed)
                {
                    txtProgress.Text = ex.Message;
                    btnDownload.Content = "Retry download";
                    btnDownload.IsEnabled = true;
                }
            }
            finally
            {
                _downloading = false;
                if (_closed) Cleanup();
            }
        }

        private void btnLater_Click(object sender, RoutedEventArgs e) => Close();
        private void btnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void chkFutureUpdateNotifications_Click(object sender, RoutedEventArgs e)
        {
            bool enabled = chkFutureUpdateNotifications.IsChecked == true;
            _updateService.SetNotificationsEnabled(enabled);
            NotificationPreferenceChanged?.Invoke(enabled);
        }

        private void OnUpdateProgressChanged(UpdateProgress progress)
        {
            if (_closed) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_closed) return;
                txtProgress.Text = progress.Message;
                progressBar.Value = progress.ProgressPercentage;
                if (progress.Status == UpdateStatus.Error) progressPanel.Visibility = Visibility.Visible;
            }));
        }

        private static string FormatVersion(Version? version) => version == null ? "unknown" :
            version.Revision > 0 ? version.ToString(4) : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

        private void Cleanup()
        {
            _downloadCts.Dispose();
            // Packaged downloads stay cached for a later retry. Startup never installs them automatically.
            if (!_installStarted && _downloadedFilePath != null && _updateInfo.Delivery != UpdateDelivery.Package)
            {
                try
                {
                    if (File.Exists(_downloadedFilePath)) File.Delete(_downloadedFilePath);
                    var directory = Path.GetDirectoryName(_downloadedFilePath);
                    if (Directory.Exists(directory)) Directory.Delete(directory);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _closed = true;
            _downloadCts.Cancel();
            _updateService.UpdateProgressChanged -= OnUpdateProgressChanged;
            if (!_downloading) Cleanup();
            base.OnClosed(e);
        }
    }
}
