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
        public static void ApplyTheme(Window window, bool isDarkMode)
        {
            var (mainBg, cardBg, secondaryBg, textColor, statsBg, winColor, lossColor, cardBorder) = GetThemeColors(isDarkMode);

            // Update resource dictionaries
            window.Resources["MainBackgroundBrush"] = mainBg;
            window.Resources["CardBackgroundBrush"] = cardBg;
            window.Resources["SecondaryBackgroundBrush"] = secondaryBg;
            window.Resources["TextColorBrush"] = textColor;

            // Update window background
            window.Background = isDarkMode ? 
                new SolidColorBrush(Color.FromRgb(10, 10, 16)) : 
                new SolidColorBrush(Color.FromRgb(245, 245, 250));

            // Update stats panel
            var statsPanel = window.FindName("statsPanel") as Border;
            if (statsPanel != null)
                statsPanel.Background = statsBg;

            // Update main content border
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
            
            // FIX: Also update the icLoginAccounts ItemsControl that displays the login cards!
            if (icLoginAccounts != null)
            {
                Console.WriteLine($"🎯 Updating icLoginAccounts directly...");
                
                // Clear and set directly (bypass binding)
                icLoginAccounts.ItemsSource = null;
                icLoginAccounts.ItemsSource = accounts;
                
                // Force layout update
                icLoginAccounts.UpdateLayout();
                icLoginAccounts.InvalidateVisual();
                
                Console.WriteLine($"✅ Updated icLoginAccounts with {accounts?.Count ?? 0} accounts");
                
                // Double-check by counting items
                Console.WriteLine($"icLoginAccounts.Items.Count after update: {icLoginAccounts.Items.Count}");
                
                // Try to find borders immediately after update
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
            var (totalGames, totalWins, totalLosses, winRate) = AccountService.CalculateStats(accounts);
            
            var txtTotalGames = window.FindName("txtTotalGames") as TextBlock;
            if (txtTotalGames != null)
            {
                txtTotalGames.Text = $"Total Games: {totalGames} | Wins: {totalWins} | Losses: {totalLosses} | Win Rate: {winRate:F1}%";
            }
        }
    }
} 