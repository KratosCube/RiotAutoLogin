using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace RiotAutoLogin
{
    public partial class MainWindow
    {
        private bool _staticClientAlertControlsHooked;
        private bool _suppressStaticClientAlertEvents;
        private bool _remotePickLanUrlUiPatched;

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            Dispatcher.BeginInvoke(new Action(UpdateCurrentVersionDisplay), DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(new Action(StartClientAlertsSettingsRetryTimer), DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(new Action(StartRemotePickLanUrlPatchTimer), DispatcherPriority.Loaded);
        }

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
        }

        private static string FormatAppVersion(Version? version)
        {
            if (version == null)
                return "unknown";

            if (version.Revision > 0)
                return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";

            if (version.Build > 0)
                return $"{version.Major}.{version.Minor}.{version.Build}";

            return $"{version.Major}.{version.Minor}";
        }

        private void StartClientAlertsSettingsRetryTimer()
        {
            int attempts = 0;
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };

            timer.Tick += (_, _) =>
            {
                attempts++;

                UpdateCurrentVersionDisplay();
                HookStaticClientAlertSettingsControls();
                UpdateStaticClientAlertSettingsUi();

                EnsureClientAlertSettingsCard();
                UpdateClientAlertSettingsUi();

                if (attempts >= 30)
                    timer.Stop();
            };

            timer.Start();
        }

        private void StartRemotePickLanUrlPatchTimer()
        {
            int attempts = 0;
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };

            timer.Tick += (_, _) =>
            {
                attempts++;

                if (TryPatchRemotePickLanUrlUi() || attempts >= 30)
                    timer.Stop();
            };

            timer.Start();
        }

        private bool TryPatchRemotePickLanUrlUi()
        {
            if (_remotePickLanUrlUiPatched)
                return true;

            if (_remotePickToggle == null)
                return false;

            Button? copyButton = FindRemotePickCopyButton(this);
            if (copyButton == null)
                return false;

            _remotePickLanUrlUiPatched = true;
            copyButton.Content = "Copy Link";

            copyButton.Click += (_, _) =>
            {
                if (!_remotePickServerService.IsRunning)
                    return;

                string url = GetPreferredRemotePickPhoneUrl();
                Clipboard.SetText(url);
                UpdateRemotePickStatus(
                    "Link copied. Open this URL on your phone while it is on the same Wi-Fi/LAN:",
                    url);
            };

            _remotePickToggle.Checked += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_remotePickServerService.IsRunning)
                        return;

                    UpdateRemotePickStatus(
                        "Remote Pick is running. Open this URL on your phone while it is on the same Wi-Fi/LAN:",
                        GetPreferredRemotePickPhoneUrl());
                }), DispatcherPriority.Background);
            };

            return true;
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

            if (host.StartsWith("192.168.", StringComparison.Ordinal))
                return true;

            if (host.StartsWith("10.", StringComparison.Ordinal))
                return true;

            if (!host.StartsWith("172.", StringComparison.Ordinal))
                return false;

            string[] parts = host.Split('.');
            return parts.Length >= 2 &&
                   int.TryParse(parts[1], out int secondOctet) &&
                   secondOctet >= 16 &&
                   secondOctet <= 31;
        }

        private static bool RemotePickUrlHostStartsWith(string url, string prefix)
        {
            return TryGetRemotePickUrlHost(url, out string host) && host.StartsWith(prefix, StringComparison.Ordinal);
        }

        private static bool TryGetRemotePickUrlHost(string url, out string host)
        {
            host = string.Empty;

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                return false;

            host = uri.Host;
            return !string.IsNullOrWhiteSpace(host);
        }

        private static Button? FindRemotePickCopyButton(DependencyObject root)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is Button button)
                {
                    string? content = button.Content?.ToString();
                    if (string.Equals(content, "Copy Links", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(content, "Copy Link", StringComparison.OrdinalIgnoreCase))
                    {
                        return button;
                    }
                }

                Button? nestedButton = FindRemotePickCopyButton(child);
                if (nestedButton != null)
                    return nestedButton;
            }

            return null;
        }

        private void HookStaticClientAlertSettingsControls()
        {
            if (_staticClientAlertControlsHooked)
                return;

            if (FindName("tglGameStartAlert") is not ToggleButton gameStartToggle ||
                FindName("tglFlashSlotWarning") is not ToggleButton flashWarningToggle ||
                FindName("rbFlashSlot1") is not RadioButton flashSlot1 ||
                FindName("rbFlashSlot2") is not RadioButton flashSlot2)
            {
                return;
            }

            TextBox? repeatCountTextBox = FindName("txtGameStartAlertRepeatCount") as TextBox;
            _staticClientAlertControlsHooked = true;

            gameStartToggle.Checked += (_, _) =>
            {
                if (_suppressStaticClientAlertEvents)
                    return;

                _hotkeySettings.GameStartAlertEnabled = true;
                SaveHotkeySettings();
                UpdateStaticClientAlertSettingsUi();
            };

            gameStartToggle.Unchecked += (_, _) =>
            {
                if (_suppressStaticClientAlertEvents)
                    return;

                _hotkeySettings.GameStartAlertEnabled = false;
                SaveHotkeySettings();
                UpdateStaticClientAlertSettingsUi();
            };

            if (repeatCountTextBox != null)
            {
                repeatCountTextBox.LostFocus += (_, _) => SaveStaticGameStartAlertRepeatCount(repeatCountTextBox);
                repeatCountTextBox.KeyDown += (_, e) =>
                {
                    if (e.Key != Key.Enter)
                        return;

                    SaveStaticGameStartAlertRepeatCount(repeatCountTextBox);
                    e.Handled = true;
                };
            }

            flashWarningToggle.Checked += (_, _) =>
            {
                if (_suppressStaticClientAlertEvents)
                    return;

                _hotkeySettings.FlashSlotWarningEnabled = true;
                SaveHotkeySettings();
                UpdateStaticClientAlertSettingsUi();
            };

            flashWarningToggle.Unchecked += (_, _) =>
            {
                if (_suppressStaticClientAlertEvents)
                    return;

                _hotkeySettings.FlashSlotWarningEnabled = false;
                SaveHotkeySettings();
                UpdateStaticClientAlertSettingsUi();
            };

            flashSlot1.Checked += (_, _) =>
            {
                if (_suppressStaticClientAlertEvents)
                    return;

                _hotkeySettings.PreferredFlashSlot = 1;
                SaveHotkeySettings();
                UpdateStaticClientAlertSettingsUi();
            };

            flashSlot2.Checked += (_, _) =>
            {
                if (_suppressStaticClientAlertEvents)
                    return;

                _hotkeySettings.PreferredFlashSlot = 2;
                SaveHotkeySettings();
                UpdateStaticClientAlertSettingsUi();
            };
        }

        private void SaveStaticGameStartAlertRepeatCount(TextBox repeatCountTextBox)
        {
            if (_suppressStaticClientAlertEvents)
                return;

            int parsedValue = int.TryParse(repeatCountTextBox.Text, out int value)
                ? value
                : _hotkeySettings.GameStartAlertRepeatCount;

            _hotkeySettings.GameStartAlertRepeatCount = ClampGameStartAlertRepeatCount(parsedValue);
            SaveHotkeySettings();
            UpdateStaticClientAlertSettingsUi();
            UpdateClientAlertSettingsUi();
        }

        private void UpdateStaticClientAlertSettingsUi()
        {
            if (FindName("tglGameStartAlert") is not ToggleButton gameStartToggle ||
                FindName("tglFlashSlotWarning") is not ToggleButton flashWarningToggle ||
                FindName("rbFlashSlot1") is not RadioButton flashSlot1 ||
                FindName("rbFlashSlot2") is not RadioButton flashSlot2)
            {
                return;
            }

            TextBox? repeatCountTextBox = FindName("txtGameStartAlertRepeatCount") as TextBox;

            if (_hotkeySettings.PreferredFlashSlot != 1 && _hotkeySettings.PreferredFlashSlot != 2)
                _hotkeySettings.PreferredFlashSlot = 2;

            _hotkeySettings.GameStartAlertRepeatCount = ClampGameStartAlertRepeatCount(_hotkeySettings.GameStartAlertRepeatCount);

            _suppressStaticClientAlertEvents = true;
            try
            {
                gameStartToggle.IsChecked = _hotkeySettings.GameStartAlertEnabled;
                gameStartToggle.Content = _hotkeySettings.GameStartAlertEnabled ? "ON" : "OFF";

                if (repeatCountTextBox != null)
                    repeatCountTextBox.Text = _hotkeySettings.GameStartAlertRepeatCount.ToString();

                flashWarningToggle.IsChecked = _hotkeySettings.FlashSlotWarningEnabled;
                flashWarningToggle.Content = _hotkeySettings.FlashSlotWarningEnabled ? "ON" : "OFF";

                flashSlot1.IsChecked = _hotkeySettings.PreferredFlashSlot == 1;
                flashSlot2.IsChecked = _hotkeySettings.PreferredFlashSlot == 2;
            }
            finally
            {
                _suppressStaticClientAlertEvents = false;
            }
        }

        private static int ClampGameStartAlertRepeatCount(int value)
        {
            return Math.Clamp(value, 1, 30);
        }
    }
}
