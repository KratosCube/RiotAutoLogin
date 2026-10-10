using System.Windows;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using RiotAutoLogin;
using RiotAutoLogin.Models;
using Velopack;

internal static class Program
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [STAThread]
    private static void Main()
    {
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var window = new MainWindow();
        try
        {
            var tabs = (TabControl)window.FindName("mainTabs")!;
            var accountsTab = (TabItem)window.FindName("AccountsTab")!;
            var saved = (ListBox)window.FindName("lbAccounts")!;
            var cards = (ItemsControl)window.FindName("icAccounts")!;
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

            var chooser = new QuickLoginWindow((List<Account>)saved.ItemsSource);
            try
            {
                chooser.ShowCenteredOnCursor();
                if (chooser.FindName("AccountList") is not ItemsControl choices || choices.Items.Count != 1)
                    throw new Exception("Quick Login did not display the saved account.");
                var bounds = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
                var handle = new System.Windows.Interop.WindowInteropHelper(chooser).Handle;
                if (!GetWindowRect(handle, out var rect) ||
                    Math.Abs((rect.Left + rect.Right) / 2.0 - (bounds.Left + bounds.Right) / 2.0) > 2 ||
                    Math.Abs((rect.Top + rect.Bottom) / 2.0 - (bounds.Top + bounds.Bottom) / 2.0) > 2)
                    throw new Exception("Quick Login is not centered in the cursor monitor's work area.");
                Console.WriteLine("Quick Login rendered with a saved account.");
            }
            finally { chooser.Close(); }
        }
        finally
        {
            window.Hide();
            window.Close();
            app.Shutdown();
        }
    }
}
