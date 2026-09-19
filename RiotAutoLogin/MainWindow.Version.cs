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
                    ? "A new release is checked at startup and shown when one is available."
                    : "Startup release notifications are disabled. Manual checks still work.";
            }
            finally
            {
                _suppressUpdateNotificationEvents = false;
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
