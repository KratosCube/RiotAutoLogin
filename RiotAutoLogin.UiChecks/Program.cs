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
            var converter = new StringToImageSourceConverter();
            var image = converter.Convert(path, typeof(BitmapSource), null!, System.Globalization.CultureInfo.InvariantCulture);
            if (image is not BitmapSource { PixelWidth: 192, PixelHeight: 96 })
                throw new Exception("Account cards did not decode a proportional thumbnail.");
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

            var chooser = new QuickLoginWindow((List<Account>)saved.ItemsSource);
            try
            {
                chooser.ShowCenteredOnCursor();
                if (chooser.FindName("AccountList") is not ItemsControl choices || choices.Items.Count != 1)
                    throw new Exception("Quick Login did not display the saved account.");
                var bounds = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Control.MousePosition).WorkingArea;
                var handle = new System.Windows.Interop.WindowInteropHelper(chooser).Handle;
                if (!GetWindowRect(handle, out var rect) ||
                    Math.Abs((rect.Left + rect.Right) / 2.0 - (bounds.Left + bounds.Right) / 2.0) > 2 ||
                    Math.Abs((rect.Top + rect.Bottom) / 2.0 - (bounds.Top + bounds.Bottom) / 2.0) > 2)
                    throw new Exception("Quick Login is not centered in the cursor monitor's work area.");
                Console.WriteLine("Quick Login rendered with a saved account.");
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
