using System.Windows;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using RiotAutoLogin;
using RiotAutoLogin.Controls;
using RiotAutoLogin.Converters;
using RiotAutoLogin.Models;
using RiotAutoLogin.Services;
using Velopack;
using TabControl = System.Windows.Controls.TabControl;
using ListBox = System.Windows.Controls.ListBox;

internal static class Program
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    private static bool CanRegisterQuickLogin(uint key)
    {
        const int probeId = 100;
        if (RegisterHotKey(IntPtr.Zero, probeId, GlobalHotkeyService.MOD_CONTROL | GlobalHotkeyService.MOD_ALT, key))
        {
            UnregisterHotKey(IntPtr.Zero, probeId);
            return true;
        }
        int error = Marshal.GetLastWin32Error();
        if (error != 1409) throw new System.ComponentModel.Win32Exception(error);
        return false;
    }

    private static void CheckAvatarThumbnail()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        try
        {
            using var original = new System.Drawing.Bitmap(2400, 1200);
            original.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            var account = new Account { AvatarPath = path };
            var image = account.AvatarThumbnail;
            if (image is not BitmapSource { PixelWidth: 192, PixelHeight: 96, IsFrozen: true } ||
                !ReferenceEquals(image, account.AvatarThumbnail))
                throw new Exception("Account cards did not decode a proportional thumbnail.");
            if (!ReferenceEquals(image, Task.Run(() => account.AvatarThumbnail).GetAwaiter().GetResult()))
                throw new Exception("The background binding did not reuse the frozen thumbnail.");
            account.AvatarPath = string.Empty;
            if (account.AvatarThumbnail != null)
                throw new Exception("Clearing the avatar left the old image visible.");
            // The loaded thumbnail must release the user's file.
            using var exclusive = System.IO.File.Open(path, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None);
        }
        finally { System.IO.File.Delete(path); }
    }

    [STAThread]
    private static void Main()
    {
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var window = new MainWindow();
        try
        {
            var keyBox = (System.Windows.Controls.TextBox)window.FindName("txtHotkeyKey")!;
            uint shortcut = GlobalHotkeyService.GetVirtualKeyCode(keyBox.Text.Single());
            if (!CanRegisterQuickLogin(shortcut))
                throw new Exception("The configured Quick Login shortcut was already in use before the test.");
            new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            if (CanRegisterQuickLogin(shortcut))
                throw new Exception("Quick Login was unavailable when the main window handle was created.");
            Console.WriteLine("Windows registered the configured Quick Login shortcut before the first layout.");

            var tabs = (TabControl)window.FindName("mainTabs")!;
            var accountsTab = (TabItem)window.FindName("AccountsTab")!;
            var saved = (ListBox)window.FindName("lbAccounts")!;
            var cards = (ItemsControl)window.FindName("icAccounts")!;
            var alertsHost = (ContentControl)window.FindName("clientAlertsSettingsHost")!;
            if (alertsHost.Content != null)
                throw new Exception("The alert settings card was built before Settings was opened.");
            if (tabs == null || accountsTab == null || saved == null || cards == null)
                throw new Exception("The Accounts controls are missing.");

            saved.ItemsSource = new List<Account>
            {
                new() { AccountName = "sample", GameName = "David", TagLine = "test", Region = "eun1" }
            };
            tabs.SelectedItem = accountsTab;
            window.Show();
            window.UpdateLayout();
            if (!ReferenceEquals(tabs.SelectedItem, accountsTab) || cards.Items.Count != 1)
                throw new Exception("The Accounts tab did not display the saved account.");
            Console.WriteLine("Accounts tab rendered with a saved account.");
            AccountGridChecks.Run(window, tabs);
            tabs.SelectedItem = accountsTab;
            window.Width = window.MinWidth; window.Height = window.MinHeight; window.UpdateLayout();
            var form = (ScrollViewer)window.FindName("accountFormScroll");
            if (form.ScrollableHeight > 1 || form.ScrollableWidth > 1) throw new Exception("The account form must fit the minimum window size.");
            AccountGridChecks.Capture(window, "accounts-editor-preview.png");
            using (var updates = new UpdateService("KratosCube", "RiotAutoLogin"))
            {
                var dialog = new UpdateNotificationWindow(new UpdateInfo
                {
                    CurrentVersion = new Version(1, 3, 16), LatestVersion = new Version(1, 3, 17), FileSize = 8_400_000,
                    Delivery = UpdateDelivery.Package, UsesDelta = true, FullDownloadSize = 85_000_000,
                    Changelog = "A clearer overview\\n\\n• Annual CS across your accounts\\n• Direct Results, Farming and Time controls\\n• Client position filters for Remote Pick\\n• Developer previews from feature branches".Replace("\\n", "\n")
                }, updates) { Owner = window };
                dialog.Show(); dialog.UpdateLayout();
                foreach (string name in new[] { "btnDownload", "btnLater", "btnClose", "chkFutureUpdateNotifications" })
                {
                    var element = (FrameworkElement)dialog.FindName(name);
                    var rect = element.TransformToAncestor(dialog).TransformBounds(new Rect(element.RenderSize));
                    if (!new Rect(dialog.RenderSize).Contains(rect)) throw new Exception("Update dialog control clipped: " + name);
                }
                AccountGridChecks.Capture(dialog, "update-dialog-preview.png");
                dialog.Close();
            }

            var popupAccounts = new List<Account>
            {
                new() { GameName = "Ashen Fox", TagLine = "EUW", Region = "euw1",
                    QueueRanks = new() { [RankedQueue.SoloDuo] = new RankData { Tier = "EMERALD", Rank = "II", Wins = 24, Losses = 16 } } },
                new() { GameName = "North Star", TagLine = "R2", Region = "eun1",
                    QueueRanks = new() { [RankedQueue.SoloDuo] = new RankData { Tier = "GRANDMASTER", Wins = 6, Losses = 4 } } },
                new() { GameName = "A longer summoner name", TagLine = "R3", Region = "eun1",
                    QueueRanks = new() { [RankedQueue.SoloDuo] = new RankData { Tier = "SILVER", Rank = "IV", Wins = 3, Losses = 7 } } },
                new() { GameName = "New account", TagLine = "R4", Region = "euw1" }
            };
            double[] sampleRanks = [2250, 2850, 1100];
            for (int i = 0; i < sampleRanks.Length; i++)
                popupAccounts[i].ApplyOpponentRanks(RankedQueue.SoloDuo, new OpponentRankSummary
                {
                    MeanRankPosition = sampleRanks[i], Games = 10, RankedOpponents = 48,
                    TotalOpponents = 50, UpdatedUtc = DateTime.UtcNow
                });
            var chooser = new QuickLoginWindow(popupAccounts);
            try
            {
                chooser.ShowCenteredOnCursor();
                if (chooser.FindName("AccountList") is not ItemsControl choices || choices.Items.Count != popupAccounts.Count)
                    throw new Exception("Quick Login did not display the saved account.");
                var bounds = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Control.MousePosition).WorkingArea;
                var handle = new System.Windows.Interop.WindowInteropHelper(chooser).Handle;
                if (!GetWindowRect(handle, out var rect) ||
                    Math.Abs((rect.Left + rect.Right) / 2.0 - (bounds.Left + bounds.Right) / 2.0) > 2 ||
                    Math.Abs((rect.Top + rect.Bottom) / 2.0 - (bounds.Top + bounds.Bottom) / 2.0) > 2)
                    throw new Exception("Quick Login is not centered in the cursor monitor's work area.");
                Console.WriteLine("Quick Login rendered with a saved account.");
                for (int i = 0; i < popupAccounts.Count; i++)
                {
                    var presenter = (ContentPresenter)choices.ItemContainerGenerator.ContainerFromIndex(i);
                    var rate = (ContentPresenter)choices.ItemTemplate.FindName("WinRate", presenter);
                    AccountGridChecks.CheckWinRate(rate, popupAccounts[i].WinFraction);
                    var emblem = (System.Windows.Controls.Image)choices.ItemTemplate.FindName("RankEmblem", presenter);
                    if ((emblem.Source != null) != (i < 3))
                        throw new Exception("The popup rank emblem does not match the saved account.");
                    var button = (System.Windows.Controls.Button)choices.ItemTemplate.FindName("AccountChoice", presenter);
                    var opponents = (ContentPresenter)choices.ItemTemplate.FindName("OpponentRank", presenter);
                    AccountGridChecks.CheckOpponentRank(opponents, popupAccounts[i].OpponentRankLabel);
                    var opponentBounds = opponents.TransformToAncestor(button).TransformBounds(new Rect(opponents.RenderSize));
                    var rateBounds = rate.TransformToAncestor(button).TransformBounds(new Rect(rate.RenderSize));
                    if (!new Rect(button.RenderSize).Contains(rateBounds) || !new Rect(button.RenderSize).Contains(opponentBounds))
                        throw new Exception("The popup win rate extends past the account button.");
                }
                AccountGridChecks.Capture(chooser, "quick-login-preview.png");
            }
            finally { chooser.Close(); }

            window.Close();
            if (window.IsVisible || !app.Windows.Cast<Window>().Contains(window))
                throw new Exception("Closing the main window on launch did not keep it available in the tray.");
            window.Show();
            tabs.SelectedItem = window.FindName("SettingsTab");
            if (alertsHost.Content is not ClientAlertsSettingsCard alerts)
                throw new Exception("Settings did not create its alert controls on first use.");
            tabs.SelectedItem = accountsTab;
            tabs.SelectedItem = window.FindName("SettingsTab");
            if (!ReferenceEquals(alertsHost.Content, alerts))
                throw new Exception("Reopening Settings recreated its alert controls.");
            Console.WriteLine("The main window reopened and Settings reused its controls.");
            CheckAvatarThumbnail();
        }
        finally
        {
            window.Hide();
            window.Close();
            app.Shutdown();
        }
    }
}
