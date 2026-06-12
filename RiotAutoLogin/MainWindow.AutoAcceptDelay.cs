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
