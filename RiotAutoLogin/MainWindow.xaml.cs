using RiotAutoLogin.Models;
using RiotAutoLogin.Services;
using RiotAutoLogin.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Forms;
using Path = System.IO.Path;

namespace RiotAutoLogin
{
    public partial class MainWindow : Window
    {
        // DllImports to bring a window to the front.
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
        
        // DllImports for cursor position and screen detection
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        private const int SW_MAXIMIZE = 3;

        // Data and configuration fields.
        private List<Account> _accounts = new List<Account>();
        private readonly string _configFilePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RiotClientAutoLogin", "accounts.json");

        // System Tray Icon
        private NotifyIcon? _notifyIcon;

        // Global Hotkey Service
        private GlobalHotkeyService? _hotkeyService;
        private HotkeySettings _hotkeySettings = new HotkeySettings();
        private readonly string _hotkeySettingsFilePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RiotClientAutoLogin", "hotkey_settings.json");

        // Update Service
        private UpdateService? _updateService;

        private bool _isDarkMode = true;
        private string _selectedAvatarPath = string.Empty;
        private string _selectedRegion = "eun1";
        private Border? _lastSelectedCard;

        // For mapping an account to its card border (used in selection highlighting).
        private Dictionary<Account, Border> _accountCardMap = new Dictionary<Account, Border>();
        private System.Windows.Controls.Primitives.Popup? _quickLoginPopup;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            LoadAccounts();
            LoadHotkeySettings();
            InitializeServices();
            RefreshUI();
            
            this.Closing += MainWindow_Closing;
            this.KeyDown += MainWindow_KeyDown;
        }

        private void InitializeServices()
        {
            InitializeNotifyIcon(); 
            InitializeHotkeyService();
            InitializeUpdateService();
        }
            
        private void RefreshUI()
        {
            RefreshAccountLists();
            UpdateTotalGameStats();
            UpdateHotkeyDisplay();
            UpdateRunOnStartupToggleUI();
        }

        private void InitializeNotifyIcon()
        {
            _notifyIcon = new NotifyIcon();

            try
            {
                // Try to load the custom icon from embedded resources first
                var resourceStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/resources/logoAcc.ico"));
                if (resourceStream != null)
                {
                    _notifyIcon.Icon = new System.Drawing.Icon(resourceStream.Stream);
                    Console.WriteLine("Custom system tray icon (logoAcc.ico) loaded from resources successfully.");
                }
                else
                {
                    // Try to load from file path as fallback
                    string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "logoAcc.ico");
                    if (File.Exists(iconPath))
                    {
                        _notifyIcon.Icon = new System.Drawing.Icon(iconPath);
                        Console.WriteLine("Custom system tray icon (logoAcc.ico) loaded from file path successfully.");
                    }
                    else
                    {
                        Console.WriteLine($"Custom icon not found at: {iconPath}");
                        // Fallback to system icon
                        _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
                        Console.WriteLine("Fallback system tray icon (SystemIcons.Application) loaded.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading custom system tray icon: {ex.Message}");
                try
                {
                    // Attempt to load a generic system icon as a fallback
                    _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
                    Console.WriteLine("Fallback system tray icon (SystemIcons.Application) loaded.");
                }
                catch (Exception exSysIcon)
                {
                    Console.WriteLine($"Error loading fallback system tray icon: {exSysIcon.Message}");
                    // If even this fails, the tray icon likely won't show, but the app might still run.
                }
            }
            
            _notifyIcon.Visible = false; // Initially hidden, will be shown on minimize or X-close
            _notifyIcon.Text = "Riot Auto Login";

            // Handle double-click to show the main window
            _notifyIcon.DoubleClick += (s, args) => ShowMainWindow();

            // Context menu for the tray icon
            _notifyIcon.ContextMenuStrip = new ContextMenuStrip();
            _notifyIcon.ContextMenuStrip.Items.Add("Show", null, (s, args) => ShowMainWindow());
            _notifyIcon.ContextMenuStrip.Items.Add("Exit", null, (s, args) => ExitApplication());
        }

        private void InitializeHotkeyService()
        {
            _hotkeyService?.Dispose(); // Dispose existing if any

            _hotkeyService = new GlobalHotkeyService(this);
            // Use _hotkeySettings.Modifier and _hotkeySettings.VirtualKey
            bool registered = _hotkeyService.Register(_hotkeySettings.Modifier, _hotkeySettings.VirtualKey); 
            if (registered)
            {
                _hotkeyService.HotkeyPressed += OnHotkeyPressed;
                Console.WriteLine($"Hotkey {_hotkeySettings.DisplayName} registered.");
            }
            else
            {
                Console.WriteLine($"Failed to register hotkey {_hotkeySettings.DisplayName}. It might be in use by another application.");
                System.Windows.MessageBox.Show($"Failed to register hotkey: {_hotkeySettings.DisplayName}. It might already be in use.", "Hotkey Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            UpdateHotkeyDisplay(); // Update the label in settings
        }

        private void InitializeUpdateService()
        {
            // Configured for GitHub repository: https://github.com/KratosCube/RiotAutoLogin
            _updateService = new UpdateService("KratosCube", "RiotAutoLogin");
            
            // Subscribe to update events
            _updateService.UpdateAvailable += OnUpdateAvailable;
            _updateService.UpdateProgressChanged += OnUpdateProgressChanged;
            
            Console.WriteLine("Update service initialized");
        }

        private void OnUpdateAvailable(UpdateInfo updateInfo)
        {
            // Show update notification on UI thread
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var updateWindow = new UpdateNotificationWindow(updateInfo, _updateService);
                    updateWindow.Owner = this;
                    updateWindow.Show();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error showing update notification: {ex.Message}");
                }
            });
        }

        private void OnUpdateProgressChanged(UpdateProgress progress)
        {
            // Log update progress
            Console.WriteLine($"Update progress: {progress.Status} - {progress.Message}");
        }

        private async void btnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Disable the button during check
                btnCheckUpdates.IsEnabled = false;
                btnCheckUpdates.Content = "Checking...";
                
                Console.WriteLine("🔄 Manual update check requested...");
                await _updateService.CheckForUpdatesAsync();
                
