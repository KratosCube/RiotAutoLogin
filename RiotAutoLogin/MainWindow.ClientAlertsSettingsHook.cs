using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace RiotAutoLogin
{
    public partial class MainWindow
    {
        private bool _staticClientAlertControlsHooked;
        private bool _suppressStaticClientAlertEvents;

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            Dispatcher.BeginInvoke(new Action(StartClientAlertsSettingsRetryTimer), DispatcherPriority.Loaded);
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

                HookStaticClientAlertSettingsControls();
                UpdateStaticClientAlertSettingsUi();

                EnsureClientAlertSettingsCard();
                UpdateClientAlertSettingsUi();

                if (attempts >= 30)
                    timer.Stop();
            };

            timer.Start();
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

        private void UpdateStaticClientAlertSettingsUi()
        {
            if (FindName("tglGameStartAlert") is not ToggleButton gameStartToggle ||
                FindName("tglFlashSlotWarning") is not ToggleButton flashWarningToggle ||
                FindName("rbFlashSlot1") is not RadioButton flashSlot1 ||
                FindName("rbFlashSlot2") is not RadioButton flashSlot2)
            {
                return;
            }

            if (_hotkeySettings.PreferredFlashSlot != 1 && _hotkeySettings.PreferredFlashSlot != 2)
                _hotkeySettings.PreferredFlashSlot = 2;

            _suppressStaticClientAlertEvents = true;
            try
            {
                gameStartToggle.IsChecked = _hotkeySettings.GameStartAlertEnabled;
                gameStartToggle.Content = _hotkeySettings.GameStartAlertEnabled ? "ON" : "OFF";

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
    }
}
