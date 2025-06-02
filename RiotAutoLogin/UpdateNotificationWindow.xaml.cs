using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using RiotAutoLogin.Models;
using RiotAutoLogin.Services;

namespace RiotAutoLogin
{
    public partial class UpdateNotificationWindow : Window
    {
        private readonly UpdateInfo _updateInfo;
        private readonly UpdateService _updateService;
        private string? _downloadedFilePath;

        public UpdateNotificationWindow(UpdateInfo updateInfo, UpdateService updateService)
        {
            InitializeComponent();
            _updateInfo = updateInfo;
            _updateService = updateService;
            
            InitializeUI();
            
            // Subscribe to update progress
            _updateService.UpdateProgressChanged += OnUpdateProgressChanged;
        }

        private void InitializeUI()
        {
            // Update info text
            txtUpdateInfo.Text = $"A new version of Riot Auto Login is available!\n" +
                               $"Current version: v{_updateInfo.CurrentVersion}\n" +
                               $"Latest version: v{_updateInfo.LatestVersion}";

            // Changelog
            txtChangelog.Text = string.IsNullOrEmpty(_updateInfo.Changelog) 
                ? "No changelog available." 
                : _updateInfo.Changelog;

            // File size
            if (_updateInfo.FileSize.HasValue)
            {
                var sizeInMB = _updateInfo.FileSize.Value / (1024.0 * 1024.0);
                txtFileSize.Text = $"Download size: {sizeInMB:F1} MB";
            }
        }

        private async void btnDownload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Show progress panel
                progressPanel.Visibility = Visibility.Visible;
                btnDownload.IsEnabled = false;
                btnDownload.Content = "Downloading...";

                // Create download path
                var tempDir = Path.GetTempPath();
                var fileName = $"RiotAutoLogin-v{_updateInfo.LatestVersion}.exe";
                _downloadedFilePath = Path.Combine(tempDir, fileName);

                // Download the update
                bool downloadSuccess = await _updateService.DownloadUpdateAsync(_updateInfo, _downloadedFilePath);

                if (downloadSuccess)
                {
                    // Change button to install
                    btnDownload.Content = "Install & Restart";
                    btnDownload.IsEnabled = true;
                    btnDownload.Click -= btnDownload_Click;
                    btnDownload.Click += btnInstall_Click;
                    
                    txtProgress.Text = "Download completed! Click 'Install & Restart' to update.";
                }
                else
                {
                    MessageBox.Show("Failed to download the update. Please try again later.", 
                        "Download Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    ResetDownloadButton();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error downloading update: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ResetDownloadButton();
            }
        }

        private void btnInstall_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_downloadedFilePath) || !File.Exists(_downloadedFilePath))
                {
                    MessageBox.Show("Downloaded file not found. Please download again.", 
                        "Installation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var result = MessageBox.Show(
                    "The application will now close and restart with the new version. Continue?",
                    "Install Update", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Install the update (this will close the application)
                    _updateService.InstallUpdate(_downloadedFilePath, restartApp: true);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error installing update: {ex.Message}", 
                    "Installation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnLater_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void OnUpdateProgressChanged(UpdateProgress progress)
        {
            // Update UI on the UI thread
            Dispatcher.Invoke(() =>
            {
                txtProgress.Text = progress.Message;
                progressBar.Value = progress.ProgressPercentage;

                if (progress.Status == UpdateStatus.Error)
                {
                    MessageBox.Show($"Update error: {progress.Message}", 
                        "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    ResetDownloadButton();
                }
            });
        }

        private void ResetDownloadButton()
        {
            btnDownload.Content = "Download Update";
            btnDownload.IsEnabled = true;
            progressPanel.Visibility = Visibility.Collapsed;
        }

        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe from events
            _updateService.UpdateProgressChanged -= OnUpdateProgressChanged;
            
            // Clean up downloaded file if not installing
            if (!string.IsNullOrEmpty(_downloadedFilePath) && File.Exists(_downloadedFilePath))
            {
                try
                {
                    File.Delete(_downloadedFilePath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
            
            base.OnClosed(e);
        }
    }
} 