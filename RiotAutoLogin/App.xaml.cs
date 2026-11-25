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
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--describe-build") >= 0)
            {
                System.IO.File.WriteAllText("build-channel.json", System.Text.Json.JsonSerializer.Serialize(new
                {
                    development = BuildChannel.IsDevelopment, dataDirectory = BuildChannel.DataDirectoryName,
                    startupSuffix = BuildChannel.StartupSuffix
                }));
                return;
            }
            // Handle updater hooks before constructing WPF or starting client monitors.
            // A downloaded update still waits for the user's Update & Restart action.
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
