using System;
using System.Windows;
using RiotAutoLogin.Services;
using Velopack;

namespace RiotAutoLogin
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        [STAThread]
        private static void Main()
        {
            // Handle updater hooks before constructing WPF or starting client monitors.
            // A downloaded update must still wait for the user's Install & Restart action.
            VelopackApp.Build()
                .SetAutoApplyOnStartup(false)
                .OnFirstRun(_ => StartupManager.RebindExistingStartup())
                .OnRestarted(_ => StartupManager.RebindExistingStartup())
                .OnBeforeUninstallFastCallback(_ =>
                {
                    if (StartupManager.IsRegisteredForStartup()) StartupManager.RemoveFromStartup();
                })
                .Run();

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }

}
