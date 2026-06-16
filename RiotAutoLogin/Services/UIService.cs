using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Documents;
using RiotAutoLogin.Utilities;

namespace RiotAutoLogin.Services
{
    public static class UIService
    {
        private const string GreyscreenValueTag = "GreyscreenTotalValue";
        private const string GreyscreenUiAttachedTag = "GreyscreenStatsUiAttached";
        private const string GreyscreenRefreshHookTag = "GreyscreenRefreshHookAttached";

        public static void ApplyTheme(Window window, bool isDarkMode)
        {
            var (mainBg, cardBg, secondaryBg, textColor, statsBg, winColor, lossColor, cardBorder) = GetThemeColors(isDarkMode);

            window.Resources["MainBackgroundBrush"] = mainBg;
            window.Resources["CardBackgroundBrush"] = cardBg;
            window.Resources["SecondaryBackgroundBrush"] = secondaryBg;
            window.Resources["TextColorBrush"] = textColor;

            window.Background = isDarkMode ? 
                new SolidColorBrush(Color.FromRgb(10, 10, 16)) : 
                new SolidColorBrush(Color.FromRgb(245, 245, 250));

            var statsPanel = window.FindName("statsPanel") as Border;
            if (statsPanel != null)
                statsPanel.Background = statsBg;

            var mainContentBorder = VisualTreeHelperExtensions.FindVisualChildren<Border>(window)
                .FirstOrDefault(b => b.CornerRadius.TopLeft == 20);
            if (mainContentBorder != null)
                mainContentBorder.Background = mainBg;

            UpdateTabHeaders(window, isDarkMode);
            UpdateCardBackgrounds(window, cardBg, cardBorder, textColor, winColor, lossColor);
        }

        private static (SolidColorBrush main, SolidColorBrush card, SolidColorBrush secondary, 
                       SolidColorBrush text, SolidColorBrush stats, SolidColorBrush win, 
                       SolidColorBrush loss, SolidColorBrush border) GetThemeColors(bool isDarkMode)
        {
            if (isDarkMode)
            {
                return (
                    new SolidColorBrush(Color.FromRgb(18, 18, 24)),
                    new SolidColorBrush(Color.FromRgb(26, 26, 36)),
                    new SolidColorBrush(Color.FromRgb(46, 46, 58)),
                    new SolidColorBrush(Colors.White),
                    new SolidColorBrush(Color.FromRgb(34, 34, 34)),
                    new SolidColorBrush(Color.FromRgb(34, 187, 187)),
                    new SolidColorBrush(Color.FromRgb(242, 68, 5)),
                    new SolidColorBrush(Color.FromRgb(40, 40, 50))
                );
            }
            else
            {
                return (
                    new SolidColorBrush(Colors.White),
                    new SolidColorBrush(Color.FromRgb(235, 235, 240)),
                    new SolidColorBrush(Color.FromRgb(220, 220, 230)),
                    new SolidColorBrush(Colors.Black),
                    new SolidColorBrush(Color.FromRgb(225, 225, 225)),
                    new SolidColorBrush(Color.FromRgb(0, 128, 128)),
                    new SolidColorBrush(Color.FromRgb(180, 0, 0)),
                    new SolidColorBrush(Color.FromRgb(200, 200, 210))
                );
            }
        }