                // Reset button
                btnCheckUpdates.IsEnabled = true;
                btnCheckUpdates.Content = "Check for Updates";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during manual update check: {ex.Message}");
                System.Windows.MessageBox.Show($"Failed to check for updates: {ex.Message}", 
                    "Update Check Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                
                // Reset button
                btnCheckUpdates.IsEnabled = true;
                btnCheckUpdates.Content = "Check for Updates";
            }
        }

        private void OnHotkeyPressed()
        {
            Console.WriteLine("Hotkey pressed!");
            
            // Toggle popup behavior: if already open, close it; if closed, open it
            if (_quickLoginPopup != null && _quickLoginPopup.IsOpen)
            {
                Console.WriteLine("QuickLoginPopup already visible - closing it");
                _quickLoginPopup.IsOpen = false;
                _quickLoginPopup = null;
                return;
            }
            
            // Show the popup regardless of main window state (this is the whole point of global hotkey!)
            ShowQuickLoginPopup();
        }

        private void ShowQuickLoginPopup()
        {
            Console.WriteLine("🔧 ShowQuickLoginPopup called");
            
            // Ensure accounts are loaded.
            if (_accounts == null || !_accounts.Any())
            {
                Console.WriteLine("No accounts loaded for QuickLoginPopup.");
                return;
            }

            // Close any existing popup
            if (_quickLoginPopup != null)
            {
                _quickLoginPopup.IsOpen = false;
                _quickLoginPopup = null;
            }

            // Get the center of the screen where the mouse is located
            Point mouseScreenCenter = GetMouseScreenCenter();

            // Create a simple popup with StaysOpen = true for manual control
            _quickLoginPopup = new System.Windows.Controls.Primitives.Popup
            {
                IsOpen = false, // Start closed, we'll open it after setup
                StaysOpen = true, // We'll handle closing manually
                Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute,
                AllowsTransparency = true,
                PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade,
                Focusable = true // Allow the popup to receive focus for keyboard events
            };

            // Create main border for the popup with dynamic sizing
            var screenHeight = SystemParameters.WorkArea.Height;
            var maxPopupHeight = Math.Min(screenHeight * 0.8, _accounts.Count * 100 + 150); // Dynamic height based on accounts
            var popupWidth = Math.Max(450, Math.Min(600, _accounts.Count > 3 ? 600 : 450)); // Wider for better content display
            
            var mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(35, 35, 47)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 92)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(20),
                MaxWidth = popupWidth,
                MaxHeight = maxPopupHeight,
                MinWidth = 400,
                MinHeight = 200,
                Focusable = true, // Allow border to receive focus
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    ShadowDepth = 5,
                    BlurRadius = 15,
                    Opacity = 0.3
                }
            };

            // Add keyboard event handler for Escape key
            mainBorder.KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Console.WriteLine("Escape key pressed on mainBorder - closing popup");
                    if (_quickLoginPopup != null && _quickLoginPopup.IsOpen)
                    {
                        _quickLoginPopup.IsOpen = false;
                    }
                    e.Handled = true;
                }
            };

            // Also add PreviewKeyDown for earlier capture
            mainBorder.PreviewKeyDown += (sender, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Console.WriteLine("Escape key preview on mainBorder - closing popup");
                    if (_quickLoginPopup != null && _quickLoginPopup.IsOpen)
                    {
                        _quickLoginPopup.IsOpen = false;
                    }
                    e.Handled = true;
                }
            };

            var stackPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical
            };

            // Add title with instruction text
            var titleText = new TextBlock
            {
                Text = "Quick Login - Press ESC to close",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 15)
            };
            stackPanel.Children.Add(titleText);

            // Add account buttons with ScrollViewer for better scaling
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = maxPopupHeight - 100, // Leave space for title and padding
                Margin = new Thickness(0)
            };
            
            var accountPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical
            };

            foreach (var account in _accounts)
            {
                var button = CreateQuickLoginButton(account);
                accountPanel.Children.Add(button);
            }

            scrollViewer.Content = accountPanel;
            stackPanel.Children.Add(scrollViewer);

            mainBorder.Child = stackPanel;
            _quickLoginPopup.Child = mainBorder;
            
            // Add keyboard event handler to the popup itself as well
            _quickLoginPopup.KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Console.WriteLine("Escape key pressed on popup - closing");
                    if (_quickLoginPopup != null && _quickLoginPopup.IsOpen)
                    {
                        _quickLoginPopup.IsOpen = false;
                    }
                    e.Handled = true;
                }
            };

            // Add PreviewKeyDown to popup for earlier capture
            _quickLoginPopup.PreviewKeyDown += (sender, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Console.WriteLine("Escape key preview on popup - closing");
                    if (_quickLoginPopup != null && _quickLoginPopup.IsOpen)
                    {
                        _quickLoginPopup.IsOpen = false;
                    }
                    e.Handled = true;
                }
            };
            
            // Measure the popup size before positioning
            mainBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var popupSize = mainBorder.DesiredSize;
            
            // Get screen bounds for proper positioning
            var screenBounds = SystemParameters.WorkArea;
            
            // Calculate position to center the popup on the mouse screen with boundary checking
            double popupX = mouseScreenCenter.X - (popupSize.Width / 2);
            double popupY = mouseScreenCenter.Y - (popupSize.Height / 2);
            
            // Ensure popup stays within screen bounds
            popupX = Math.Max(10, Math.Min(popupX, screenBounds.Width - popupSize.Width - 10));
            popupY = Math.Max(10, Math.Min(popupY, screenBounds.Height - popupSize.Height - 10));
            
            // Set the position
            _quickLoginPopup.HorizontalOffset = popupX;
            _quickLoginPopup.VerticalOffset = popupY;
            
            Console.WriteLine($"Positioning popup at: {popupX}, {popupY} (size: {popupSize.Width}x{popupSize.Height}, accounts: {_accounts.Count})");
            
            // Add event handler for when popup closes
            _quickLoginPopup.Closed += (sender, e) =>
            {
                Console.WriteLine("Quick login popup closed");
                _quickLoginPopup = null;
                StopClickOutsideMonitoring();
            };
            
            // Now open the popup
            _quickLoginPopup.IsOpen = true;
            
            // Force focus to the popup content with multiple approaches
            Task.Run(async () =>
            {
                await Task.Delay(100); // Small delay to ensure popup is rendered
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        // Try multiple focus approaches
                        if (_quickLoginPopup.Child is FrameworkElement popupChild)
                        {
                            popupChild.Focus();
                            Keyboard.Focus(popupChild);
                        }
                        
                        // Also try focusing the main border
                        mainBorder.Focus();
                        Keyboard.Focus(mainBorder);
                        
                        // Make sure the popup is focusable
                        if (!mainBorder.Focusable)
                        {
                            mainBorder.Focusable = true;
                            mainBorder.Focus();
                        }
                        
                        Console.WriteLine($"Popup opened and focus set. Focusable: {mainBorder.Focusable}, IsFocused: {mainBorder.IsFocused}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error setting focus: {ex.Message}");
                    }
                });
            });
            
            // Start monitoring for clicks outside the popup with improved detection
            StartClickOutsideMonitoring(popupX, popupY, popupSize.Width, popupSize.Height);
            
            Console.WriteLine("Quick login popup opened successfully - Press ESC to close");
        }

        private System.Windows.Controls.Button CreateQuickLoginButton(Account account)
        {
            var button = new System.Windows.Controls.Button
            {
                Height = 80,
                Margin = new Thickness(0, 5, 0, 5), // Reduced margin for better spacing
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White,
                FontSize = 14,
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
                Padding = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            // Remove default button style to prevent weird hover effects
            button.Style = null;
            button.Template = new ControlTemplate(typeof(System.Windows.Controls.Button))
            {
                VisualTree = new FrameworkElementFactory(typeof(ContentPresenter))
            };

            // Create rounded border for the button content with custom hover effects
            var buttonBorder = new Border
            {
                Background = (SolidColorBrush)Resources["CardBackgroundBrush"],
                BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 92)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(15, 10, 15, 10)
            };

            // Store original colors for hover effect
            var originalBackground = buttonBorder.Background.Clone();
            var originalBorderBrush = buttonBorder.BorderBrush.Clone();
            var hoverBackground = new SolidColorBrush(Color.FromRgb(50, 50, 62));
            var hoverBorderBrush = new SolidColorBrush(Color.FromRgb(120, 120, 127));

            // Apply hover effects to the button itself for better detection
            button.MouseEnter += (s, e) =>
            {
                buttonBorder.Background = hoverBackground;
                buttonBorder.BorderBrush = hoverBorderBrush;
            };

            button.MouseLeave += (s, e) =>
            {
                buttonBorder.Background = originalBackground;
                buttonBorder.BorderBrush = originalBorderBrush;
            };

            var stackPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal
            };

            // Add avatar
            var avatarBorder = new Border
            {
                Width = 50,
                Height = 50,
                CornerRadius = new CornerRadius(25),
                Margin = new Thickness(0, 0, 15, 0),
                ClipToBounds = true,
                Background = new SolidColorBrush(Color.FromRgb(85, 85, 92))
            };

            if (!string.IsNullOrEmpty(account.AvatarPath) && File.Exists(account.AvatarPath))
            {
                try
                {
                    var avatarImage = new System.Windows.Controls.Image
                    {
                        Source = new BitmapImage(new Uri(account.AvatarPath, UriKind.Absolute)),
                        Stretch = Stretch.UniformToFill
                    };
                    avatarBorder.Child = avatarImage;
                }
                catch
                {
                    // Default avatar with first letter
                    var avatarText = new TextBlock
                    {
                        Text = account.GameName.FirstOrDefault().ToString().ToUpper(),
                        FontSize = 20,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    };
                    avatarBorder.Child = avatarText;
                }
            }
            else
            {
                // Default avatar with first letter
                var avatarText = new TextBlock
                {
                    Text = account.GameName.FirstOrDefault().ToString().ToUpper(),
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                avatarBorder.Child = avatarText;
            }

            stackPanel.Children.Add(avatarBorder);

            // Add text info
            var textPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            // Account name
            var nameText = new TextBlock
            {
                Text = $"{account.GameName}#{account.TagLine}",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            textPanel.Children.Add(nameText);

            // Add rank info if available
            if (!string.IsNullOrEmpty(account.RankInfo) && account.RankInfo != "Unranked")
            {
                var rankText = new TextBlock
                {
                    Text = $"{account.RankInfo} - {account.LeaguePoints} LP",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 120)),
                    Margin = new Thickness(0, 2, 0, 0)
                };
                textPanel.Children.Add(rankText);
            }

            stackPanel.Children.Add(textPanel);

            buttonBorder.Child = stackPanel;
            button.Content = buttonBorder;

            // Add click handler
            button.Click += (sender, e) =>
            {
                Console.WriteLine($"🎯 Quick login clicked: {account.GameName}");
                _quickLoginPopup.IsOpen = false;
                
                // Start login in background
                Task.Run(() => StartLoginProcess(account));
            };

            return button;
        }

        private void ShowMainWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
            SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle); // Bring to front
            _notifyIcon.Visible = false;
        }

        private void ExitApplication()
        {
            _hotkeyService?.Dispose(); // Dispose hotkey service
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false; // Hide it explicitly before disposing
                _notifyIcon.Dispose(); 
                _notifyIcon = null; // Set to null after disposal
            }
            System.Windows.Application.Current.Shutdown();
        }
        
        protected override void OnStateChanged(EventArgs e)
        {
            // Allow normal minimize to taskbar - don't hide to tray
            // Only the close button (X) should hide to tray, which is handled in MainWindow_Closing
            base.OnStateChanged(e);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("🚀 Application starting - Loading accounts and initializing...");
            
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
                    // Load API key with UI update
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    var apiKeyTextBox = this.FindName("txtApiKey") as System.Windows.Controls.TextBox;
                    if (apiKeyTextBox != null)
                    {
                        string apiKey = await Task.Run(() => ApiKeyManager.GetApiKey());
                        if (!string.IsNullOrEmpty(apiKey))
                        {
                            apiKeyTextBox.Text = "••••••••••••••••••••" + apiKey.Substring(Math.Max(0, apiKey.Length - 4));
                            apiKeyTextBox.ToolTip = "API key is saved. Enter a new key to update.";
                                Console.WriteLine("✅ API key loaded successfully");
                            }
                            else
                            {
                                Console.WriteLine("⚠️ No API key found - rank updates will not work");
                            }
                        }
                    });
                    
                    // Update account ranks with feedback
                    if (_accounts.Any())
                    {
                        Console.WriteLine($"🔄 Starting automatic rank update for {_accounts.Count} accounts...");
                    await UpdateAllAccountsAsync();
                        
                        // Update UI after rank update
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            RefreshAccountLists();
                            UpdateTotalGameStats();
                            SaveAccounts();
                        });
                        
                        Console.WriteLine("✅ Account ranks updated successfully on startup");
                    }
                    else
                    {
                        Console.WriteLine("ℹ️ No accounts found - skipping rank update");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error during startup rank update: {ex.Message}");
                    // Still refresh UI even if rank update failed
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        RefreshAccountLists();
                        UpdateTotalGameStats();
                    });
                }
            });
            
            LoadAutoPickSettings();

            // Update current version display
            try
            {
                var currentVersion = _updateService.GetSettings(); // Get current version from update service
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version ?? new Version("1.0.0");
                txtCurrentVersion.Text = $"Current version: v{version.ToString(3)}"; // Only show major.minor.patch
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating version display: {ex.Message}");
                txtCurrentVersion.Text = "Current version: v1.0.0";
            }

            // Start auto-pick monitor if any auto-pick feature is enabled
            if (_autoPickSettings.AutoPickEnabled || _autoPickSettings.AutoBanEnabled || _autoPickSettings.AutoSpellsEnabled)
            {
                StartAutoPickMonitor();
            }
            
            // Check for updates on startup (non-blocking)
            _ = Task.Run(async () =>
            {
                try
                {
                    if (_updateService.ShouldCheckForUpdates())
                    {
                        Console.WriteLine("🔄 Checking for application updates...");
                        await _updateService.CheckForUpdatesAsync();
                    }
                    else
                    {
                        Console.WriteLine("⏭️ Skipping update check (checked recently)");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error checking for updates: {ex.Message}");
                }
            });
            
            Console.WriteLine("🎉 Application fully loaded and ready to use!");
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
                UpdateAvatarPreview(_selectedAvatarPath);
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

        #endregion

        #region Manage Accounts Event Handlers

        private void btnAddAccount_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtAccountLogin.Text) ||
                string.IsNullOrWhiteSpace(txtGameName.Text) ||
                string.IsNullOrWhiteSpace(txtTagLine.Text) ||
                string.IsNullOrWhiteSpace(txtAccountPassword.Password))
            {
                System.Windows.MessageBox.Show("Please enter account login, game name, tagline, and password.",
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

            System.Windows.MessageBox.Show("Account added successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
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
                System.Windows.MessageBox.Show("Select an account from the list to update.");
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
                System.Windows.MessageBox.Show("Select an account from the list to delete.");
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
                Console.WriteLine("Error loading avatar: " + ex.Message);
                imgAvatarPreview.Source = null;
            }
        }

        private void RefreshAccountLists()
        {
            UIService.RefreshAccountLists(this, _accounts);
        }

        private void LoadAccounts()
        {
            _accounts = AccountService.LoadAccounts();
        }

        private void SaveAccounts()
        {
            AccountService.SaveAccounts(_accounts);
        }

        #endregion

        #region Login Tab Event Handlers

        private async void AccountCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // This method only handles MANAGE ACCOUNTS cards now
            if (sender is Border clickedBorder && clickedBorder.Tag is Account account)
            {
                Console.WriteLine($"Manage account card clicked: {account.GameName}");
                lbAccounts.SelectedItem = account;
            }
        }

        // NEW: Completely rewritten - no async, no await, pure simplicity
        private void LoginCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border clickedBorder && clickedBorder.Tag is Account account)
            {
                Console.WriteLine($"🎯 LOGIN card clicked: {account.GameName}");
                
                // Prevent multiple clicks
                if (_isLoginInProgress)
                {
                    Console.WriteLine("Login already in progress, ignoring click...");
                    return;
                }
                
                // Start login in background thread to avoid UI issues
                Task.Run(() => StartLoginProcess(account));
            }
        }

        private void StartLoginProcess(Account account)
        {
            try
            {
                _isLoginInProgress = true;
                Console.WriteLine($"Starting background login for: {account.GameName}");
                
                // Focus window first
                FocusRiotClientWindow();
                
                // Small delay
                Thread.Sleep(500);
                
                // Decrypt password
                string password = EncryptionService.DecryptString(account.EncryptedPassword);
                if (string.IsNullOrEmpty(password))
                {
                    Console.WriteLine("Failed to decrypt password!");
                    return;
                }
                
                // Call the automation service (this is already async internally)
                var loginTask = RiotClientAutomationService.LaunchAndLoginAsync(account.AccountName, password);
                loginTask.Wait(); // Wait for completion
                
                Console.WriteLine("Login process completed!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in login process: {ex.Message}");
            }
            finally
            {
                _isLoginInProgress = false;
            }
        }

        private async void lbLoginAccounts_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // This method is intentionally empty - login is handled by LoginCard_MouseDown
        }

        private void lbLoginAccounts_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lbLoginAccounts.SelectedItem is Account account)
            {
                Console.WriteLine($"Double-click login for: {account.GameName}");
                Task.Run(() => StartLoginProcess(account));
            }
        }

        private async void btnCheckRank_Click(object sender, RoutedEventArgs e)
        {
            await UpdateAllAccountsAsync();
            RefreshAccountLists();
            UpdateTotalGameStats();
            SaveAccounts();
        }

        #endregion

        #region Update All Accounts

        private async Task UpdateAllAccountsAsync()
        {
            await AccountService.UpdateAllAccountsAsync(_accounts);
        }

        private void UpdateTotalGameStats()
        {
            UIService.UpdateTotalGameStats(this, _accounts);
        }

        #endregion

        #region Riot Client Automation and Settings

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is System.Windows.Controls.TabControl tabControl && tabControl.SelectedItem is TabItem selectedTab)
            {
                // Check if the SettingsTab is selected by its Name property
                if (selectedTab.Name == "SettingsTab") 
                {
                    // Load and display API Key
                    try
                    {
                        var apiKeyTextBox = VisualTreeHelperExtensions.FindVisualChildren<System.Windows.Controls.TextBox>(selectedTab)
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
                        Console.WriteLine("Error loading API key for display: " + ex.Message);
                    }

                    // Load and display Hotkey settings & Run on Startup
                    LoadHotkeySettings(); // This now also loads RunOnStartup setting
                    UpdateHotkeyDisplay(); 
                    UpdateRunOnStartupToggleUI(); // Update the new toggle
                }
            }
        }

        private void btnSaveApiKey_Click(object sender, RoutedEventArgs e)
        {
            var apiKeyTextBox = VisualTreeHelperExtensions.FindVisualChildren<System.Windows.Controls.TextBox>(this)
                .FirstOrDefault(tb => tb.Name == "txtApiKey");
            if (apiKeyTextBox == null)
            {
                System.Windows.MessageBox.Show("Cannot find API key text box.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            string apiKey = apiKeyTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                System.Windows.MessageBox.Show("Please enter a valid API key.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            bool success = ApiKeyManager.SaveApiKey(apiKey);
            if (success)
            {
                apiKeyTextBox.Text = "••••••••••••••••••••" + apiKey.Substring(Math.Max(0, apiKey.Length - 4));
                System.Windows.MessageBox.Show("API key saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show("Failed to save API key. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    System.Windows.MessageBox.Show("League Client is not running. Please start League of Legends first.",
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
            // This method is called when the window is truly closing (e.g., after Application.Shutdown() is called).
            // Ensure resources are released here.
            _hotkeyService?.Dispose(); 
            _notifyIcon?.Dispose(); 
            
            // Cleanup update service events
            if (_updateService != null)
            {
                _updateService.UpdateAvailable -= OnUpdateAvailable;
                _updateService.UpdateProgressChanged -= OnUpdateProgressChanged;
            }
            
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
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
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
                    Console.WriteLine("Error monitoring League client: " + ex.Message);
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

        private BitmapImage? _primaryChampionImage;
        public BitmapImage? PrimaryChampionImage
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
        public event PropertyChangedEventHandler? PropertyChanged;
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
                    Console.WriteLine($"Updated primary champion: {champion.Name}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error setting champion: {ex.Message}");
                    System.Windows.MessageBox.Show($"Error setting champion: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
        private CancellationTokenSource? _autoPickCts;

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
                    Console.WriteLine($"Error in champion select monitor: {ex.Message}");
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
                            Console.WriteLine("Selected summoner spell 1");
                    }

                    if (_autoPickSettings.SummonerSpell2Id > 0)
                    {
                        bool success = LCUService.SelectSummonerSpell(_autoPickSettings.SummonerSpell2Id, 2);
                        if (success)
                            Console.WriteLine("Selected summoner spell 2");
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
                Console.WriteLine($"Error handling champion select: {ex.Message}");
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
                        Console.WriteLine($"Hovering secondary champion: {hoverSuccess}");
                    }

                    // Lock in champion
                    if (_autoPickSettings.InstantLock)
                    {
                        bool lockSuccess = LCUService.SelectChampion(_autoPickSettings.PickChampionId, actId, true);
                        Console.WriteLine($"Locking in champion: {lockSuccess}");
                    }
                    else if (_autoPickSettings.PickLockDelayMs > 0)
                    {
                        await Task.Delay(_autoPickSettings.PickLockDelayMs);
                        bool lockSuccess = LCUService.SelectChampion(_autoPickSettings.PickChampionId, actId, true);
                        Console.WriteLine($"Locking in champion after delay: {lockSuccess}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling pick: {ex.Message}");
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
                    Console.WriteLine($"Hovering ban: {hoverSuccess}");

                    // Lock in ban
                    if (_autoPickSettings.InstantBan)
                    {
                        bool lockSuccess = LCUService.SelectChampion(_autoPickSettings.BanChampionId, actId, true);
                        Console.WriteLine($"Locking in ban: {lockSuccess}");
                    }
                    else if (_autoPickSettings.BanLockDelayMs > 0)
                    {
                        await Task.Delay(_autoPickSettings.BanLockDelayMs);
                        bool lockSuccess = LCUService.SelectChampion(_autoPickSettings.BanChampionId, actId, true);
                        Console.WriteLine($"Locking in ban after delay: {lockSuccess}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling ban: {ex.Message}");
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
                Console.WriteLine($"Error loading auto-pick settings: {ex.Message}");
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
                Console.WriteLine($"Error saving auto-pick settings: {ex.Message}");
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
                Console.WriteLine($"Error loading images: {ex.Message}");
            }
        }

        private BitmapImage? _secondaryChampionImage;
        public BitmapImage? SecondaryChampionImage
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

        private BitmapImage? _banChampionImage;
        public BitmapImage? BanChampionImage
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

        private BitmapImage? _spell1Image;
        public BitmapImage? Spell1Image
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

        private BitmapImage? _spell2Image;
        public BitmapImage? Spell2Image
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

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Check if the window is actually closing or just being hidden
            if (this.IsVisible) // Or any other condition that implies it's not a true exit
            {
                e.Cancel = true; // Prevent the window from closing
                this.Hide();     // Hide it instead
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = true; // Show tray icon
                }
                Console.WriteLine("MainWindow_Closing: Hid to system tray.");
            }
            else
            {
                // This means the application is actually exiting (e.g., via tray menu)
                Console.WriteLine("MainWindow_Closing: Application is exiting.");
                _hotkeyService?.Dispose();
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }
                // No System.Windows.Application.Current.Shutdown(); here, 
                // as it will be called by the ExitApplication method or by WPF itself on natural shutdown.
            }
        }

        private void LoadHotkeySettings()
        {
            try
            {
                if (File.Exists(_hotkeySettingsFilePath))
                {
                    string json = File.ReadAllText(_hotkeySettingsFilePath);
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        Console.WriteLine("Hotkey settings file is empty. Using defaults and will save them.");
                        _hotkeySettings = new HotkeySettings(); // Use defaults
                        _hotkeySettings.Modifier = GlobalHotkeyService.MOD_CONTROL | GlobalHotkeyService.MOD_ALT;
                        // RunOnStartup is false by default
                        // Save settings to create a valid file for next time.
                        SaveHotkeySettings(); 
                    }
                    else
                    {
                        var loadedSettings = JsonSerializer.Deserialize<HotkeySettings>(json);
                        if (loadedSettings != null)
                        {
                            _hotkeySettings = loadedSettings;
                            // Ensure modifier is always Ctrl + Alt, overriding any saved modifier value for simplicity
                            _hotkeySettings.Modifier = GlobalHotkeyService.MOD_CONTROL | GlobalHotkeyService.MOD_ALT;
                        }
                        else // Deserialization returned null (e.g. invalid JSON content like "null" string)
                        {
                            Console.WriteLine("Failed to deserialize hotkey settings (returned null). Using defaults for this session. Original file NOT overwritten.");
                            _hotkeySettings = new HotkeySettings(); // Use defaults
                            _hotkeySettings.Modifier = GlobalHotkeyService.MOD_CONTROL | GlobalHotkeyService.MOD_ALT;
                            // RunOnStartup is false by default
                            // DO NOT SAVE here to avoid overwriting a potentially recoverable file
                        }
                    }
                }
                else // File does not exist
                {
                    Console.WriteLine("Hotkey settings file not found. Using defaults and creating a new file.");
                    _hotkeySettings = new HotkeySettings(); // Use defaults if file doesn't exist
                    _hotkeySettings.Modifier = GlobalHotkeyService.MOD_CONTROL | GlobalHotkeyService.MOD_ALT;
                    // RunOnStartup is false by default
                    SaveHotkeySettings(); // Save to create the file
                }
            }
            catch (JsonException jsonEx)
            {
                Console.WriteLine($"Error deserializing hotkey settings (JsonException): {jsonEx.Message}. Using defaults for this session. Original file NOT overwritten.");
                _hotkeySettings = new HotkeySettings(); 
                _hotkeySettings.Modifier = GlobalHotkeyService.MOD_CONTROL | GlobalHotkeyService.MOD_ALT;
                // RunOnStartup is false by default
                // DO NOT SAVE here to avoid overwriting a potentially recoverable file
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Generic error loading hotkey settings: {ex.Message}. Using defaults for this session. Original file NOT overwritten.");
                _hotkeySettings = new HotkeySettings(); 
                _hotkeySettings.Modifier = GlobalHotkeyService.MOD_CONTROL | GlobalHotkeyService.MOD_ALT;
                // RunOnStartup is false by default
                 // DO NOT SAVE here to avoid overwriting a potentially recoverable file
            }
            // UI updates are now handled by the caller (e.g., constructor or TabControl_SelectionChanged)
        }

        private void SaveHotkeySettings()
        {
            try
            {
                _hotkeySettings.Modifier = GlobalHotkeyService.MOD_CONTROL | GlobalHotkeyService.MOD_ALT;
                string keyChar = txtHotkeyKey.Text.ToUpper();
                if (!string.IsNullOrEmpty(keyChar) && keyChar.Length == 1)
                {
                    _hotkeySettings.VirtualKey = GlobalHotkeyService.GetVirtualKeyCode(keyChar[0]);
                }
                else
                {
                    _hotkeySettings.VirtualKey = GlobalHotkeyService.VK_L;
                    txtHotkeyKey.Text = "L"; 
                }
                _hotkeySettings.DisplayName = $"Ctrl + Alt + {txtHotkeyKey.Text.ToUpper()}";
                // RunOnStartup is already set in _hotkeySettings by its toggle's event handler

                string json = JsonSerializer.Serialize(_hotkeySettings, new JsonSerializerOptions { WriteIndented = true });
                Directory.CreateDirectory(Path.GetDirectoryName(_hotkeySettingsFilePath)!); 
                File.WriteAllText(_hotkeySettingsFilePath, json);
                Console.WriteLine($"Settings saved: Hotkey={_hotkeySettings.DisplayName}, RunOnStartup={_hotkeySettings.RunOnStartup}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving settings: {ex.Message}");
                System.Windows.MessageBox.Show($"Error saving settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateHotkeyDisplay()
        {
            // Ensure UI elements are available before trying to update them.
            // This is important if called early in startup.
            if (txtHotkeyKey == null || lblCurrentHotkey == null) 
            {
                Console.WriteLine("UpdateHotkeyDisplay: UI elements not ready yet.");
                return;
            }

            string keyChar = GlobalHotkeyService.GetKeyFromVirtualCode(_hotkeySettings.VirtualKey);
            if (string.IsNullOrEmpty(keyChar) || _hotkeySettings.VirtualKey == 0) 
            {
                keyChar = "L"; 
                _hotkeySettings.VirtualKey = GlobalHotkeyService.VK_L; 
            }
            txtHotkeyKey.Text = keyChar;
            lblCurrentHotkey.Text = $"Current Hotkey: Ctrl + Alt + {keyChar}";
            _hotkeySettings.DisplayName = lblCurrentHotkey.Text.Replace("Current Hotkey: ", "");
            Console.WriteLine($"Hotkey display updated: {_hotkeySettings.DisplayName}");
        }

        private void UpdateRunOnStartupToggleUI()
        {
            if (tglRunOnStartup == null)
            {
                Console.WriteLine("UpdateRunOnStartupToggleUI: Toggle button not ready yet.");
                return;
            }
            tglRunOnStartup.IsChecked = _hotkeySettings.RunOnStartup;
            tglRunOnStartup.Content = _hotkeySettings.RunOnStartup ? "ON" : "OFF";
            Console.WriteLine($"Run on startup toggle UI updated: {_hotkeySettings.RunOnStartup}");
        }

        private void btnSaveHotkey_Click(object sender, RoutedEventArgs e)
        {
            string newKey = txtHotkeyKey.Text.ToUpper();
            if (string.IsNullOrEmpty(newKey) || newKey.Length > 1 || !char.IsLetterOrDigit(newKey[0]))
            {
                System.Windows.MessageBox.Show("Please enter a single valid letter or digit for the hotkey.", "Invalid Hotkey", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtHotkeyKey.Text = GlobalHotkeyService.GetKeyFromVirtualCode(_hotkeySettings.VirtualKey); 
                return;
            }

            uint newVirtualKey = GlobalHotkeyService.GetVirtualKeyCode(newKey[0]);
            uint fixedModifier = GlobalHotkeyService.MOD_CONTROL | GlobalHotkeyService.MOD_ALT;

            _hotkeyService?.Unregister();

            _hotkeySettings.Modifier = fixedModifier;
            _hotkeySettings.VirtualKey = newVirtualKey;
            _hotkeySettings.DisplayName = $"Ctrl + Alt + {newKey}";

            if (_hotkeyService != null)
            {
                bool registered = _hotkeyService.Register(_hotkeySettings.Modifier, _hotkeySettings.VirtualKey);
                if (registered)
                {
                    Console.WriteLine($"New hotkey registered: {_hotkeySettings.DisplayName}");
                    SaveHotkeySettings(); 
                    UpdateHotkeyDisplay(); 
                    System.Windows.MessageBox.Show($"Hotkey updated to: {_hotkeySettings.DisplayName}", "Hotkey Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    Console.WriteLine($"Failed to register new hotkey: {_hotkeySettings.DisplayName}.");
                    LoadHotkeySettings(); 
                    InitializeHotkeyService(); 
                    System.Windows.MessageBox.Show($"Failed to register hotkey: Ctrl + Alt + {newKey}. It might be in use. Reverted to previous or default.", "Hotkey Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                Console.WriteLine("Hotkey service is null. Cannot save or register hotkey.");
                 System.Windows.MessageBox.Show("Hotkey service is not available. Cannot save hotkey.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void tglRunOnStartup_Checked(object sender, RoutedEventArgs e)
        {
            _hotkeySettings.RunOnStartup = true;
            if (StartupManager.AddToStartup())
            {
                Console.WriteLine("Successfully added to startup.");
            }
            else
            {
                Console.WriteLine("Failed to add to startup.");
                // Optionally revert UI or show error to user
                _hotkeySettings.RunOnStartup = false; // Revert setting
                System.Windows.MessageBox.Show("Failed to add the application to Windows startup. Please check permissions.", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            SaveHotkeySettings(); // Save all settings
            UpdateRunOnStartupToggleUI(); // Reflect current state
        }

        private void tglRunOnStartup_Unchecked(object sender, RoutedEventArgs e)
        {
            _hotkeySettings.RunOnStartup = false;
            if (StartupManager.RemoveFromStartup())
            {
                Console.WriteLine("Successfully removed from startup.");
            }
            else
            {
                Console.WriteLine("Failed to remove from startup.");
                // Optionally revert UI or show error to user
                 _hotkeySettings.RunOnStartup = true; // Revert setting if removal failed critically
                System.Windows.MessageBox.Show("Failed to remove the application from Windows startup. Please check permissions or if it was registered.", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            SaveHotkeySettings(); // Save all settings
            UpdateRunOnStartupToggleUI(); // Reflect current state
        }

        private string BuildHotkeyDisplayName(uint modifiers, string key)
        {
            // This method is now simpler as modifiers are fixed.
            return $"Ctrl + Alt + {key.ToUpper()}";
        }

        private void txtHotkeyKey_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Allow only a single alphanumeric character
            if (e.Text.Length == 1 && char.IsLetterOrDigit(e.Text[0]))
            {
                // If TextBox already has text, replace it (effectively ensuring only one char)
                if (txtHotkeyKey.Text.Length > 0)
                {
                    txtHotkeyKey.Text = ""; 
                }
                // e.Handled will be false, allowing the input
            }
            else
            {
                e.Handled = true; // Disallow input
            }
        }

        private void ApplyTheme()
        {
            UIService.ApplyTheme(this, _isDarkMode);
            RefreshAccountLists();
            UpdateLayout();
        }

        private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Handle Escape key to close quick login popup
            if (e.Key == Key.Escape && _quickLoginPopup != null && _quickLoginPopup.IsOpen)
            {
                Console.WriteLine("Escape key pressed in main window - closing quick login popup");
                _quickLoginPopup.IsOpen = false;
                e.Handled = true;
                return;
            }
            
            // Enable Enter key for quick login when an account is selected
            if (e.Key == Key.Enter && lbLoginAccounts?.SelectedItem is Account account)
            {
                Console.WriteLine($"Enter key pressed for login: {account.GameName}");
                
                // Show Riot Client window if it exists
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
                
                // Perform login
                string decryptedPassword = EncryptionService.DecryptString(account.EncryptedPassword);
                if (!string.IsNullOrEmpty(decryptedPassword))
                {
                    _ = RiotClientAutomationService.LaunchAndLoginAsync(account.AccountName, decryptedPassword);
                }
                else
                {
                    System.Windows.MessageBox.Show("Failed to decrypt password for the selected account.", 
                        "Login Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                
                e.Handled = true;
            }
            // Add debug hotkey F12 to test account loading
            else if (e.Key == Key.F12)
            {
                Console.WriteLine("=== F12 DEBUG INFO ===");
                Console.WriteLine($"Total accounts loaded: {_accounts?.Count ?? 0}");
                Console.WriteLine($"lbLoginAccounts is null: {lbLoginAccounts == null}");
                Console.WriteLine($"icLoginAccounts is null: {icLoginAccounts == null}");
                
                if (_accounts != null)
                {
                    foreach (var acc in _accounts)
                    {
                        Console.WriteLine($"Account: {acc.GameName} - {acc.AccountName}");
                    }
                }
                
                // Try to find account cards in the UI
                if (icLoginAccounts != null)
                {
                    var borders = VisualTreeHelperExtensions.FindVisualChildren<Border>(icLoginAccounts).ToList();
                    Console.WriteLine($"Found {borders.Count} borders in icLoginAccounts");
                    
                    foreach (var border in borders)
                    {
                        if (border.Tag is Account acc)
                        {
                            Console.WriteLine($"Border with account: {acc.GameName}, Name: {border.Name ?? "null"}");
                        }
                        else
                        {
                            Console.WriteLine($"Border without account tag, Name: {border.Name ?? "null"}");
                        }
                    }
                }
                
                e.Handled = true;
            }
        }

        // Add flag to prevent multiple login attempts
        private bool _isLoginInProgress = false;

        private static void FocusRiotClientWindow()
        {
            try
            {
                Console.WriteLine("Attempting to focus Riot Client window...");
                
                // Try different process names that Riot Client might use
                string[] processNames = { "Riot Client", "RiotClientServices", "RiotClientUx" };
                Process riotProcess = null;
                
                foreach (string processName in processNames)
                {
                    Process[] processes = Process.GetProcessesByName(processName);
                    if (processes.Length > 0)
                    {
                        // Find the process with a visible main window
                        foreach (Process proc in processes)
                        {
                            if (proc.MainWindowHandle != IntPtr.Zero)
                            {
                                riotProcess = proc;
                                Console.WriteLine($"Found Riot Client process: {processName} (ID: {proc.Id})");
                                break;
                            }
                        }
                        if (riotProcess != null) break;
                    }
                }
                
                if (riotProcess == null)
                {
                    Console.WriteLine("No Riot Client process with visible window found.");
                    return;
                }
                
                IntPtr hWnd = riotProcess.MainWindowHandle;
                if (hWnd == IntPtr.Zero)
                {
                    Console.WriteLine("Riot Client window handle is invalid.");
                    return;
                }
                
                Console.WriteLine($"Riot Client window handle: {hWnd}");
                
                // Multi-step approach to ensure window gets focus
                
                // Step 1: Restore if minimized
                if (IsIconic(hWnd))
                {
                    Console.WriteLine("Window is minimized, restoring...");
                    ShowWindow(hWnd, SW_RESTORE);
                    Thread.Sleep(200); // Short delay to allow restore
                }
                else
                {
                    Console.WriteLine("Window is not minimized.");
                }
                
                // Step 2: Show the window
                Console.WriteLine("Showing window...");
                ShowWindow(hWnd, SW_SHOW);
                Thread.Sleep(100);
                
                // Step 3: Bring to top
                Console.WriteLine("Bringing window to top...");
                BringWindowToTop(hWnd);
                Thread.Sleep(100);
                
                // Step 4: Force foreground (with thread attachment trick)
                Console.WriteLine("Setting foreground window...");
                IntPtr foregroundWindow = GetForegroundWindow();
                if (foregroundWindow != hWnd)
                {
                    uint currentThreadId = GetCurrentThreadId();
                    uint targetThreadId = GetWindowThreadProcessId(hWnd, out uint targetProcessId);
                    
                    if (targetThreadId != currentThreadId)
                    {
                        Console.WriteLine($"Attaching threads (current: {currentThreadId}, target: {targetThreadId})");
                        AttachThreadInput(currentThreadId, targetThreadId, true);
                        SetForegroundWindow(hWnd);
                        AttachThreadInput(currentThreadId, targetThreadId, false);
                    }
                    else
                    {
                        SetForegroundWindow(hWnd);
                    }
                }
                
                // Step 5: Final verification
                Thread.Sleep(100);
                IntPtr newForegroundWindow = GetForegroundWindow();
                if (newForegroundWindow == hWnd)
                {
                    Console.WriteLine("✅ Successfully focused Riot Client window!");
                }
                else
                {
                    Console.WriteLine($"⚠️ Window focus may not have worked. Current foreground: {newForegroundWindow}, Expected: {hWnd}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error focusing Riot Client window: {ex.Message}");
            }
        }

        private async void PerformAccountLogin(Account account)
        {
            if (_isLoginInProgress)
            {
                Console.WriteLine("Login already in progress, skipping...");
                return;
            }

            _isLoginInProgress = true;
            try
            {
                Console.WriteLine($"Starting login for: {account.GameName}");
                
                // Focus the Riot Client window using the robust method
                FocusRiotClientWindow();
                
                // Small delay to ensure window is ready for automation
                await Task.Delay(300);
                
                await RiotClientAutomationService.LaunchAndLoginAsync(
                    account.AccountName,
                    EncryptionService.DecryptString(account.EncryptedPassword));
            }
            finally
            {
                _isLoginInProgress = false;
            }
        }

        private Point GetMouseScreenCenter()
        {
            try
            {
                // Get current cursor position
                if (!GetCursorPos(out POINT cursorPos))
                {
                    Console.WriteLine("Failed to get cursor position, using primary screen center");
                    return GetPrimaryScreenCenter();
                }

                // Get the monitor that contains the cursor
                IntPtr hMonitor = MonitorFromPoint(cursorPos, MONITOR_DEFAULTTONEAREST);
                if (hMonitor == IntPtr.Zero)
                {
                    Console.WriteLine("Failed to get monitor info, using primary screen center");
                    return GetPrimaryScreenCenter();
                }

                // Get monitor information
                MONITORINFO monitorInfo = new MONITORINFO();
                monitorInfo.cbSize = (uint)Marshal.SizeOf(typeof(MONITORINFO));
                
                if (!GetMonitorInfo(hMonitor, ref monitorInfo))
                {
                    Console.WriteLine("Failed to get monitor details, using primary screen center");
                    return GetPrimaryScreenCenter();
                }

                // Calculate center of the work area (excluding taskbar)
                double centerX = (monitorInfo.rcWork.Left + monitorInfo.rcWork.Right) / 2.0;
                double centerY = (monitorInfo.rcWork.Top + monitorInfo.rcWork.Bottom) / 2.0;

                Console.WriteLine($"Mouse screen center: {centerX}, {centerY}");
                return new Point(centerX, centerY);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting mouse screen center: {ex.Message}");
                return GetPrimaryScreenCenter();
            }
        }

        private Point GetPrimaryScreenCenter()
        {
            // Fallback to primary screen center
            double centerX = SystemParameters.PrimaryScreenWidth / 2.0;
            double centerY = SystemParameters.PrimaryScreenHeight / 2.0;
            return new Point(centerX, centerY);
        }

        // Click outside monitoring for popup
        private System.Windows.Threading.DispatcherTimer? _clickMonitorTimer;
        private Rect _popupBounds;

        private void StartClickOutsideMonitoring(double popupX, double popupY, double popupWidth, double popupHeight)
        {
            // Store popup bounds with some padding to avoid edge cases
            _popupBounds = new Rect(popupX - 5, popupY - 5, popupWidth + 10, popupHeight + 10);
            
            // Create timer to check for clicks outside
            _clickMonitorTimer?.Stop();
            _clickMonitorTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100) // Check every 100ms for better reliability
            };
            
            bool wasMousePressed = false;
            
            _clickMonitorTimer.Tick += (sender, e) =>
            {
                try
                {
                    // Check for Escape key as a reliable fallback
                    if (Keyboard.IsKeyDown(Key.Escape))
                    {
                        Console.WriteLine("Escape key detected in monitoring timer - closing popup");
                        if (_quickLoginPopup != null && _quickLoginPopup.IsOpen)
                        {
                            _quickLoginPopup.IsOpen = false;
                        }
                        return;
                    }
                    
                    // Get current cursor position
                    if (GetCursorPos(out POINT cursorPos))
                    {
                        Point mousePos = new Point(cursorPos.X, cursorPos.Y);
                        
                        // Check if left mouse button is currently pressed
                        bool isMousePressed = (System.Windows.Forms.Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left;
                        
                        // Detect mouse button release after being pressed (end of click)
                        if (wasMousePressed && !isMousePressed)
                        {
                            // Check if the release happened outside popup bounds
                            if (!_popupBounds.Contains(mousePos))
                            {
                                Console.WriteLine($"Click released outside popup at ({mousePos.X}, {mousePos.Y}) - closing popup");
                                if (_quickLoginPopup != null && _quickLoginPopup.IsOpen)
                                {
                                    // Use BeginInvoke for better thread safety
                                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        if (_quickLoginPopup != null && _quickLoginPopup.IsOpen)
                                        {
                                            _quickLoginPopup.IsOpen = false;
                                        }
                                    }));
                                }
                            }
                        }
                        
                        wasMousePressed = isMousePressed;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in click monitoring: {ex.Message}");
                }
            };
            
            _clickMonitorTimer.Start();
            Console.WriteLine($"Started improved click outside monitoring and Escape key detection for bounds: ({_popupBounds.X}, {_popupBounds.Y}, {_popupBounds.Width}x{_popupBounds.Height})");
        }

        private void StopClickOutsideMonitoring()
        {
            _clickMonitorTimer?.Stop();
            _clickMonitorTimer = null;
            Console.WriteLine("Stopped click outside monitoring");
        }
    }
}