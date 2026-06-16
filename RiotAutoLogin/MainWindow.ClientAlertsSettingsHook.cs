using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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

            repeatCountTextBox?.LostFocus += (_, _) => SaveStaticGameStartAlertRepeatCount(repeatCountTextBox);
            repeatCountTextBox?.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter)
                    return;

                SaveStaticGameStartAlertRepeatCount(repeatCountTextBox);
                e.Handled = true;
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
