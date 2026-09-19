using RiotAutoLogin.Models;
using RiotAutoLogin.Services;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace RiotAutoLogin
{
    public partial class MainWindow
    {
        private bool _manualUpdateCheckRequested;
        private bool _manualUpdateFeedbackInitialized;
        private readonly RemotePickServerService _remotePickServerService = new();

        private void InitializeSettingsExtras()
        {
            AutoAcceptSettingsService.Load();
            txtAutoAcceptDelaySeconds.Text = AutoAcceptSettingsService.DelaySeconds
                .ToString(CultureInfo.InvariantCulture);
            UpdateAutoAcceptDelayHint(AutoAcceptSettingsService.DelaySeconds);

            tglRemotePick.IsChecked = false;
            btnCopyRemotePickLink.IsEnabled = false;
            UpdateRemotePickStatus("Stopped. Enable Remote Pick only when you want to use your phone.");

            InitializeManualUpdateFeedback();
        }

        private async void RemotePickToggle_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                await _remotePickServerService.StartAsync();
                tglRemotePick.Content = "ON";
                btnCopyRemotePickLink.IsEnabled = true;

                UpdateRemotePickStatus(
                    "Remote Pick is running. Open this address on a phone connected to the same Wi-Fi/LAN:",
                    GetPreferredRemotePickPhoneUrl());
            }
            catch (Exception ex)
            {
                tglRemotePick.IsChecked = false;
                tglRemotePick.Content = "OFF";
                btnCopyRemotePickLink.IsEnabled = false;
                UpdateRemotePickStatus($"Failed to start Remote Pick: {ex.Message}");

                MessageBox.Show(
                    $"Remote Pick server could not be started:\n{ex.Message}\n\nIf Windows Firewall asks for access, allow it for Private networks.",
                    "Remote Pick Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void RemotePickToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _remotePickServerService.Stop();
            tglRemotePick.Content = "OFF";
            btnCopyRemotePickLink.IsEnabled = false;
            UpdateRemotePickStatus("Stopped. Enable Remote Pick only when you want to use your phone.");
        }

        private void btnCopyRemotePickLink_Click(object sender, RoutedEventArgs e)
        {
            if (!_remotePickServerService.IsRunning)
                return;

            string url = GetPreferredRemotePickPhoneUrl();
            Clipboard.SetText(url);
            UpdateRemotePickStatus("Link copied. Open it on a phone connected to the same Wi-Fi/LAN:", url);
        }

        private void UpdateRemotePickStatus(string status, string? url = null)
        {
            txtRemotePickStatus.Text = status;
            txtRemotePickUrl.Text = url ?? string.Empty;
        }

        private string GetPreferredRemotePickPhoneUrl()
        {
            foreach (string url in _remotePickServerService.LocalUrls)
            {
                if (RemotePickUrlHostStartsWith(url, "192.168."))
                    return url;
            }

            foreach (string url in _remotePickServerService.LocalUrls)
            {
                if (IsPrivateRemotePickUrl(url))
                    return url;
            }

            foreach (string url in _remotePickServerService.LocalUrls)
            {
                if (!RemotePickUrlHostStartsWith(url, "127."))
                    return url;
            }

            return _remotePickServerService.LocalUrl;
        }

        private static bool IsPrivateRemotePickUrl(string url)
        {
            if (!TryGetRemotePickUrlHost(url, out string host))
                return false;

            if (host.StartsWith("192.168.", StringComparison.Ordinal) ||
                host.StartsWith("10.", StringComparison.Ordinal))
                return true;

            if (!host.StartsWith("172.", StringComparison.Ordinal))
                return false;

            string[] parts = host.Split('.');
            return parts.Length >= 2 &&
                   int.TryParse(parts[1], out int secondOctet) &&
                   secondOctet is >= 16 and <= 31;
        }

        private static bool RemotePickUrlHostStartsWith(string url, string prefix)
        {
            return TryGetRemotePickUrlHost(url, out string host) &&
                   host.StartsWith(prefix, StringComparison.Ordinal);
        }

        private static bool TryGetRemotePickUrlHost(string url, out string host)
        {
            host = string.Empty;
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                return false;

            host = uri.Host;
            return !string.IsNullOrWhiteSpace(host);
        }

        private void InitializeManualUpdateFeedback()
        {
            if (_manualUpdateFeedbackInitialized || _updateService == null)
                return;

            _manualUpdateFeedbackInitialized = true;
            btnCheckUpdates.PreviewMouseLeftButtonDown += (_, _) => _manualUpdateCheckRequested = true;
            btnCheckUpdates.KeyDown += (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Space)
                    _manualUpdateCheckRequested = true;
            };

            _updateService.UpdateProgressChanged += OnManualUpdateProgressChanged;
        }

        private void OnManualUpdateProgressChanged(UpdateProgress progress)
        {
            if (!_manualUpdateCheckRequested)
                return;

            if (progress.Status == UpdateStatus.NoUpdateAvailable)
            {
                _manualUpdateCheckRequested = false;
                Dispatcher.Invoke(() => MessageBox.Show(
                    progress.Message,
                    "No Updates Available",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information));
            }
            else if (progress.Status == UpdateStatus.Error)
            {
                _manualUpdateCheckRequested = false;
                Dispatcher.Invoke(() => MessageBox.Show(
                    progress.Message,
                    "Update Check Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning));
            }
            else if (progress.Status == UpdateStatus.UpdateAvailable)
            {
                _manualUpdateCheckRequested = false;
            }
        }

        private void AutoAcceptDelaySecondsTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            SaveAutoAcceptDelayFromTextBox();
            Keyboard.ClearFocus();
            e.Handled = true;
        }

        private void AutoAcceptDelaySecondsTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveAutoAcceptDelayFromTextBox();
        }

        private void AutoAcceptDelaySecondsTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !int.TryParse(e.Text, NumberStyles.None, CultureInfo.InvariantCulture, out _);
        }

        private void AutoAcceptDelaySecondsTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.DataObject.GetDataPresent(DataFormats.Text) ||
                e.DataObject.GetData(DataFormats.Text) is not string pastedText ||
                !int.TryParse(pastedText, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                e.CancelCommand();
            }
        }

        private void SaveAutoAcceptDelayFromTextBox()
        {
            if (!int.TryParse(
                    txtAutoAcceptDelaySeconds.Text,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int delaySeconds))
            {
                delaySeconds = 0;
            }

            int normalizedDelay = AutoAcceptSettingsService.SaveDelaySeconds(delaySeconds);
            txtAutoAcceptDelaySeconds.Text = normalizedDelay.ToString(CultureInfo.InvariantCulture);
            UpdateAutoAcceptDelayHint(normalizedDelay);
        }

        private void UpdateAutoAcceptDelayHint(int delaySeconds)
        {
            txtAutoAcceptDelayHint.Text = delaySeconds == 0
                ? $"0 = accept immediately. Maximum {AutoAcceptSettingsService.MaxDelaySeconds} seconds."
                : $"Wait {delaySeconds} seconds, then accept only if ReadyCheck is still active.";
        }
    }
}
