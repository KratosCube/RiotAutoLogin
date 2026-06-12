using RiotAutoLogin.Models;
using RiotAutoLogin.Services;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace RiotAutoLogin
{
    public partial class MainWindow
    {
        private TextBox? _autoAcceptDelaySecondsTextBox;
        private TextBlock? _autoAcceptDelayHintTextBlock;
        private bool _manualUpdateCheckRequested;
        private bool _manualUpdateFeedbackInitialized;
        private bool _remotePickUiInitialized;
        private readonly RemotePickServerService _remotePickServerService = new();
        private ToggleButton? _remotePickToggle;
        private TextBlock? _remotePickStatusText;
        private TextBlock? _remotePickUrlText;

        static MainWindow()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnMainWindowLoadedForAutoAcceptDelay));
        }

        private static void OnMainWindowLoadedForAutoAcceptDelay(object sender, RoutedEventArgs e)
        {
            if (sender is MainWindow window)
            {
                window.InitializeAutoAcceptDelayUi();
                window.InitializeRemotePickServerUi();
                window.InitializeManualUpdateFeedback();
            }
        }

        private void InitializeAutoAcceptDelayUi()
        {
            if (_autoAcceptDelaySecondsTextBox != null)
                return;

            AutoAcceptSettingsService.Load();

            if (FindName("tglAutoAccept") is not ToggleButton autoAcceptToggle)
                return;

            StackPanel? autoAcceptStack = FindVisualParent<StackPanel>(autoAcceptToggle);
            if (autoAcceptStack == null)
                return;

            Border delayPanel = CreateAutoAcceptDelayPanel();

            // Auto Accept card children are: title, description, toggle grid, status panel.
            // Insert the delay control between the toggle and the status text.
            int insertIndex = Math.Min(3, autoAcceptStack.Children.Count);
            autoAcceptStack.Children.Insert(insertIndex, delayPanel);
        }

        private Border CreateAutoAcceptDelayPanel()
        {
            var container = new Border
            {
                Background = TryFindResource("SecondaryBackgroundBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(30, 38, 50)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 14, 0, 0)
            };

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            textStack.Children.Add(new TextBlock
            {
                Text = "Delay before accepting",
                FontSize = 14,
                Foreground = TryFindResource("TextColorBrush") as Brush ?? Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });

            _autoAcceptDelayHintTextBlock = new TextBlock
            {
                Text = $"0 = accept immediately. Max {AutoAcceptSettingsService.MaxDelaySeconds} seconds.",
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = 12,
                Opacity = 0.68,
                Foreground = TryFindResource("TextColorBrush") as Brush ?? Brushes.White,
                TextWrapping = TextWrapping.Wrap
            };
            textStack.Children.Add(_autoAcceptDelayHintTextBlock);

            Grid.SetColumn(textStack, 0);
            root.Children.Add(textStack);

            var inputStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0)
            };

            _autoAcceptDelaySecondsTextBox = new TextBox
            {
                Width = 72,
                Text = AutoAcceptSettingsService.DelaySeconds.ToString(CultureInfo.InvariantCulture),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "How many seconds RiotAutoLogin should wait before accepting the match."
            };

            if (TryFindResource("ModernTextBox") is Style textBoxStyle)
            {
                _autoAcceptDelaySecondsTextBox.Style = textBoxStyle;
            }

            _autoAcceptDelaySecondsTextBox.PreviewTextInput += AutoAcceptDelaySecondsTextBox_PreviewTextInput;
            _autoAcceptDelaySecondsTextBox.LostFocus += (_, _) => SaveAutoAcceptDelayFromTextBox();
            _autoAcceptDelaySecondsTextBox.KeyDown += AutoAcceptDelaySecondsTextBox_KeyDown;
            DataObject.AddPastingHandler(_autoAcceptDelaySecondsTextBox, AutoAcceptDelaySecondsTextBox_Pasting);

            inputStack.Children.Add(_autoAcceptDelaySecondsTextBox);
            inputStack.Children.Add(new TextBlock
            {
                Text = "sec",
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = TryFindResource("TextColorBrush") as Brush ?? Brushes.White,
                Opacity = 0.8
            });

            Grid.SetColumn(inputStack, 1);
            root.Children.Add(inputStack);

            container.Child = root;
            return container;
        }

        private void InitializeRemotePickServerUi()
        {
            if (_remotePickUiInitialized)
                return;

            if (FindName("tglAutoAccept") is not ToggleButton autoAcceptToggle)
                return;

            StackPanel? autoAcceptStack = FindVisualParent<StackPanel>(autoAcceptToggle);
            Border? autoAcceptCard = autoAcceptStack == null ? null : FindVisualParent<Border>(autoAcceptStack);
            StackPanel? settingsStack = autoAcceptCard == null ? null : FindVisualParent<StackPanel>(autoAcceptCard);
            if (settingsStack == null)
                return;

            _remotePickUiInitialized = true;

            Border remotePickCard = CreateRemotePickCard();
            int insertIndex = autoAcceptCard == null ? settingsStack.Children.Count : settingsStack.Children.IndexOf(autoAcceptCard) + 1;
            settingsStack.Children.Insert(Math.Max(0, insertIndex), remotePickCard);

            Closed += (_, _) => _remotePickServerService.Stop();
        }

        private Border CreateRemotePickCard()
        {
            var card = new Border
            {
                Background = TryFindResource("CardBackgroundBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(20, 25, 35)),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(18),
                Margin = new Thickness(0, 0, 0, 14)
            };

            var stack = new StackPanel();
            card.Child = stack;

            stack.Children.Add(new TextBlock
            {
                Text = "Remote Pick Server",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextColorBrush") as Brush ?? Brushes.White
            });

            stack.Children.Add(new TextBlock
            {
                Text = "Start a temporary LAN page for picking champions from your phone.",
                Margin = new Thickness(0, 6, 0, 14),
                FontSize = 13,
                Opacity = 0.68,
                Foreground = TryFindResource("TextColorBrush") as Brush ?? Brushes.White,
                TextWrapping = TextWrapping.Wrap
            });

            var toggleGrid = new Grid();
            toggleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toggleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            toggleGrid.Children.Add(new TextBlock
            {
                Text = $"Enable web pick page on port {RemotePickServerService.DefaultPort}",
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = TryFindResource("TextColorBrush") as Brush ?? Brushes.White
            });

            _remotePickToggle = new ToggleButton
            {
                Width = 100,
                Content = "OFF",
                Style = TryFindResource("ModernToggleButton") as Style
            };
            _remotePickToggle.Checked += RemotePickToggle_Checked;
            _remotePickToggle.Unchecked += RemotePickToggle_Unchecked;
            Grid.SetColumn(_remotePickToggle, 1);
            toggleGrid.Children.Add(_remotePickToggle);
            stack.Children.Add(toggleGrid);

            var statusPanel = new Border
            {
                Background = TryFindResource("SecondaryBackgroundBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(30, 38, 50)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 14, 0, 0)
            };

            var statusStack = new StackPanel();
            statusPanel.Child = statusStack;

            _remotePickStatusText = new TextBlock
            {
                Text = "Remote Pick is stopped. Enable it only when you want to pick from your phone.",
                FontSize = 13,
                Foreground = TryFindResource("TextColorBrush") as Brush ?? Brushes.White,
                TextWrapping = TextWrapping.Wrap
            };
            statusStack.Children.Add(_remotePickStatusText);

            _remotePickUrlText = new TextBlock
            {
                Text = string.Empty,
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextColorBrush") as Brush ?? Brushes.White,
                TextWrapping = TextWrapping.Wrap
            };
            statusStack.Children.Add(_remotePickUrlText);

            var buttonStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var copyButton = new Button
            {
                Content = "Copy Links",
                Style = TryFindResource("SecondaryButton") as Style,
                IsEnabled = false
            };
            copyButton.Click += (_, _) =>
            {
                if (!_remotePickServerService.IsRunning)
                    return;

                Clipboard.SetText(_remotePickServerService.LocalUrlDisplay);
                UpdateRemotePickStatus(
                    "Links copied. If one address does not work, try the next one. Your phone must be on the same Wi-Fi.",
                    _remotePickServerService.LocalUrlDisplay);
            };

            _remotePickToggle.Checked += (_, _) => copyButton.IsEnabled = true;
            _remotePickToggle.Unchecked += (_, _) => copyButton.IsEnabled = false;
            buttonStack.Children.Add(copyButton);
            statusStack.Children.Add(buttonStack);

            stack.Children.Add(statusPanel);
            return card;
        }

        private async void RemotePickToggle_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                await _remotePickServerService.StartAsync();
                if (_remotePickToggle != null)
                    _remotePickToggle.Content = "ON";

                UpdateRemotePickStatus(
                    "Remote Pick is running. Try these addresses on your phone; one may be a VPN/virtual adapter, so use the Wi-Fi/LAN address that works:",
                    _remotePickServerService.LocalUrlDisplay);
            }
            catch (Exception ex)
            {
                if (_remotePickToggle != null)
                {
                    _remotePickToggle.IsChecked = false;
                    _remotePickToggle.Content = "OFF";
                }

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
            if (_remotePickToggle != null)
                _remotePickToggle.Content = "OFF";

            UpdateRemotePickStatus("Remote Pick is stopped. Enable it only when you want to pick from your phone.");
        }

        private void UpdateRemotePickStatus(string status, string? url = null)
        {
            if (_remotePickStatusText != null)
                _remotePickStatusText.Text = status;

            if (_remotePickUrlText != null)
                _remotePickUrlText.Text = url ?? string.Empty;
        }

        private void InitializeManualUpdateFeedback()
        {
            if (_manualUpdateFeedbackInitialized)
                return;

            if (FindName("btnCheckUpdates") is not Button checkUpdatesButton)
                return;

            _manualUpdateFeedbackInitialized = true;

            // The original Click handler in MainWindow.xaml.cs still performs the update check.
            // These handlers only mark the check as manual so UpdateProgressChanged can show visible feedback.
            checkUpdatesButton.PreviewMouseLeftButtonDown += (_, _) => _manualUpdateCheckRequested = true;
            checkUpdatesButton.Click += (_, _) => _manualUpdateCheckRequested = true;
            checkUpdatesButton.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter || e.Key == Key.Space)
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
                // The existing UpdateAvailable event opens the update window.
                _manualUpdateCheckRequested = false;
            }
        }

        private void AutoAcceptDelaySecondsTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SaveAutoAcceptDelayFromTextBox();
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        private void AutoAcceptDelaySecondsTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !int.TryParse(e.Text, NumberStyles.None, CultureInfo.InvariantCulture, out _);
        }

        private void AutoAcceptDelaySecondsTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.DataObject.GetDataPresent(DataFormats.Text))
            {
                e.CancelCommand();
                return;
            }

            string? pastedText = e.DataObject.GetData(DataFormats.Text) as string;
            if (!int.TryParse(pastedText, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                e.CancelCommand();
            }
        }

        private void SaveAutoAcceptDelayFromTextBox()
        {
            if (_autoAcceptDelaySecondsTextBox == null)
                return;

            if (!int.TryParse(_autoAcceptDelaySecondsTextBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int delaySeconds))
            {
                delaySeconds = 0;
            }

            int normalizedDelay = AutoAcceptSettingsService.SaveDelaySeconds(delaySeconds);
            _autoAcceptDelaySecondsTextBox.Text = normalizedDelay.ToString(CultureInfo.InvariantCulture);

            if (_autoAcceptDelayHintTextBlock != null)
            {
                _autoAcceptDelayHintTextBlock.Text = normalizedDelay == 0
                    ? $"0 = accept immediately. Max {AutoAcceptSettingsService.MaxDelaySeconds} seconds."
                    : $"RiotAutoLogin will wait {normalizedDelay} seconds, then accept only if ReadyCheck is still active.";
            }
        }

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject? parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T typedParent)
                    return typedParent;

                parent = VisualTreeHelper.GetParent(parent);
            }

            return null;
        }
    }
}