        private static void UpdateTabHeaders(Window window, bool isDarkMode)
        {
            var selectedTabBrush = isDarkMode ?
                new SolidColorBrush(Color.FromRgb(42, 42, 54)) :
                new SolidColorBrush(Color.FromRgb(220, 220, 230));

            var normalTabBrush = isDarkMode ?
                new SolidColorBrush(Color.FromRgb(26, 26, 36)) :
                new SolidColorBrush(Color.FromRgb(235, 235, 240));

            foreach (var tabItem in VisualTreeHelperExtensions.FindVisualChildren<TabItem>(window))
            {
                var tabHeader = VisualTreeHelperExtensions.FindVisualChildren<Border>(tabItem)
                    .FirstOrDefault(b => b.Padding.Top == 10 && b.CornerRadius.TopLeft == 10);
                
                if (tabHeader != null)
                {
                    if (tabItem.IsSelected)
                    {
                        tabHeader.Background = selectedTabBrush;
                        tabHeader.BorderThickness = new Thickness(0, 0, 0, 2);
                        tabHeader.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 82, 82));
                    }
                    else
                    {
                        tabHeader.Background = normalTabBrush;
                        tabHeader.BorderThickness = new Thickness(0);
                    }
                }
            }
        }

        private static void UpdateCardBackgrounds(Window window, SolidColorBrush cardBg, SolidColorBrush cardBorder,
            SolidColorBrush textColor, SolidColorBrush winColor, SolidColorBrush lossColor)
        {
            var icAccounts = window.FindName("icAccounts") as ItemsControl;
            if (icAccounts == null) return;

            foreach (var border in VisualTreeHelperExtensions.FindVisualChildren<Border>(icAccounts))
            {
                if (border.Tag is Models.Account)
                {
                    border.Background = cardBg;
                    border.BorderThickness = new Thickness(1);
                    border.BorderBrush = cardBorder;

                    UpdateTextBlockColors(border, textColor, winColor, lossColor);
                }
            }
        }

        private static void UpdateTextBlockColors(Border border, SolidColorBrush textColor, 
            SolidColorBrush winColor, SolidColorBrush lossColor)
        {
            foreach (var textBlock in VisualTreeHelperExtensions.FindVisualChildren<TextBlock>(border))
            {
                if (textBlock.Inlines.Count == 0)
                {
                    textBlock.Foreground = textColor;
                }
                else
                {
                    foreach (var inline in textBlock.Inlines.OfType<Run>())
                    {
                        if (inline.Text.Contains("W/") || inline.Text.Contains("L") || inline.Text.Contains("LP"))
                        {
                            inline.Foreground = textColor;
                        }
                        else if (inline.PreviousInline is Run prev)
                        {
                            inline.Foreground = prev.Text switch
                            {
                                " LP, " => winColor,
                                "W/" => lossColor,
                                _ => textColor
                            };
                        }
                    }
                }
            }
        }

        public static void RefreshAccountLists(Window window, System.Collections.Generic.List<Models.Account> accounts)
        {
            Console.WriteLine($"🔧 UIService.RefreshAccountLists called with {accounts?.Count ?? 0} accounts");
            
            var lbAccounts = window.FindName("lbAccounts") as ListBox;
            var lbLoginAccounts = window.FindName("lbLoginAccounts") as ListBox;
            var icLoginAccounts = window.FindName("icLoginAccounts") as ItemsControl;

            if (lbAccounts != null)
            {
                lbAccounts.ItemsSource = null;
                lbAccounts.ItemsSource = accounts;
                Console.WriteLine($"Updated lbAccounts with {accounts?.Count ?? 0} accounts");
            }

            if (lbLoginAccounts != null)
            {
                lbLoginAccounts.ItemsSource = null;
                lbLoginAccounts.ItemsSource = accounts;
                Console.WriteLine($"Updated lbLoginAccounts with {accounts?.Count ?? 0} accounts");
            }
            
            if (icLoginAccounts != null)
            {
                Console.WriteLine($"🎯 Updating icLoginAccounts directly...");
                icLoginAccounts.ItemsSource = null;
                icLoginAccounts.ItemsSource = accounts;
                icLoginAccounts.UpdateLayout();
                icLoginAccounts.InvalidateVisual();
                Console.WriteLine($"✅ Updated icLoginAccounts with {accounts?.Count ?? 0} accounts");
                Console.WriteLine($"icLoginAccounts.Items.Count after update: {icLoginAccounts.Items.Count}");
                
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var borders = VisualTreeHelperExtensions.FindVisualChildren<Border>(icLoginAccounts).ToList();
                    Console.WriteLine($"🔍 Found {borders.Count} borders immediately after icLoginAccounts update");
                    
                    foreach (var border in borders.Take(2))
                    {
                        if (border.Tag is Models.Account acc)
                        {
                            Console.WriteLine($"  ✅ Border with account: {acc.GameName}");
                        }
                        else
                        {
                            Console.WriteLine($"  ❌ Border without account tag: {border.Tag?.GetType().Name ?? "null"}");
                        }
                    }
                }), System.Windows.Threading.DispatcherPriority.Render);
            }
            else
            {
                Console.WriteLine("⚠️ icLoginAccounts not found!");
            }
        }

        public static void UpdateTotalGameStats(Window window, System.Collections.Generic.List<Models.Account> accounts)
        {
            EnsureGreyscreenStatsUi(window, accounts);

            var (totalGames, totalWins, totalLosses, winRate, totalGreyscreens, totalGreyscreenSeconds) = AccountService.CalculateStats(accounts);

            var txtStatsGamesValue = window.FindName("txtStatsGamesValue") as TextBlock;
            var txtStatsWinsValue = window.FindName("txtStatsWinsValue") as TextBlock;
            var txtStatsLossesValue = window.FindName("txtStatsLossesValue") as TextBlock;
            var txtStatsWinRateValue = window.FindName("txtStatsWinRateValue") as TextBlock;
            var txtStatsGreyscreensValue = window.FindName("txtStatsGreyscreenTimeValue") as TextBlock ?? FindTaggedTextBlock(window, GreyscreenValueTag);
            var txtTotalGames = window.FindName("txtTotalGames") as TextBlock;

            if (txtStatsGamesValue != null)
                txtStatsGamesValue.Text = totalGames.ToString();

            if (txtStatsWinsValue != null)
                txtStatsWinsValue.Text = totalWins.ToString();

            if (txtStatsLossesValue != null)
                txtStatsLossesValue.Text = totalLosses.ToString();

            if (txtStatsWinRateValue != null)
                txtStatsWinRateValue.Text = $"{winRate:F1}%";

            if (txtStatsGreyscreensValue != null)
                txtStatsGreyscreensValue.Text = totalGreyscreenSeconds > 0 ? FormatGreyscreenDuration(totalGreyscreenSeconds) : "0 min";

            if (txtTotalGames != null)
                txtTotalGames.Text = $"Total Games: {totalGames} | Wins: {totalWins} | Losses: {totalLosses} | Win Rate: {winRate:F1}% | Greyscreen Time: {FormatGreyscreenDuration(totalGreyscreenSeconds)} | Deaths: {totalGreyscreens}";
        }

        private static void EnsureGreyscreenStatsUi(Window window, System.Collections.Generic.List<Models.Account> accounts)
        {
            HookStatsRefreshButton(window, accounts);

            if (window.FindName("txtStatsGreyscreenTimeValue") is TextBlock)
                return;

            if (window.FindName("statsPanel") is not Border statsPanel || statsPanel.Child is not Grid grid)
                return;

            if (Equals(statsPanel.Tag, GreyscreenUiAttachedTag))
                return;

            const int insertColumn = 4;
            grid.ColumnDefinitions.Insert(insertColumn, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foreach (UIElement child in grid.Children)
            {
                int column = Grid.GetColumn(child);
                if (column >= insertColumn)
                    Grid.SetColumn(child, column + 1);
            }

            var greyscreenValue = new TextBlock
            {
                Tag = GreyscreenValueTag,
                Text = "0 min",
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(182, 109, 255))
            };

            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(20, 27, 36)),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 10, 0),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Greyscreen Time",
                            FontSize = 11,
                            Opacity = 0.65,
                            Foreground = window.Resources["TextColorBrush"] as Brush ?? Brushes.White
                        },
                        greyscreenValue
                    }
                }
            };

            Grid.SetColumn(card, insertColumn);
            grid.Children.Add(card);
            statsPanel.Tag = GreyscreenUiAttachedTag;
        }

        private static void HookStatsRefreshButton(Window window, System.Collections.Generic.List<Models.Account> accounts)
        {
            if (window.FindName("btnRefreshQuickStats") is not Button button)
                return;

            if (Equals(button.Tag, GreyscreenRefreshHookTag))
                return;

            button.Tag = GreyscreenRefreshHookTag;
            button.Click += async (_, _) => await SyncGreyscreensAsync(window, accounts, showMessageOnSuccess: false);
        }

        private static async System.Threading.Tasks.Task SyncGreyscreensAsync(Window window, System.Collections.Generic.List<Models.Account> accounts, bool showMessageOnSuccess)
        {
            LcuGreyscreenStatsResult result = await LcuGreyscreenStatsService.GetCurrentAccountGreyscreensAsync();
            if (!result.Success)
            {
                if (showMessageOnSuccess)
                    MessageBox.Show(result.Message, "Greyscreen Sync", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Models.Account? account = FindSavedAccountForGreyscreenSync(accounts, result);
            if (account == null)
            {
                if (showMessageOnSuccess)
                {
                    string riotId = string.IsNullOrWhiteSpace(result.TagLine) ? result.GameName : $"{result.GameName}#{result.TagLine}";
                    MessageBox.Show($"Synced greyscreen data for {riotId}, but no matching saved account was found.", "Greyscreen Sync", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            account.Greyscreens = result.Greyscreens;
            account.GreyscreenSeconds = result.GreyscreenSeconds;
            account.GreyscreensLastUpdatedUtc = DateTime.UtcNow.ToString("O");
            AccountService.SaveAccounts(accounts);
            RefreshAccountLists(window, accounts);
            UpdateTotalGameStats(window, accounts);

            if (showMessageOnSuccess)
            {
                string savedRiotId = string.IsNullOrWhiteSpace(account.TagLine) ? account.GameName : $"{account.GameName}#{account.TagLine}";
                string formattedTime = FormatGreyscreenDuration(result.GreyscreenSeconds);
                MessageBox.Show($"Saved greyscreen time for {savedRiotId}: {formattedTime} ({result.Greyscreens} deaths).\nSource: {result.Source}", "Greyscreen Sync", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private static string FormatGreyscreenDuration(long seconds)
        {
            if (seconds <= 0)
                return "0 min";

            if (seconds < 60)
                return $"{seconds}s";

            long minutes = seconds / 60;
            if (minutes < 120)
                return $"{minutes} min";

            double hours = seconds / 3600.0;
            if (hours < 48)
                return $"{hours:F1} h";

            double days = seconds / 86400.0;
            return $"{days:F1} d";
        }

        private static Models.Account? FindSavedAccountForGreyscreenSync(System.Collections.Generic.List<Models.Account> accounts, LcuGreyscreenStatsResult result)
        {
            string gameName = NormalizeRiotIdPart(result.GameName);
            string tagLine = NormalizeRiotIdPart(result.TagLine);

            Models.Account? exact = accounts.FirstOrDefault(account =>
                NormalizeRiotIdPart(account.GameName) == gameName &&
                (string.IsNullOrEmpty(tagLine) || NormalizeRiotIdPart(account.TagLine) == tagLine));

            if (exact != null)
                return exact;

            return accounts.FirstOrDefault(account => NormalizeRiotIdPart(account.GameName) == gameName);
        }

        private static string NormalizeRiotIdPart(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static TextBlock? FindTaggedTextBlock(Window window, string tag)
        {
            return VisualTreeHelperExtensions.FindVisualChildren<TextBlock>(window)
                .FirstOrDefault(textBlock => Equals(textBlock.Tag, tag));
        }
    }
}
