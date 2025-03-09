using RiotAutoLogin.Models;
using RiotAutoLogin.Services;
using RiotAutoLogin.Utilities;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace RiotAutoLogin
{
    public partial class MainWindow : Window
    {
        // DllImports to bring a window to the front.
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_RESTORE = 9;

        // Data and configuration fields.
        private List<Account> _accounts = new List<Account>();
        private readonly string _configFilePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RiotClientAutoLogin", "accounts.json");

        private bool _isDarkMode = true;
        private string _selectedAvatarPath = string.Empty;
        private string _selectedRegion = "eun1";
        private Border _lastSelectedCard;

        // For mapping an account to its card border (used in selection highlighting).
        private Dictionary<Account, Border> _accountCardMap = new Dictionary<Account, Border>();

        public MainWindow()
        {
            InitializeComponent();
            LoadAccounts();
            RefreshAccountLists();
            UpdateTotalGameStats();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadAccounts();
            RefreshAccountLists();
            UpdateTotalGameStats();

            // Deferred API key loading and account update.
            await Task.Run(async () =>
            {
                try
                {
                    var apiKeyTextBox = this.FindName("txtApiKey") as TextBox;
                    if (apiKeyTextBox != null)
                    {
                        string apiKey = await Task.Run(() => ApiKeyManager.GetApiKey());
                        if (!string.IsNullOrEmpty(apiKey))
                        {
                            apiKeyTextBox.Text = "••••••••••••••••••••" + apiKey.Substring(Math.Max(0, apiKey.Length - 4));
                            apiKeyTextBox.ToolTip = "API key is saved. Enter a new key to update.";
                        }
                    }
                    await UpdateAllAccountsAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error in deferred loading: " + ex.Message);
                }
            });
        }

        #region Window Control Event Handlers

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void btnSelectAvatar_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image files (*.jpg, *.jpeg, *.png)|*.jpg;*.jpeg;*.png"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                _selectedAvatarPath = openFileDialog.FileName;
                // Update avatar preview.
                imgAvatarPreview.Source = new BitmapImage(new Uri(_selectedAvatarPath, UriKind.Absolute));
            }
        }

        private void btnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void btnToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            _isDarkMode = !_isDarkMode;
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            // Create new brushes for the theme.
            SolidColorBrush mainBackgroundBrush;
            SolidColorBrush cardBackgroundBrush;
            SolidColorBrush secondaryBackgroundBrush;
            SolidColorBrush textColorBrush;
            SolidColorBrush statsPanelBrush;
            SolidColorBrush winColorBrush;
            SolidColorBrush lossColorBrush;
            SolidColorBrush cardBorderBrush;

            if (_isDarkMode)
            {
                this.Background = new SolidColorBrush(Color.FromRgb(10, 10, 16));
                mainBackgroundBrush = new SolidColorBrush(Color.FromRgb(18, 18, 24));
                cardBackgroundBrush = new SolidColorBrush(Color.FromRgb(26, 26, 36));
                secondaryBackgroundBrush = new SolidColorBrush(Color.FromRgb(46, 46, 58));
                textColorBrush = new SolidColorBrush(Colors.White);
                statsPanelBrush = new SolidColorBrush(Color.FromRgb(34, 34, 34));
                winColorBrush = new SolidColorBrush(Color.FromRgb(34, 187, 187));
                lossColorBrush = new SolidColorBrush(Color.FromRgb(242, 68, 5));
                cardBorderBrush = new SolidColorBrush(Color.FromRgb(40, 40, 50));
            }
            else
            {
                this.Background = new SolidColorBrush(Color.FromRgb(245, 245, 250));
                mainBackgroundBrush = new SolidColorBrush(Colors.White);
                cardBackgroundBrush = new SolidColorBrush(Color.FromRgb(235, 235, 240));
                secondaryBackgroundBrush = new SolidColorBrush(Color.FromRgb(220, 220, 230));
                textColorBrush = new SolidColorBrush(Colors.Black);
                statsPanelBrush = new SolidColorBrush(Color.FromRgb(225, 225, 225));
                winColorBrush = new SolidColorBrush(Color.FromRgb(0, 128, 128));
                lossColorBrush = new SolidColorBrush(Color.FromRgb(180, 0, 0));
                cardBorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 210));
            }

            // Update resource dictionaries.
            this.Resources["MainBackgroundBrush"] = mainBackgroundBrush;
            this.Resources["CardBackgroundBrush"] = cardBackgroundBrush;
            this.Resources["SecondaryBackgroundBrush"] = secondaryBackgroundBrush;
            this.Resources["TextColorBrush"] = textColorBrush;

            // Update stats panel background.
            var statsPanel = FindName("statsPanel") as Border;
            if (statsPanel != null)
            {
                statsPanel.Background = statsPanelBrush;
            }

            // Update main content border background.
            var mainContentBorder = VisualTreeHelperExtensions.FindVisualChildren<Border>(this)
                                    .FirstOrDefault(b => b.CornerRadius.TopLeft == 20);
            if (mainContentBorder != null)
            {
                mainContentBorder.Background = mainBackgroundBrush;
            }

            // Update tab headers.
            UpdateTabHeaders();

            // Refresh account lists.
            var accounts = _accounts.ToList();
            lbAccounts.ItemsSource = null;
            lbLoginAccounts.ItemsSource = null;
            lbAccounts.ItemsSource = accounts;
            lbLoginAccounts.ItemsSource = accounts;

            // Directly update card backgrounds.
            UpdateCardBackgrounds(cardBackgroundBrush, cardBorderBrush, textColorBrush, winColorBrush, lossColorBrush);

            UpdateLayout();
        }

        private void UpdateCardBackgrounds(SolidColorBrush cardBackground, SolidColorBrush cardBorder,
            SolidColorBrush textColor, SolidColorBrush winColor, SolidColorBrush lossColor)
        {
            // Update all account card borders.
            foreach (var border in VisualTreeHelperExtensions.FindVisualChildren<Border>(this))
            {
                if (border.Padding.Left == 20 && border.Padding.Top == 20 &&
                    border.CornerRadius.TopLeft == 10)
                {
                    border.Background = cardBackground;
                    border.BorderThickness = new Thickness(1);
                    border.BorderBrush = cardBorder;

                    foreach (var textBlock in VisualTreeHelperExtensions.FindVisualChildren<TextBlock>(border))
                    {
                        if (textBlock.Inlines.Count == 0)
                        {
                            textBlock.Foreground = textColor;
                        }
                        else
                        {
                            foreach (var inline in textBlock.Inlines)
                            {
                                if (inline is Run run)
                                {
                                    if (run.Text == "W/" || run.Text == "L" || run.Text.Contains("LP"))
                                    {
                                        run.Foreground = textColor;
                                    }
                                    else if (run.PreviousInline != null)
                                    {
                                        var prevText = (run.PreviousInline as Run)?.Text;
                                        if (prevText == " LP, ")
                                        {
                                            run.Foreground = winColor;
                                        }
                                        else if (prevText == "W/")
                                        {
                                            run.Foreground = lossColor;
                                        }
                                        else
                                        {
                                            run.Foreground = textColor;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private void UpdateTabHeaders()
        {
            var selectedTabBrush = _isDarkMode ?
                new SolidColorBrush(Color.FromRgb(42, 42, 54)) :
                new SolidColorBrush(Color.FromRgb(220, 220, 230));

            var normalTabBrush = _isDarkMode ?
                new SolidColorBrush(Color.FromRgb(26, 26, 36)) :
                new SolidColorBrush(Color.FromRgb(235, 235, 240));

            foreach (var tabItem in VisualTreeHelperExtensions.FindVisualChildren<TabItem>(this))
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

        #endregion

        #region Manage Accounts Event Handlers

        private void btnAddAccount_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtAccountLogin.Text) ||
                string.IsNullOrWhiteSpace(txtGameName.Text) ||
                string.IsNullOrWhiteSpace(txtTagLine.Text) ||
                string.IsNullOrWhiteSpace(txtAccountPassword.Password))
            {
                MessageBox.Show("Please enter account login, game name, tagline, and password.",
                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string region = _selectedRegion;
            var newAccount = new Account
            {
                AccountName = txtAccountLogin.Text,
                GameName = txtGameName.Text,
                TagLine = txtTagLine.Text,
                Region = region,
                EncryptedPassword = EncryptionService.EncryptString(txtAccountPassword.Password),
                AvatarPath = _selectedAvatarPath
            };

            _accounts.Add(newAccount);
            SaveAccounts();
            RefreshAccountLists();
            UpdateTotalGameStats();

            MessageBox.Show("Account added successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void btnUpdateAccount_Click(object sender, RoutedEventArgs e)
        {
            if (lbAccounts.SelectedItem is Account selected)
            {
                string region = _selectedRegion;
                selected.AccountName = txtAccountLogin.Text;
                selected.GameName = txtGameName.Text;
                selected.TagLine = txtTagLine.Text;
                selected.Region = region;
                if (!string.IsNullOrWhiteSpace(txtAccountPassword.Password))
                {
                    selected.EncryptedPassword = EncryptionService.EncryptString(txtAccountPassword.Password);
                }
                if (!string.IsNullOrWhiteSpace(_selectedAvatarPath))
                {
                    selected.AvatarPath = _selectedAvatarPath;
                }
                SaveAccounts();
                RefreshAccountLists();
                UpdateTotalGameStats();
            }
            else
            {
                MessageBox.Show("Select an account from the list to update.");
            }
        }

        private void btnDeleteAccount_Click(object sender, RoutedEventArgs e)
        {
            if (lbAccounts.SelectedItem is Account selected)
            {
                _accounts.Remove(selected);
                SaveAccounts();
                RefreshAccountLists();
                UpdateTotalGameStats();
            }
            else
            {
                MessageBox.Show("Select an account from the list to delete.");
            }
        }

        private void lbAccounts_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lbAccounts.SelectedItem is Account selected)
            {
                txtAccountLogin.Text = selected.AccountName;
                txtGameName.Text = selected.GameName;
                txtTagLine.Text = selected.TagLine;
                txtAccountPassword.Password = string.Empty;
                _selectedAvatarPath = selected.AvatarPath;
                UpdateAvatarPreview(selected.AvatarPath);

                if (_accountCardMap.TryGetValue(selected, out Border selectedBorder))
                {
                    if (_lastSelectedCard != null)
                    {
                        _lastSelectedCard.BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 74));
                        _lastSelectedCard.BorderThickness = new Thickness(1);
                        _lastSelectedCard.Background = (SolidColorBrush)Resources["CardBackgroundBrush"];
                    }
                    selectedBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 82, 82));
                    selectedBorder.Background = new SolidColorBrush(Color.FromRgb(42, 42, 54));
                    selectedBorder.BorderThickness = new Thickness(2);
                    _lastSelectedCard = selectedBorder;
                }
            }
        }

        private void UpdateAvatarPreview(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                imgAvatarPreview.Source = null;
                return;
            }

            try
            {
                BitmapImage bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 60;
                bmp.EndInit();
                bmp.Freeze();
                imgAvatarPreview.Source = bmp;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error loading avatar: " + ex.Message);
                imgAvatarPreview.Source = null;
            }
        }

        private void RefreshAccountLists()
        {
            lbAccounts.ItemsSource = null;
            lbAccounts.ItemsSource = _accounts;
            lbLoginAccounts.ItemsSource = null;
            lbLoginAccounts.ItemsSource = _accounts;
        }

        private void LoadAccounts()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    _accounts = JsonSerializer.Deserialize<List<Account>>(json) ?? new List<Account>();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading accounts: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveAccounts()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_configFilePath)!);
                string json = JsonSerializer.Serialize(_accounts, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFilePath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving accounts: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Login Tab Event Handlers

        private void AccountCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border clickedBorder && clickedBorder.Tag is Account account)
            {
                if (clickedBorder.Name == "loginCard" ||
                    VisualTreeHelperExtensions.FindVisualParent<ItemsControl>(clickedBorder)?.Name == "icLoginAccounts")
                {
                    lbLoginAccounts.SelectedItem = account;
                    if (e.ClickCount >= 2)
                    {
                        Process[] processes = Process.GetProcessesByName("Riot Client");
                        if (processes.Length > 0)
                        {
                            IntPtr hWnd = processes[0].MainWindowHandle;
                            if (hWnd != IntPtr.Zero)
                            {
                                ShowWindow(hWnd, SW_RESTORE);
                                SetForegroundWindow(hWnd);
                            }
                        }
                        _ = RiotClientAutomationService.LaunchAndLoginAsync(
                            account.AccountName,
                            EncryptionService.DecryptString(account.EncryptedPassword));
                    }
                }
                else
                {
                    lbAccounts.SelectedItem = account;
                }
            }
        }

        private async void lbLoginAccounts_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lbLoginAccounts.SelectedItem is Account account)
            {
                Process[] processes = Process.GetProcessesByName("Riot Client");
                if (processes.Length > 0)
                {
                    IntPtr hWnd = processes[0].MainWindowHandle;
                    if (hWnd != IntPtr.Zero)
                    {
                        ShowWindow(hWnd, SW_RESTORE);
                        SetForegroundWindow(hWnd);
                    }
                }
                await RiotClientAutomationService.LaunchAndLoginAsync(
                    account.AccountName,
                    EncryptionService.DecryptString(account.EncryptedPassword));
            }
        }

        private async void btnCheckRank_Click(object sender, RoutedEventArgs e)
        {
            await UpdateAllAccountsAsync();
        }

        #endregion

        #region Update All Accounts

        private async Task UpdateAllAccountsAsync()
        {
            var updateTasks = _accounts.Select(async account =>
            {
                string gameName = account.GameName;
                string tagLine = account.TagLine;
                string region = account.Region;
                string rankResult = await RiotClientAutomationService.GetRankAsync(gameName, tagLine, region);
                UpdateAccountWithRankInfo(account, rankResult);
            }).ToList();

            await Task.WhenAll(updateTasks);
            RefreshAccountLists();
            UpdateTotalGameStats();
            SaveAccounts();
        }

        private void UpdateAccountWithRankInfo(Account account, string rankResult)
        {
            account.RankInfo = rankResult;
            if (!rankResult.Contains("Error") && !rankResult.Contains("Unranked"))
            {
                int lpStart = rankResult.IndexOf('(');
                int lpEnd = rankResult.IndexOf(" LP");
                if (lpStart != -1 && lpEnd != -1 && lpEnd > lpStart)
                {
                    string lpText = rankResult.Substring(lpStart + 1, lpEnd - lpStart - 1).Trim();
                    if (int.TryParse(lpText, out int lp))
                        account.LeaguePoints = lp;
                }
                int winsStart = rankResult.IndexOf(",") + 1;
                int winsEnd = rankResult.IndexOf("W/");
                if (winsStart != -1 && winsEnd != -1 && winsEnd > winsStart)
                {
                    string winsText = rankResult.Substring(winsStart, winsEnd - winsStart).Trim();
                    if (int.TryParse(winsText, out int wins))
                        account.Wins = wins;
                }
                int lossesStart = rankResult.IndexOf("W/") + 2;
                int lossesEnd = rankResult.IndexOf("L", lossesStart);
                if (lossesStart != -1 && lossesEnd != -1 && lossesEnd > lossesStart)
                {
                    string lossesText = rankResult.Substring(lossesStart, lossesEnd - lossesStart).Trim();
                    if (int.TryParse(lossesText, out int losses))
                        account.Losses = losses;
                }
            }
            else
            {
                account.LeaguePoints = 0;
                account.Wins = 0;
                account.Losses = 0;
            }
        }

        private void UpdateTotalGameStats()
        {
            int totalWins = _accounts.Sum(a => a.Wins);
            int totalLosses = _accounts.Sum(a => a.Losses);
            int totalGames = totalWins + totalLosses;
            double winRate = totalGames > 0 ? Math.Round((double)totalWins / totalGames * 100, 1) : 0;
            txtTotalGames.Text = $"Total Games: {totalGames} | Wins: {totalWins} | Losses: {totalLosses} | Win Rate: {winRate}%";
        }

        #endregion

        #region Riot Client Automation and Settings

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl tabControl && tabControl.SelectedItem is TabItem selectedTab)
            {
                if (selectedTab.Header.ToString().ToUpper() == "SETTINGS")
                {
                    try
                    {
                        var apiKeyTextBox = VisualTreeHelperExtensions.FindVisualChildren<TextBox>(selectedTab)
                            .FirstOrDefault(tb => tb.Name == "txtApiKey");
                        if (apiKeyTextBox != null)
                        {
                            string apiKey = ApiKeyManager.GetApiKey();
                            if (!string.IsNullOrEmpty(apiKey))
                            {
                                apiKeyTextBox.Text = "••••••••••••••••••••" + apiKey.Substring(Math.Max(0, apiKey.Length - 4));
                                apiKeyTextBox.ToolTip = "API key is saved. Enter a new key to update.";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Error loading API key: " + ex.Message);
                    }
                }
            }
        }

        private void btnSaveApiKey_Click(object sender, RoutedEventArgs e)
        {
            var apiKeyTextBox = VisualTreeHelperExtensions.FindVisualChildren<TextBox>(this)
                .FirstOrDefault(tb => tb.Name == "txtApiKey");
            if (apiKeyTextBox == null)
            {
                MessageBox.Show("Cannot find API key text box.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            string apiKey = apiKeyTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show("Please enter a valid API key.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            bool success = ApiKeyManager.SaveApiKey(apiKey);
            if (success)
            {
                apiKeyTextBox.Text = "••••••••••••••••••••" + apiKey.Substring(Math.Max(0, apiKey.Length - 4));
                MessageBox.Show("API key saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Failed to save API key. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnEUW_Checked(object sender, RoutedEventArgs e)
        {
            _selectedRegion = "euw1";
            btnEUW.IsChecked = true;
            if (btnEUNE != null)
                btnEUNE.IsChecked = false;
        }

        private void btnEUNE_Checked(object sender, RoutedEventArgs e)
        {
            _selectedRegion = "eun1";
            btnEUNE.IsChecked = true;
            if (btnEUW != null)
                btnEUW.IsChecked = false;
        }

        #endregion

        #region (Optional) Helper Methods

        // Example: Cache account card borders for quick access (if your XAML uses an ItemsControl named icAccounts).
        private void CacheAccountCards()
        {
            _accountCardMap.Clear();
            foreach (var border in VisualTreeHelperExtensions.FindVisualChildren<Border>(icAccounts))
            {
                if (border.Tag is Account account)
                {
                    _accountCardMap[account] = border;
                }
            }
        }

        #endregion
    }
}