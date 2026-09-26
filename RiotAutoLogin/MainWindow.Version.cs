using System;
using System.Reflection;

namespace RiotAutoLogin
{
    public partial class MainWindow
    {
        private bool _suppressUpdateNotificationEvents;

        private void UpdateCurrentVersionDisplay()
        {
            try
            {
                Version? version = Assembly.GetExecutingAssembly().GetName().Version;
                txtCurrentVersion.Text = $"Current version: v{FormatAppVersion(version)}";
            }
            catch
            {
                txtCurrentVersion.Text = "Current version: unknown";
            }

            UpdateUpdateNotificationUi();
        }

        private void tglUpdateNotifications_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            SaveUpdateNotificationPreference(true);
        }

        private void tglUpdateNotifications_Unchecked(object sender, System.Windows.RoutedEventArgs e)
        {
            SaveUpdateNotificationPreference(false);
        }

        private void SaveUpdateNotificationPreference(bool enabled)
        {
            if (_suppressUpdateNotificationEvents || _updateService == null)
                return;

            _updateService.SetNotificationsEnabled(enabled);
            UpdateUpdateNotificationUi();
        }

        private void UpdateUpdateNotificationUi()
        {
            if (_updateService == null || tglUpdateNotifications == null || txtUpdateNotificationsHint == null)
                return;

            bool enabled = _updateService.NotificationsEnabled;
            _suppressUpdateNotificationEvents = true;
            try
            {
                tglUpdateNotifications.IsChecked = enabled;
                tglUpdateNotifications.Content = enabled ? "ON" : "OFF";
                txtUpdateNotificationsHint.Text = enabled
                    ? "Announced releases are shown once. Minor releases are available through Check for Updates."
                    : "Startup release notifications are disabled. Manual checks still work.";
                txtUpdateDelivery.Text = _updateService.SupportsDeltaUpdates
                    ? "Smaller updates enabled. Existing files are reused when a delta is available."
                    : "Standalone version. One-time setup enables smaller future updates and keeps your accounts and settings.";
                btnEnableSmallerUpdates.Visibility = _updateService.SupportsDeltaUpdates
                    ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            }
            finally
            {
                _suppressUpdateNotificationEvents = false;
            }
        }

        private async void btnEnableSmallerUpdates_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_updateService == null) return;
            btnEnableSmallerUpdates.IsEnabled = false;
            btnCheckUpdates.IsEnabled = false;
            try
            {
                var info = await _updateService.CheckForUpdatesAsync(manual: true, allowMigration: true);
                txtUpdateCheckStatus.Text = info.ErrorMessage ?? (info.CanInstall
                    ? "One-time setup is ready in the update window."
                    : "The setup package has not been published for this version yet.");
            }
            finally
            {
                btnEnableSmallerUpdates.IsEnabled = true;
                btnCheckUpdates.IsEnabled = true;
            }
        }

        private static string FormatAppVersion(Version? version)
        {
            if (version == null)
                return "unknown";

            if (version.Revision > 0)
                return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";

            return $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
        }
    }
}
