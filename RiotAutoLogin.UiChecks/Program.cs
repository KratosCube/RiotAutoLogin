using System.Windows;
using System.Windows.Controls;
using RiotAutoLogin;
using RiotAutoLogin.Models;
using Velopack;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
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
        }
        finally
        {
            window.Hide();
            window.Close();
            app.Shutdown();
        }
    }
}
