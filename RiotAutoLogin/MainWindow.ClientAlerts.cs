using RiotAutoLogin.Controls;
using RiotAutoLogin.Services;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Media;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace RiotAutoLogin
{
    public partial class MainWindow
    {
        private CancellationTokenSource? _clientAlertsCts;
        private bool _clientAlertsInitialized;
        private bool _suppressClientAlertSettingEvents;
        private bool _gameStartAlertShownForCurrentGame;
        private bool _flashWarningShownForCurrentChampSelect;
        private string _lastFlashWarningSessionKey = string.Empty;
        private string _lastPickTurnActionKey = string.Empty;
        private ClientAlertsSettingsCard? _clientAlertsSettingsCard;

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            InitializeClientAlertFeatures();
        }

        private void InitializeClientAlertFeatures()
        {
            if (_clientAlertsInitialized)
                return;

            _clientAlertsInitialized = true;
            EnsureClientAlertSettingsCard();
            UpdateClientAlertSettingsUi();
            StartClientAlertMonitor();
            Closed += (_, _) => StopClientAlertMonitor();
        }

        private void EnsureClientAlertSettingsCard()
        {
            if (_clientAlertsSettingsCard != null)
                return;

            _clientAlertsSettingsCard = clientAlertsSettingsCard;
            HookClientAlertSettingsEvents(_clientAlertsSettingsCard);
        }

        private void HookClientAlertSettingsEvents(ClientAlertsSettingsCard card)
        {
            HookAlertToggle(card.tglGameStartAlert, enabled => _hotkeySettings.GameStartAlertEnabled = enabled);
            HookAlertToggle(card.tglPickTurnAlert, enabled => _hotkeySettings.PickTurnAlertEnabled = enabled);
            HookAlertToggle(card.tglFlashSlotWarning, enabled => _hotkeySettings.FlashSlotWarningEnabled = enabled);

            card.txtGameStartAlertRepeatCount.LostFocus += (_, _) => SaveGameStartAlertRepeatCount(card.txtGameStartAlertRepeatCount);
            card.txtGameStartAlertRepeatCount.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter)
                    return;

                SaveGameStartAlertRepeatCount(card.txtGameStartAlertRepeatCount);
                e.Handled = true;
            };

            HookSoundPicker(card.gameStartSoundPicker, path => _hotkeySettings.GameStartAlertSoundPath = path);
            HookSoundPicker(card.pickTurnSoundPicker, path => _hotkeySettings.PickTurnAlertSoundPath = path);
            HookSoundPicker(card.flashWarningSoundPicker, path => _hotkeySettings.FlashSlotWarningSoundPath = path);
            HookSoundPreview(
                card.gameStartSoundPicker,
                () => SystemSounds.Hand.Play());
            HookSoundPreview(
                card.pickTurnSoundPicker,
                () => SystemSounds.Exclamation.Play());
            HookSoundPreview(
                card.flashWarningSoundPicker,
                () => SystemSounds.Exclamation.Play());

            card.rbFlashSlot1.Checked += (_, _) =>
            {
                if (_suppressClientAlertSettingEvents)
                    return;

                _hotkeySettings.PreferredFlashSlot = 1;
                SaveHotkeySettings();
                UpdateClientAlertSettingsUi();
            };

            card.rbFlashSlot2.Checked += (_, _) =>
            {
                if (_suppressClientAlertSettingEvents)
                    return;

                _hotkeySettings.PreferredFlashSlot = 2;
                SaveHotkeySettings();
                UpdateClientAlertSettingsUi();
            };
        }

        private void HookAlertToggle(ToggleButton toggle, Action<bool> setValue)
        {
            void SaveValue(bool enabled)
            {
                if (_suppressClientAlertSettingEvents)
                    return;

                setValue(enabled);
                SaveHotkeySettings();
                UpdateClientAlertSettingsUi();
            }

            toggle.Checked += (_, _) => SaveValue(true);
            toggle.Unchecked += (_, _) => SaveValue(false);
        }

        private void HookSoundPicker(AlertSoundPicker picker, Action<string> setPath)
        {
            picker.SoundChanged += (_, _) =>
            {
                if (_suppressClientAlertSettingEvents)
                    return;

                setPath(picker.SelectedPath);
                SaveHotkeySettings();
            };
        }

        private void HookSoundPreview(AlertSoundPicker picker, Action playDefault)
        {
            picker.PreviewRequested += async (_, _) =>
                await PlayConfiguredAlertSoundAsync(picker.SelectedPath, playDefault);
        }

        private void SaveGameStartAlertRepeatCount(TextBox repeatCountTextBox)
        {
            if (_suppressClientAlertSettingEvents)
                return;

            int parsedValue = int.TryParse(repeatCountTextBox.Text, out int value)
                ? value
                : _hotkeySettings.GameStartAlertRepeatCount;

            _hotkeySettings.GameStartAlertRepeatCount = ClampGameStartAlertRepeatCount(parsedValue);
            SaveHotkeySettings();
            UpdateClientAlertSettingsUi();
        }

        private static int ClampGameStartAlertRepeatCount(int value) => Math.Clamp(value, 1, 30);

        private void UpdateClientAlertSettingsUi()
        {
            if (_clientAlertsSettingsCard == null)
                return;

            if (_hotkeySettings.PreferredFlashSlot != 1 && _hotkeySettings.PreferredFlashSlot != 2)
                _hotkeySettings.PreferredFlashSlot = 2;

            _hotkeySettings.GameStartAlertRepeatCount = ClampGameStartAlertRepeatCount(_hotkeySettings.GameStartAlertRepeatCount);

            _suppressClientAlertSettingEvents = true;
            try
            {
                _clientAlertsSettingsCard.tglGameStartAlert.IsChecked = _hotkeySettings.GameStartAlertEnabled;
                _clientAlertsSettingsCard.tglGameStartAlert.Content = _hotkeySettings.GameStartAlertEnabled ? "ON" : "OFF";
                _clientAlertsSettingsCard.txtGameStartAlertRepeatCount.Text = _hotkeySettings.GameStartAlertRepeatCount.ToString();

                _clientAlertsSettingsCard.tglPickTurnAlert.IsChecked = _hotkeySettings.PickTurnAlertEnabled;
                _clientAlertsSettingsCard.tglPickTurnAlert.Content = _hotkeySettings.PickTurnAlertEnabled ? "ON" : "OFF";

                _clientAlertsSettingsCard.tglFlashSlotWarning.IsChecked = _hotkeySettings.FlashSlotWarningEnabled;
                _clientAlertsSettingsCard.tglFlashSlotWarning.Content = _hotkeySettings.FlashSlotWarningEnabled ? "ON" : "OFF";

                _clientAlertsSettingsCard.rbFlashSlot1.IsChecked = _hotkeySettings.PreferredFlashSlot == 1;
                _clientAlertsSettingsCard.rbFlashSlot2.IsChecked = _hotkeySettings.PreferredFlashSlot == 2;

                _clientAlertsSettingsCard.gameStartSoundPicker.SelectedPath = _hotkeySettings.GameStartAlertSoundPath;
                _clientAlertsSettingsCard.pickTurnSoundPicker.SelectedPath = _hotkeySettings.PickTurnAlertSoundPath;
                _clientAlertsSettingsCard.flashWarningSoundPicker.SelectedPath = _hotkeySettings.FlashSlotWarningSoundPath;
            }
            finally
            {
                _suppressClientAlertSettingEvents = false;
            }
        }

        private void StartClientAlertMonitor()
        {
            if (_clientAlertsCts != null)
                return;

            _clientAlertsCts = new CancellationTokenSource();
            Task.Run(() => MonitorClientAlertsAsync(_clientAlertsCts.Token));
        }

        private void StopClientAlertMonitor()
        {
            try
            {
                _clientAlertsCts?.Cancel();
                _clientAlertsCts?.Dispose();
                _clientAlertsCts = null;
            }
            catch { }
        }

        private async Task MonitorClientAlertsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!LCUService.CheckIfLeagueClientIsOpen())
                    {
                        _gameStartAlertShownForCurrentGame = false;
                        _flashWarningShownForCurrentChampSelect = false;
                        _lastFlashWarningSessionKey = string.Empty;
                        _lastPickTurnActionKey = string.Empty;
                        await Task.Delay(2500, cancellationToken);
                        continue;
                    }

                    string phase = await LCUService.GetCurrentGamePhaseAsync();
                    await HandleGameStartAlertAsync(phase);
                    await HandlePickTurnAlertAsync(phase);
                    await HandleFlashSlotWarningAsync(phase);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Client alert monitor error: {ex.Message}");
                }

                await Task.Delay(1500, cancellationToken);
            }
        }

        private async Task HandleGameStartAlertAsync(string phase)
        {
            if (!_hotkeySettings.GameStartAlertEnabled)
                return;

            bool gameStarted = phase is "GameStart" or "InProgress";
            if (!gameStarted)
            {
                if (phase is "Lobby" or "None" or "EndOfGame" or "PreEndOfGame" or "WaitingForStats")
                    _gameStartAlertShownForCurrentGame = false;
                return;
            }

            if (_gameStartAlertShownForCurrentGame)
                return;

            double? gameTime = await LiveGameClockService.ReadAsync();
            if (!gameTime.HasValue) return;
            double gameTimeSeconds = gameTime.Value;

            if (gameTimeSeconds >= 1.0 && gameTimeSeconds <= 20.0)
            {
                _gameStartAlertShownForCurrentGame = true;
                await ShowGameStartAlertAsync();
            }
            else if (gameTimeSeconds > 20.0)
            {
                // App was probably opened after the game had already started. Avoid late surprise sounds.
                _gameStartAlertShownForCurrentGame = true;
            }
        }

        private async Task ShowGameStartAlertAsync()
        {
            if (await AlertSoundService.TryPlayAsync(Dispatcher, _hotkeySettings.GameStartAlertSoundPath))
                return;

            int repeatCount = ClampGameStartAlertRepeatCount(_hotkeySettings.GameStartAlertRepeatCount);

            await Task.Run(async () =>
            {
                for (int i = 0; i < repeatCount; i++)
                {
                    try
                    {
                        SystemSounds.Hand.Play();
                        await Task.Delay(180);
                        SystemSounds.Exclamation.Play();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to play game start alert sound: {ex.Message}");
                    }

                    await Task.Delay(260);
                }
            });
        }

        private async Task HandlePickTurnAlertAsync(string phase)
        {
            if (!_hotkeySettings.PickTurnAlertEnabled || phase != "ChampSelect")
            {
                if (phase != "ChampSelect")
                    _lastPickTurnActionKey = string.Empty;
                return;
            }

            var pickState = ReadCurrentPickTurnState();
            if (!pickState.success || !pickState.isLocalPlayersTurn)
                return;

            if (string.Equals(_lastPickTurnActionKey, pickState.actionKey, StringComparison.Ordinal))
                return;

            _lastPickTurnActionKey = pickState.actionKey;
            _ = PlayConfiguredAlertSoundAsync(
                _hotkeySettings.PickTurnAlertSoundPath,
                () => SystemSounds.Exclamation.Play());

            await ShowTopmostAlertAsync(
                "Your Turn to Pick",
                "It is your turn to choose a champion in the current champion select.");
        }

        private static (bool success, bool isLocalPlayersTurn, string actionKey) ReadCurrentPickTurnState()
        {
            string[] sessionResult = LCUService.ClientRequest("GET", "lol-champ-select/v1/session");
            if (sessionResult.Length < 2 || !sessionResult[0].StartsWith("2"))
                return (false, false, string.Empty);

            try
            {
                using JsonDocument document = JsonDocument.Parse(sessionResult[1]);
                JsonElement root = document.RootElement;
                int localCellId = root.TryGetProperty("localPlayerCellId", out JsonElement localCellElement) &&
                                  localCellElement.TryGetInt32(out int parsedCellId)
                    ? parsedCellId
                    : -1;
                string sessionKey = TryReadSessionKey(root, out string parsedKey)
                    ? parsedKey
                    : "current-session";

                if (localCellId < 0 ||
                    !root.TryGetProperty("actions", out JsonElement actionGroups) ||
                    actionGroups.ValueKind != JsonValueKind.Array)
                {
                    return (true, false, string.Empty);
                }

                foreach (JsonElement actionGroup in actionGroups.EnumerateArray())
                {
                    if (actionGroup.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (JsonElement action in actionGroup.EnumerateArray())
                    {
                        string actionType = action.TryGetProperty("type", out JsonElement typeElement)
                            ? typeElement.GetString() ?? string.Empty
                            : string.Empty;
                        bool completed = TryGetBool(action, "completed");
                        bool isInProgress = TryGetBool(action, "isInProgress");
                        int actorCellId = TryGetInt(action, "actorCellId");

                        if (!string.Equals(actionType, "pick", StringComparison.OrdinalIgnoreCase) ||
                            actorCellId != localCellId || completed || !isInProgress)
                        {
                            continue;
                        }

                        int actionId = TryGetInt(action, "id");
                        return (true, true, $"{sessionKey}:{localCellId}:{actionId}");
                    }
                }

                return (true, false, string.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to read local pick turn: {ex.Message}");
                return (false, false, string.Empty);
            }
        }

        private async Task HandleFlashSlotWarningAsync(string phase)
        {
            if (!_hotkeySettings.FlashSlotWarningEnabled || phase != "ChampSelect")
            {
                if (phase != "ChampSelect")
                {
                    _flashWarningShownForCurrentChampSelect = false;
                    _lastFlashWarningSessionKey = string.Empty;
                }
                return;
            }

            var spellState = ReadCurrentChampSelectSpellState();
            if (!spellState.success)
                return;

            if (!string.Equals(_lastFlashWarningSessionKey, spellState.sessionKey, StringComparison.Ordinal))
            {
                _lastFlashWarningSessionKey = spellState.sessionKey;
                _flashWarningShownForCurrentChampSelect = false;
            }

            if (_flashWarningShownForCurrentChampSelect)
                return;

            const int flashId = 4;
            int preferredSlot = _hotkeySettings.PreferredFlashSlot == 1 ? 1 : 2;
            int oppositeSlot = preferredSlot == 1 ? 2 : 1;
            int flashSlot = spellState.spell1Id == flashId ? 1 : spellState.spell2Id == flashId ? 2 : 0;

            if (flashSlot == oppositeSlot)
            {
                _flashWarningShownForCurrentChampSelect = true;
                await ShowFlashSlotWarningAsync(preferredSlot, flashSlot);
            }
        }

        private Task ShowFlashSlotWarningAsync(int preferredSlot, int actualSlot)
        {
            _ = PlayConfiguredAlertSoundAsync(
                _hotkeySettings.FlashSlotWarningSoundPath,
                () => SystemSounds.Exclamation.Play());

            string preferredLabel = preferredSlot == 1 ? "Spell 1 / D" : "Spell 2 / F";
            string actualLabel = actualSlot == 1 ? "Spell 1 / D" : "Spell 2 / F";

            return ShowTopmostAlertAsync(
                "Flash Slot Warning",
                $"Flash is on the other side.\n\nPreferred: {preferredLabel}\nCurrent: {actualLabel}\n\nDo you know about this?");
        }

        private async Task PlayConfiguredAlertSoundAsync(string mediaPath, Action playDefault)
        {
            try
            {
                if (await AlertSoundService.TryPlayAsync(Dispatcher, mediaPath))
                    return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Custom alert playback error: {ex.Message}");
            }

            try { playDefault(); }
            catch (Exception ex) { Debug.WriteLine($"Default alert sound failed: {ex.Message}"); }
        }

        private Task ShowTopmostAlertAsync(string title, string message)
        {
            return Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Topmost = true;
                    Activate();
                    Topmost = false;
                    System.Windows.MessageBox.Show(this,
                        message,
                        title,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to show client alert '{title}': {ex.Message}");
                }
            }).Task;
        }

        private static (bool success, int spell1Id, int spell2Id, string sessionKey) ReadCurrentChampSelectSpellState()
        {
            string[] selectionResult = LCUService.ClientRequest("GET", "lol-champ-select/v1/session/my-selection");
            int spell1Id = 0;
            int spell2Id = 0;
            if (selectionResult[0].StartsWith("2") && TryReadSpellIds(selectionResult[1], out spell1Id, out spell2Id))
            {
                string key = ReadChampSelectSessionKey();
                return (true, spell1Id, spell2Id, key);
            }

            string[] sessionResult = LCUService.ClientRequest("GET", "lol-champ-select/v1/session");
            if (!sessionResult[0].StartsWith("2"))
                return (false, 0, 0, string.Empty);

            try
            {
                using JsonDocument doc = JsonDocument.Parse(sessionResult[1]);
                JsonElement root = doc.RootElement;
                int localCellId = root.TryGetProperty("localPlayerCellId", out JsonElement localCell) ? localCell.GetInt32() : -1;
                string sessionKey = TryReadSessionKey(root, out string parsedKey) ? parsedKey : string.Empty;

                if (root.TryGetProperty("myTeam", out JsonElement myTeam) && myTeam.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement player in myTeam.EnumerateArray())
                    {
                        if (!player.TryGetProperty("cellId", out JsonElement cell) || cell.GetInt32() != localCellId)
                            continue;

                        spell1Id = TryGetInt(player, "spell1Id");
                        spell2Id = TryGetInt(player, "spell2Id");
                        return (spell1Id > 0 || spell2Id > 0, spell1Id, spell2Id, sessionKey);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to read champ select spell state: {ex.Message}");
            }

            return (false, 0, 0, string.Empty);
        }

        private static bool TryReadSpellIds(string json, out int spell1Id, out int spell2Id)
        {
            spell1Id = 0;
            spell2Id = 0;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                spell1Id = TryGetInt(root, "spell1Id");
                spell2Id = TryGetInt(root, "spell2Id");
                return spell1Id > 0 || spell2Id > 0;
            }
            catch
            {
                return false;
            }
        }

        private static string ReadChampSelectSessionKey()
        {
            string[] sessionResult = LCUService.ClientRequest("GET", "lol-champ-select/v1/session");
            if (!sessionResult[0].StartsWith("2"))
                return string.Empty;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(sessionResult[1]);
                return TryReadSessionKey(doc.RootElement, out string sessionKey) ? sessionKey : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TryReadSessionKey(JsonElement root, out string sessionKey)
        {
            sessionKey = string.Empty;
            if (root.TryGetProperty("chatDetails", out JsonElement chatDetails) &&
                chatDetails.ValueKind == JsonValueKind.Object &&
                chatDetails.TryGetProperty("multiUserChatId", out JsonElement chatId))
            {
                sessionKey = chatId.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(sessionKey);
            }

            if (root.TryGetProperty("gameId", out JsonElement gameId))
            {
                sessionKey = gameId.ValueKind == JsonValueKind.String
                    ? gameId.GetString() ?? string.Empty
                    : gameId.GetRawText();
                return !string.IsNullOrWhiteSpace(sessionKey);
            }

            return false;
        }

        private static int TryGetInt(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
                    return number;
                if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
                    return number;
            }
            return 0;
        }

        private static bool TryGetBool(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value))
                return false;

            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return value.GetBoolean();

            return value.ValueKind == JsonValueKind.String &&
                   bool.TryParse(value.GetString(), out bool parsed) &&
                   parsed;
        }
    }
}
