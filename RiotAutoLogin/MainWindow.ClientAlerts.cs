using RiotAutoLogin.Controls;
using RiotAutoLogin.Services;
using RiotAutoLogin.Utilities;
using System;
using System.Diagnostics;
using System.Linq;
using System.Media;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace RiotAutoLogin
{
    public partial class MainWindow
    {
        private CancellationTokenSource? _clientAlertsCts;
        private bool _clientAlertsInitialized;
        private bool _suppressClientAlertSettingEvents;
        private bool _gameStartAlertShownForCurrentGame;
        private bool _flashWarningShownForCurrentChampSelect;
        private string _lastClientAlertPhase = string.Empty;
        private string _lastFlashWarningSessionKey = string.Empty;
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
            if (SettingsTab == null)
                return;

            if (_clientAlertsSettingsCard != null)
                return;

            StackPanel? settingsStack = FindSettingsRootStackPanel();
            if (settingsStack == null)
                return;

            ClientAlertsSettingsCard? existingCard = settingsStack.Children
                .OfType<ClientAlertsSettingsCard>()
                .FirstOrDefault();

            _clientAlertsSettingsCard = existingCard ?? new ClientAlertsSettingsCard();

            if (existingCard == null)
            {
                int insertIndex = Math.Min(3, settingsStack.Children.Count);
                settingsStack.Children.Insert(insertIndex, _clientAlertsSettingsCard);
            }

            HookClientAlertSettingsEvents(_clientAlertsSettingsCard);
        }

        private StackPanel? FindSettingsRootStackPanel()
        {
            ScrollViewer? scrollViewer = VisualTreeHelperExtensions
                .FindVisualChildren<ScrollViewer>(SettingsTab)
                .FirstOrDefault(viewer => viewer.Content is StackPanel);

            return scrollViewer?.Content as StackPanel;
        }

        private void HookClientAlertSettingsEvents(ClientAlertsSettingsCard card)
        {
            card.tglGameStartAlert.Checked += (_, _) =>
            {
                if (_suppressClientAlertSettingEvents)
                    return;

                _hotkeySettings.GameStartAlertEnabled = true;
                SaveHotkeySettings();
                UpdateClientAlertSettingsUi();
            };

            card.tglGameStartAlert.Unchecked += (_, _) =>
            {
                if (_suppressClientAlertSettingEvents)
                    return;

                _hotkeySettings.GameStartAlertEnabled = false;
                SaveHotkeySettings();
                UpdateClientAlertSettingsUi();
            };

            card.tglFlashSlotWarning.Checked += (_, _) =>
            {
                if (_suppressClientAlertSettingEvents)
                    return;

                _hotkeySettings.FlashSlotWarningEnabled = true;
                SaveHotkeySettings();
                UpdateClientAlertSettingsUi();
            };

            card.tglFlashSlotWarning.Unchecked += (_, _) =>
            {
                if (_suppressClientAlertSettingEvents)
                    return;

                _hotkeySettings.FlashSlotWarningEnabled = false;
                SaveHotkeySettings();
                UpdateClientAlertSettingsUi();
            };

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

        private void UpdateClientAlertSettingsUi()
        {
            if (_clientAlertsSettingsCard == null)
                return;

            if (_hotkeySettings.PreferredFlashSlot != 1 && _hotkeySettings.PreferredFlashSlot != 2)
                _hotkeySettings.PreferredFlashSlot = 2;

            _suppressClientAlertSettingEvents = true;
            try
            {
                _clientAlertsSettingsCard.tglGameStartAlert.IsChecked = _hotkeySettings.GameStartAlertEnabled;
                _clientAlertsSettingsCard.tglGameStartAlert.Content = _hotkeySettings.GameStartAlertEnabled ? "ON" : "OFF";

                _clientAlertsSettingsCard.tglFlashSlotWarning.IsChecked = _hotkeySettings.FlashSlotWarningEnabled;
                _clientAlertsSettingsCard.tglFlashSlotWarning.Content = _hotkeySettings.FlashSlotWarningEnabled ? "ON" : "OFF";

                _clientAlertsSettingsCard.rbFlashSlot1.IsChecked = _hotkeySettings.PreferredFlashSlot == 1;
                _clientAlertsSettingsCard.rbFlashSlot2.IsChecked = _hotkeySettings.PreferredFlashSlot == 2;
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
                        _lastClientAlertPhase = string.Empty;
                        _gameStartAlertShownForCurrentGame = false;
                        _flashWarningShownForCurrentChampSelect = false;
                        _lastFlashWarningSessionKey = string.Empty;
                        await Task.Delay(2500, cancellationToken);
                        continue;
                    }

                    string phase = await LCUService.GetCurrentGamePhaseAsync();
                    await HandleGameStartAlertAsync(phase);
                    await HandleFlashSlotWarningAsync(phase);
                    _lastClientAlertPhase = phase;
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
            bool wasGameStarted = _lastClientAlertPhase is "GameStart" or "InProgress";
            bool hadKnownPreviousPhase = !string.IsNullOrWhiteSpace(_lastClientAlertPhase);

            if (gameStarted && hadKnownPreviousPhase && !wasGameStarted && !_gameStartAlertShownForCurrentGame)
            {
                _gameStartAlertShownForCurrentGame = true;
                await ShowGameStartAlertAsync();
            }
            else if (!gameStarted && phase is "Lobby" or "None" or "EndOfGame" or "PreEndOfGame" or "WaitingForStats")
            {
                _gameStartAlertShownForCurrentGame = false;
            }
        }

        private Task ShowGameStartAlertAsync()
        {
            _ = Task.Run(async () =>
            {
                for (int i = 0; i < 10; i++)
                {
                    try { SystemSounds.Hand.Play(); } catch { }
                    await Task.Delay(260);
                }
            });

            return Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Topmost = true;
                    Activate();
                    Focus();
                    Topmost = false;
                    System.Windows.MessageBox.Show(this,
                        "GAME STARTED!\n\nLoading screen / in-game phase detected.",
                        "Riot Auto Login - Game Start Alert",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to show game start alert: {ex.Message}");
                }
            }).Task;
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
            try { SystemSounds.Exclamation.Play(); } catch { }

            string preferredLabel = preferredSlot == 1 ? "Spell 1 / D" : "Spell 2 / F";
            string actualLabel = actualSlot == 1 ? "Spell 1 / D" : "Spell 2 / F";

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
                        $"Flash is on the other side.\n\nPreferred: {preferredLabel}\nCurrent: {actualLabel}\n\nDo you know about this?",
                        "Flash Slot Warning",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to show flash slot warning: {ex.Message}");
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
                chatDetails.TryGetProperty("multiUserChatId", out JsonElement chatId))
            {
                sessionKey = chatId.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(sessionKey);
            }

            if (root.TryGetProperty("timer", out JsonElement timer) && timer.TryGetProperty("internalNowInEpochMs", out JsonElement now))
            {
                sessionKey = now.GetRawText();
                return true;
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
    }
}
