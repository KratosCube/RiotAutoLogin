using RiotAutoLogin.Models;
using RiotAutoLogin.Services;
using RiotAutoLogin.Utilities;
using System.ComponentModel;
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
            DataContext = this;
            LoadAccounts();
            RefreshAccountLists();
            UpdateTotalGameStats();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadAccounts();
            RefreshAccountLists();
            UpdateTotalGameStats();
            Task preloadTask = GameData.PreloadAllDataAsync();
            await Task.Run(() => MonitorLeagueClientAsync());

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
            LoadAutoPickSettings();

            // Start auto-pick monitor if any auto-pick feature is enabled
            if (_autoPickSettings.AutoPickEnabled || _autoPickSettings.AutoBanEnabled || _autoPickSettings.AutoSpellsEnabled)
            {
                StartAutoPickMonitor();
            }
        }

        #region Window Control Event Handlers

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Source == this)
            {
                DragMove();
                e.Handled = true;
            }
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

        private async void tglAutoAccept_Checked(object sender, RoutedEventArgs e)
        {
            if (!LCUService.IsLeagueOpen)
            {
                if (LCUService.CheckIfLeagueClientIsOpen())
                {
                    LCUService.StartAutoAccept();
                    tglAutoAccept.Content = "ON";
                    autoAcceptStatusIndicator.Fill = new SolidColorBrush(Colors.LimeGreen);
                    txtAutoAcceptStatus.Text = "Auto-accept is active - will accept game matches automatically";
                }
                else
                {
                    MessageBox.Show("League Client is not running. Please start League of Legends first.",
                        "League Client Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    tglAutoAccept.IsChecked = false;
                }
            }
            else
            {
                LCUService.StartAutoAccept();
                tglAutoAccept.Content = "ON";
                autoAcceptStatusIndicator.Fill = new SolidColorBrush(Colors.LimeGreen);
                txtAutoAcceptStatus.Text = "Auto-accept is active - will accept game matches automatically";
            }
        }

        private void tglAutoAccept_Unchecked(object sender, RoutedEventArgs e)
        {
            LCUService.StopAutoAccept();
            tglAutoAccept.Content = "OFF";
            autoAcceptStatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(112, 112, 112));
            txtAutoAcceptStatus.Text = "Auto-accept is inactive";
        }

        protected override void OnClosed(EventArgs e)
        {
            LCUService.StopAutoAccept();
            StopAutoPickMonitor();
            base.OnClosed(e);
        }

        private async Task MonitorLeagueClientAsync()
        {
            while (true)
            {
                try
                {
                    bool isLeagueOpen = LCUService.CheckIfLeagueClientIsOpen();

                    // Update UI based on League client state
                    await Dispatcher.InvokeAsync(() => {
                        if (isLeagueOpen)
                        {
                            if (tglAutoAccept.IsEnabled == false)
                            {
                                tglAutoAccept.IsEnabled = true;
                                txtAutoAcceptStatus.Text = tglAutoAccept.IsChecked == true
                                    ? "Auto-accept is active - will accept game matches automatically"
                                    : "Auto-accept is inactive. Enable to automatically accept game matches.";
                            }
                        }
                        else
                        {
                            tglAutoAccept.IsEnabled = false;
                            tglAutoAccept.IsChecked = false;
                            autoAcceptStatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(112, 112, 112));
                            txtAutoAcceptStatus.Text = "League Client is not running. Start League of Legends to use auto-accept.";
                        }
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error monitoring League client: " + ex.Message);
                }

                await Task.Delay(5000);
            }
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

        private AutoPickSettings _autoPickSettings = new AutoPickSettings();
        public AutoPickSettings AutoPickSettings => _autoPickSettings;

        private BitmapImage _primaryChampionImage;
        public BitmapImage PrimaryChampionImage
        {
            get { return _primaryChampionImage; }
            set
            {
                _primaryChampionImage = value;
                OnPropertyChanged(nameof(PrimaryChampionImage));
            }
        }

        private string _primaryChampionName = "Select Champion";
        public string PrimaryChampionName
        {
            get { return _primaryChampionName; }
            set
            {
                _primaryChampionName = value;
                OnPropertyChanged(nameof(PrimaryChampionName));
            }
        }

        // Add similar properties for SecondaryChampionImage, SecondaryChampionName, 
        // BanChampionImage, BanChampionName, Spell1Image, Spell1Name, Spell2Image, Spell2Name

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // Event handlers for UI controls
        private void primaryChampCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            SelectChampion(true, "Select Primary Champion", (champion) =>
            {
                try
                {
                    // Save to settings
                    _autoPickSettings.PickChampionId = champion.Id;
                    _autoPickSettings.PickChampionName = champion.Name;

                    // Update UI properties
                    PrimaryChampionName = champion.Name;
                    PrimaryChampionImage = champion.Image;

                    // Update the card directly
                    UpdateChampionCard(primaryChampCard, champion.Name, champion.Image);

                    SaveAutoPickSettings();

                    // Log for debugging
                    Debug.WriteLine($"Updated primary champion: {champion.Name}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error setting champion: {ex.Message}");
                    MessageBox.Show($"Error setting champion: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        private void secondaryChampCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            SelectChampion(true, "Select Secondary Champion", (champion) =>
            {
                _autoPickSettings.SecondaryChampionId = champion.Id;
                _autoPickSettings.SecondaryChampionName = champion.Name;
                SecondaryChampionName = champion.Name;
                SecondaryChampionImage = champion.Image;
                UpdateChampionCard(secondaryChampCard, champion.Name, champion.Image);
                SaveAutoPickSettings();
            });
        }
        private void UpdateChampionCard(Border card, string championName, BitmapImage championImage)
        {
            // Find the Image and TextBlock in the card
            Image imageControl = null;
            TextBlock textBlock = null;

            // Check if the card has children
            if (VisualTreeHelper.GetChildrenCount(card) > 0)
            {
                // Get the first child (should be a Grid)
                var child = VisualTreeHelper.GetChild(card, 0);

                if (child is Grid grid)
                {
                    // Now loop through the grid children
                    foreach (var gridChild in grid.Children)
                    {
                        if (gridChild is Image img)
                            imageControl = img;
                        else if (gridChild is TextBlock txt)
                            textBlock = txt;
                    }
                }
            }

            // Update directly
            if (imageControl != null)
                imageControl.Source = championImage;

            if (textBlock != null)
                textBlock.Text = championName;
        }
        private void banChampCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            SelectChampion(true, "Select Champion to Ban", (champion) =>
            {
                _autoPickSettings.BanChampionId = champion.Id;
                _autoPickSettings.BanChampionName = champion.Name;
                BanChampionName = champion.Name;
                BanChampionImage = champion.Image;
                UpdateChampionCard(banChampCard, champion.Name, champion.Image);
                SaveAutoPickSettings();
            });
        }

        private void spell1Card_MouseDown(object sender, MouseButtonEventArgs e)
        {
            SelectChampion(false, "Select Summoner Spell 1", (spell) =>
            {
                _autoPickSettings.SummonerSpell1Id = spell.Id;
                _autoPickSettings.SummonerSpell1Name = spell.Name;
                Spell1Name = spell.Name;
                Spell1Image = spell.Image;
                UpdateChampionCard(spell1Card, spell.Name, spell.Image);
                SaveAutoPickSettings();
            });
        }

        private void spell2Card_MouseDown(object sender, MouseButtonEventArgs e)
        {
            SelectChampion(false, "Select Summoner Spell 2", (spell) =>
            {
                _autoPickSettings.SummonerSpell2Id = spell.Id;
                _autoPickSettings.SummonerSpell2Name = spell.Name;
                Spell2Name = spell.Name;
                Spell2Image = spell.Image;
                UpdateChampionCard(spell2Card, spell.Name, spell.Image);
                SaveAutoPickSettings();
            });
        }

        private void SelectChampion(bool isChampionSelect, string title, Action<dynamic> onSelect)
        {
            var window = new ChampionSelectWindow(isChampionSelect, title)
            {
                Owner = this
            };

            if (window.ShowDialog() == true && window.SelectedItem != null)
            {
                onSelect(window.SelectedItem);
            }
        }

        // Settings handlers
        private void chkAutoPickEnabled_Checked(object sender, RoutedEventArgs e)
        {
            _autoPickSettings.AutoPickEnabled = true;
            SaveAutoPickSettings();

            // Start the champion select monitor
            StartAutoPickMonitor();
        }

        private void chkAutoPickEnabled_Unchecked(object sender, RoutedEventArgs e)
        {
            _autoPickSettings.AutoPickEnabled = false;
            SaveAutoPickSettings();

            // Stop the champion select monitor if other features are also disabled
            if (!_autoPickSettings.AutoBanEnabled && !_autoPickSettings.AutoSpellsEnabled)
            {
                StopAutoPickMonitor();
            }
        }

        // Add similar handlers for other checkboxes and settings

        // Delay adjustment handlers
        private void btnIncreaseHoverDelay_Click(object sender, RoutedEventArgs e)
        {
            int delay = int.Parse(txtPickHoverDelay.Text);
            delay += 100;
            txtPickHoverDelay.Text = delay.ToString();
            _autoPickSettings.PickHoverDelayMs = delay;
            SaveAutoPickSettings();
        }

        private void btnDecreaseHoverDelay_Click(object sender, RoutedEventArgs e)
        {
            int delay = int.Parse(txtPickHoverDelay.Text);
            delay = Math.Max(0, delay - 100);
            txtPickHoverDelay.Text = delay.ToString();
            _autoPickSettings.PickHoverDelayMs = delay;
            SaveAutoPickSettings();
        }

        // Add similar handlers for other delay buttons

        // Auto-pick monitoring logic
        private CancellationTokenSource _autoPickCts;

        private void StartAutoPickMonitor()
        {
            if (_autoPickCts != null)
                return;

            _autoPickCts = new CancellationTokenSource();
            Task.Run(() => MonitorChampionSelectAsync(_autoPickCts.Token));
        }

        private void StopAutoPickMonitor()
        {
            _autoPickCts?.Cancel();
            _autoPickCts?.Dispose();
            _autoPickCts = null;
        }

        private async Task MonitorChampionSelectAsync(CancellationToken cancellationToken)
        {
            bool pickSelectedInCurrentSession = false;
            bool banSelectedInCurrentSession = false;
            bool spellsSelectedInCurrentSession = false;
            string currentChatRoom = "";

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!LCUService.IsLeagueOpen || !LCUService.CheckIfLeagueClientIsOpen())
                    {
                        await Task.Delay(2000, cancellationToken);
                        continue;
                    }

                    string gamePhase = await LCUService.GetCurrentGamePhaseAsync();

                    if (gamePhase == "ChampSelect")
                    {
                        // Handle champion select actions
                        var result = await HandleChampionSelectAsync(
                            pickSelectedInCurrentSession,
                            banSelectedInCurrentSession,
                            spellsSelectedInCurrentSession,
                            currentChatRoom);

                        // Update local variables with the returned values
                        pickSelectedInCurrentSession = result.pickSelected;
                        banSelectedInCurrentSession = result.banSelected;
                        spellsSelectedInCurrentSession = result.spellsSelected;
                        currentChatRoom = result.chatRoom;
                    }
                    else
                    {
                        // Reset flags when we're not in champion select
                        pickSelectedInCurrentSession = false;
                        banSelectedInCurrentSession = false;
                        spellsSelectedInCurrentSession = false;
                        currentChatRoom = "";

                        await Task.Delay(2000, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Cancellation requested
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in champion select monitor: {ex.Message}");
                    await Task.Delay(2000, cancellationToken);
                }
            }
        }

        private async Task<(bool pickSelected, bool banSelected, bool spellsSelected, string chatRoom)> HandleChampionSelectAsync(
    bool pickSelectedInCurrentSession,
    bool banSelectedInCurrentSession,
    bool spellsSelectedInCurrentSession,
    string currentChatRoom)
        {
            try
            {
                // Get champion select session info
                string[] champSelectSession = LCUService.ClientRequest("GET", "lol-champ-select/v1/session");
                if (champSelectSession[0] != "200")
                    return (pickSelectedInCurrentSession, banSelectedInCurrentSession, spellsSelectedInCurrentSession, currentChatRoom);

                // Check if this is a new champion select session
                string chatRoomId = "";
                using (JsonDocument doc = JsonDocument.Parse(champSelectSession[1]))
                {
                    if (doc.RootElement.TryGetProperty("chatDetails", out var chatDetails) &&
                        chatDetails.TryGetProperty("multiUserChatId", out var multiUserChatId))
                    {
                        chatRoomId = multiUserChatId.GetString();
                    }
                }

                if (currentChatRoom != chatRoomId)
                {
                    // Reset flags for new champion select
                    pickSelectedInCurrentSession = false;
                    banSelectedInCurrentSession = false;
                    spellsSelectedInCurrentSession = false;
                    currentChatRoom = chatRoomId;
                }

                // Handle summoner spells if enabled and not already selected
                if (_autoPickSettings.AutoSpellsEnabled && !spellsSelectedInCurrentSession)
                {
                    if (_autoPickSettings.SummonerSpell1Id > 0)
                    {
                        bool success = LCUService.SelectSummonerSpell(_autoPickSettings.SummonerSpell1Id, 1);
                        if (success)
                            Debug.WriteLine("Selected summoner spell 1");
                    }

                    if (_autoPickSettings.SummonerSpell2Id > 0)
                    {
                        bool success = LCUService.SelectSummonerSpell(_autoPickSettings.SummonerSpell2Id, 2);
                        if (success)
                            Debug.WriteLine("Selected summoner spell 2");
                    }

                    spellsSelectedInCurrentSession = true;
                }

                // Check if it's our turn to pick or ban
                var (isMyTurn, actId, actionType) = await LCUService.GetChampSelectTurnAsync();

                if (isMyTurn)
                {
                    if (actionType == "pick" && _autoPickSettings.AutoPickEnabled && !pickSelectedInCurrentSession)
                    {
                        await HandlePickAsync(actId);
                        pickSelectedInCurrentSession = true;
                    }
                    else if (actionType == "ban" && _autoPickSettings.AutoBanEnabled && !banSelectedInCurrentSession)
                    {
                        await HandleBanAsync(actId);
                        banSelectedInCurrentSession = true;
                    }
                }

                return (pickSelectedInCurrentSession, banSelectedInCurrentSession, spellsSelectedInCurrentSession, currentChatRoom);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling champion select: {ex.Message}");
                return (pickSelectedInCurrentSession, banSelectedInCurrentSession, spellsSelectedInCurrentSession, currentChatRoom);
            }
        }

        private async Task HandlePickAsync(string actId)
        {
            try
            {
                // Hover champion after delay
                if (_autoPickSettings.PickHoverDelayMs > 0)
                    await Task.Delay(_autoPickSettings.PickHoverDelayMs);

                // Try primary champion first
                if (_autoPickSettings.PickChampionId > 0)
                {
                    bool hoverSuccess = LCUService.SelectChampion(_autoPickSettings.PickChampionId, actId);

                    // If hover fails, try secondary champion
                    if (!hoverSuccess && _autoPickSettings.SecondaryChampionId > 0)
                    {
                        hoverSuccess = LCUService.SelectChampion(_autoPickSettings.SecondaryChampionId, actId);
                        Debug.WriteLine($"Hovering secondary champion: {hoverSuccess}");
                    }

                    // Lock in champion
                    if (_autoPickSettings.InstantLock)
                    {
                        bool lockSuccess = LCUService.SelectChampion(_autoPickSettings.PickChampionId, actId, true);
                        Debug.WriteLine($"Locking in champion: {lockSuccess}");
                    }
                    else if (_autoPickSettings.PickLockDelayMs > 0)
                    {
                        await Task.Delay(_autoPickSettings.PickLockDelayMs);
                        bool lockSuccess = LCUService.SelectChampion(_autoPickSettings.PickChampionId, actId, true);
                        Debug.WriteLine($"Locking in champion after delay: {lockSuccess}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling pick: {ex.Message}");
            }
        }

        private async Task HandleBanAsync(string actId)
        {
            try
            {
                // Hover ban after delay
                if (_autoPickSettings.BanHoverDelayMs > 0)
                    await Task.Delay(_autoPickSettings.BanHoverDelayMs);

                if (_autoPickSettings.BanChampionId > 0)
                {
                    bool hoverSuccess = LCUService.SelectChampion(_autoPickSettings.BanChampionId, actId);
                    Debug.WriteLine($"Hovering ban: {hoverSuccess}");

                    // Lock in ban
                    if (_autoPickSettings.InstantBan)
                    {
                        bool lockSuccess = LCUService.SelectChampion(_autoPickSettings.BanChampionId, actId, true);
                        Debug.WriteLine($"Locking in ban: {lockSuccess}");
                    }
                    else if (_autoPickSettings.BanLockDelayMs > 0)
                    {
                        await Task.Delay(_autoPickSettings.BanLockDelayMs);
                        bool lockSuccess = LCUService.SelectChampion(_autoPickSettings.BanChampionId, actId, true);
                        Debug.WriteLine($"Locking in ban after delay: {lockSuccess}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling ban: {ex.Message}");
            }
        }

        // Load and save auto-pick settings
        private void LoadAutoPickSettings()
        {
            try
            {
                string filePath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RiotClientAutoLogin", "autopick_settings.json");

                if (System.IO.File.Exists(filePath))
                {
                    string json = System.IO.File.ReadAllText(filePath);
                    _autoPickSettings = JsonSerializer.Deserialize<AutoPickSettings>(json) ?? new AutoPickSettings();

                    // Update UI
                    UpdateAutoPickUI();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading auto-pick settings: {ex.Message}");
            }
        }

        private void SaveAutoPickSettings()
        {
            try
            {
                string directory = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RiotClientAutoLogin");

                if (!System.IO.Directory.Exists(directory))
                    System.IO.Directory.CreateDirectory(directory);

                string filePath = System.IO.Path.Combine(directory, "autopick_settings.json");
                string json = JsonSerializer.Serialize(_autoPickSettings, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving auto-pick settings: {ex.Message}");
            }
        }
        private void UpdateAutoPickUI()
        {
            // Update checkboxes
            chkAutoPickEnabled.IsChecked = _autoPickSettings.AutoPickEnabled;
            chkAutoBanEnabled.IsChecked = _autoPickSettings.AutoBanEnabled;
            chkAutoSpellsEnabled.IsChecked = _autoPickSettings.AutoSpellsEnabled;
            chkInstantLock.IsChecked = _autoPickSettings.InstantLock;
            chkInstantBan.IsChecked = _autoPickSettings.InstantBan;

            // Update delay inputs
            txtPickHoverDelay.Text = _autoPickSettings.PickHoverDelayMs.ToString();
            txtPickLockDelay.Text = _autoPickSettings.PickLockDelayMs.ToString();
            txtBanHoverDelay.Text = _autoPickSettings.BanHoverDelayMs.ToString();
            txtBanLockDelay.Text = _autoPickSettings.BanLockDelayMs.ToString();

            // Update champion and spell names/images
            PrimaryChampionName = string.IsNullOrEmpty(_autoPickSettings.PickChampionName) ?
                "Select Champion" : _autoPickSettings.PickChampionName;

            SecondaryChampionName = string.IsNullOrEmpty(_autoPickSettings.SecondaryChampionName) ?
                "Select Champion" : _autoPickSettings.SecondaryChampionName;

            BanChampionName = string.IsNullOrEmpty(_autoPickSettings.BanChampionName) ?
                "Select Champion" : _autoPickSettings.BanChampionName;

            Spell1Name = string.IsNullOrEmpty(_autoPickSettings.SummonerSpell1Name) ?
                "Select Spell" : _autoPickSettings.SummonerSpell1Name;

            Spell2Name = string.IsNullOrEmpty(_autoPickSettings.SummonerSpell2Name) ?
                "Select Spell" : _autoPickSettings.SummonerSpell2Name;

            // Load images if needed
            LoadChampionAndSpellImages();
        }

        private async void LoadChampionAndSpellImages()
        {
            try
            {
                // Load champion images
                if (!string.IsNullOrEmpty(_autoPickSettings.PickChampionName) && PrimaryChampionImage == null)
                {
                    string imageUrl = await DataDragonService.GetChampionImageUrlAsync(_autoPickSettings.PickChampionName);
                    PrimaryChampionImage = await DataDragonService.DownloadImageAsync(imageUrl);
                }

                if (!string.IsNullOrEmpty(_autoPickSettings.SecondaryChampionName) && SecondaryChampionImage == null)
                {
                    string imageUrl = await DataDragonService.GetChampionImageUrlAsync(_autoPickSettings.SecondaryChampionName);
                    SecondaryChampionImage = await DataDragonService.DownloadImageAsync(imageUrl);
                }

                if (!string.IsNullOrEmpty(_autoPickSettings.BanChampionName) && BanChampionImage == null)
                {
                    string imageUrl = await DataDragonService.GetChampionImageUrlAsync(_autoPickSettings.BanChampionName);
                    BanChampionImage = await DataDragonService.DownloadImageAsync(imageUrl);
                }

                // Load spell images
                if (!string.IsNullOrEmpty(_autoPickSettings.SummonerSpell1Name) && Spell1Image == null)
                {
                    string imageUrl = await DataDragonService.GetSummonerSpellImageUrlAsync(_autoPickSettings.SummonerSpell1Name);
                    Spell1Image = await DataDragonService.DownloadImageAsync(imageUrl);
                }

                if (!string.IsNullOrEmpty(_autoPickSettings.SummonerSpell2Name) && Spell2Image == null)
                {
                    string imageUrl = await DataDragonService.GetSummonerSpellImageUrlAsync(_autoPickSettings.SummonerSpell2Name);
                    Spell2Image = await DataDragonService.DownloadImageAsync(imageUrl);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading images: {ex.Message}");
            }
        }




        private BitmapImage _secondaryChampionImage;
        public BitmapImage SecondaryChampionImage
        {
            get { return _secondaryChampionImage; }
            set
            {
                _secondaryChampionImage = value;
                OnPropertyChanged(nameof(SecondaryChampionImage));
            }
        }

        private string _secondaryChampionName = "Select Champion";
        public string SecondaryChampionName
        {
            get { return _secondaryChampionName; }
            set
            {
                _secondaryChampionName = value;
                OnPropertyChanged(nameof(SecondaryChampionName));
            }
        }

        private BitmapImage _banChampionImage;
        public BitmapImage BanChampionImage
        {
            get { return _banChampionImage; }
            set
            {
                _banChampionImage = value;
                OnPropertyChanged(nameof(BanChampionImage));
            }
        }

        private string _banChampionName = "Select Champion";
        public string BanChampionName
        {
            get { return _banChampionName; }
            set
            {
                _banChampionName = value;
                OnPropertyChanged(nameof(BanChampionName));
            }
        }

        private BitmapImage _spell1Image;
        public BitmapImage Spell1Image
        {
            get { return _spell1Image; }
            set
            {
                _spell1Image = value;
                OnPropertyChanged(nameof(Spell1Image));
            }
        }

        private string _spell1Name = "Select Spell";
        public string Spell1Name
        {
            get { return _spell1Name; }
            set
            {
                _spell1Name = value;
                OnPropertyChanged(nameof(Spell1Name));
            }
        }

        private BitmapImage _spell2Image;
        public BitmapImage Spell2Image
        {
            get { return _spell2Image; }
            set
            {
                _spell2Image = value;
                OnPropertyChanged(nameof(Spell2Image));
            }
        }

        private string _spell2Name = "Select Spell";
        public string Spell2Name
        {
            get { return _spell2Name; }
            set
            {
                _spell2Name = value;
                OnPropertyChanged(nameof(Spell2Name));
            }
        }

        private void btnDecreaseBanHoverDelay_Click(object sender, RoutedEventArgs e)
        {
            int delay = int.Parse(txtBanHoverDelay.Text);
            delay = Math.Max(0, delay - 100);
            txtBanHoverDelay.Text = delay.ToString();
            _autoPickSettings.BanHoverDelayMs = delay;
            SaveAutoPickSettings();
        }

        private void btnIncreaseBanHoverDelay_Click(object sender, RoutedEventArgs e)
        {
            int delay = int.Parse(txtBanHoverDelay.Text);
            delay += 100;
            txtBanHoverDelay.Text = delay.ToString();
            _autoPickSettings.BanHoverDelayMs = delay;
            SaveAutoPickSettings();
        }

        private void btnDecreaseBanLockDelay_Click(object sender, RoutedEventArgs e)
        {
            int delay = int.Parse(txtBanLockDelay.Text);
            delay = Math.Max(0, delay - 100);
            txtBanLockDelay.Text = delay.ToString();
            _autoPickSettings.BanLockDelayMs = delay;
            SaveAutoPickSettings();
        }

        private void btnIncreaseBanLockDelay_Click(object sender, RoutedEventArgs e)
        {
            int delay = int.Parse(txtBanLockDelay.Text);
            delay += 100;
            txtBanLockDelay.Text = delay.ToString();
            _autoPickSettings.BanLockDelayMs = delay;
            SaveAutoPickSettings();
        }

        private void btnDecreaseLockDelay_Click(object sender, RoutedEventArgs e)
        {
            int delay = int.Parse(txtPickLockDelay.Text);
            delay = Math.Max(0, delay - 100);
            txtPickLockDelay.Text = delay.ToString();
            _autoPickSettings.PickLockDelayMs = delay;
            SaveAutoPickSettings();
        }

        private void btnIncreaseLockDelay_Click(object sender, RoutedEventArgs e)
        {
            int delay = int.Parse(txtPickLockDelay.Text);
            delay += 100;
            txtPickLockDelay.Text = delay.ToString();
            _autoPickSettings.PickLockDelayMs = delay;
            SaveAutoPickSettings();
        }

        private void chkAutoBanEnabled_Checked(object sender, RoutedEventArgs e)
        {
            _autoPickSettings.AutoBanEnabled = true;
            SaveAutoPickSettings();

            // Start the champion select monitor
            StartAutoPickMonitor();
        }

        private void chkAutoBanEnabled_Unchecked(object sender, RoutedEventArgs e)
        {
            _autoPickSettings.AutoBanEnabled = false;
            SaveAutoPickSettings();

            // Stop the champion select monitor if other features are also disabled
            if (!_autoPickSettings.AutoPickEnabled && !_autoPickSettings.AutoSpellsEnabled)
            {
                StopAutoPickMonitor();
            }
        }

        private void chkAutoSpellsEnabled_Checked(object sender, RoutedEventArgs e)
        {
            _autoPickSettings.AutoSpellsEnabled = true;
            SaveAutoPickSettings();

            // Start the champion select monitor
            StartAutoPickMonitor();
        }

        private void chkAutoSpellsEnabled_Unchecked(object sender, RoutedEventArgs e)
        {
            _autoPickSettings.AutoSpellsEnabled = false;
            SaveAutoPickSettings();

            // Stop the champion select monitor if other features are also disabled
            if (!_autoPickSettings.AutoPickEnabled && !_autoPickSettings.AutoBanEnabled)
            {
                StopAutoPickMonitor();
            }
        }

        private void chkInstantLock_Checked(object sender, RoutedEventArgs e)
        {
            _autoPickSettings.InstantLock = true;
            SaveAutoPickSettings();
        }

        private void chkInstantLock_Unchecked(object sender, RoutedEventArgs e)
        {
            _autoPickSettings.InstantLock = false;
            SaveAutoPickSettings();
        }

        private void chkInstantBan_Checked(object sender, RoutedEventArgs e)
        {
            _autoPickSettings.InstantBan = true;
            SaveAutoPickSettings();
        }

        private void chkInstantBan_Unchecked(object sender, RoutedEventArgs e)
        {
            _autoPickSettings.InstantBan = false;
            SaveAutoPickSettings();
        }
    }


}