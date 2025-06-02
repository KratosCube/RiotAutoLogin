using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services
{
    using Application = FlaUI.Core.Application;

    public static class LeagueClientAutomationService
    {
        private static CancellationTokenSource? _autoAcceptCts;

        public static void StartAutoAccept()
        {
            // Cancel any existing auto-accept task
            StopAutoAccept();

            // Create a new cancellation token source
            _autoAcceptCts = new CancellationTokenSource();

            // Start the monitoring task
            Task.Run(() => MonitorForMatchAsync(_autoAcceptCts.Token));
        }

        public static void StopAutoAccept()
        {
            _autoAcceptCts?.Cancel();
            _autoAcceptCts?.Dispose();
            _autoAcceptCts = null;
        }

        private static async Task MonitorForMatchAsync(CancellationToken cancellationToken)
        {
            Debug.WriteLine("Starting auto-accept monitoring...");

            using (var automation = new UIA3Automation())
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Look for the League Client process
                        Process[] processes = Process.GetProcessesByName("LeagueClientUx");
                        if (processes.Length == 0)
                        {
                            // League client not running, wait and check again
                            await Task.Delay(2000, cancellationToken);
                            continue;
                        }

                        var app = Application.Attach(processes[0]);
                        var mainWindow = app.GetMainWindow(automation);

                        if (mainWindow == null)
                        {
                            await Task.Delay(1000, cancellationToken);
                            continue;
                        }

                        // Look for the match accept button
                        var acceptButton = FindAcceptButton(mainWindow);
                        if (acceptButton != null)
                        {
                            Debug.WriteLine("Match found! Clicking accept button...");
                            acceptButton.Click();

                            // Wait a bit longer after clicking to avoid double clicks
                            await Task.Delay(5000, cancellationToken);
                        }

                        // Short delay between checks to avoid high CPU usage
                        await Task.Delay(1000, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // Cancellation requested, exit the loop
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in auto-accept monitor: {ex.Message}");
                        await Task.Delay(2000, cancellationToken);
                    }
                }
            }

            Debug.WriteLine("Auto-accept monitoring stopped.");
        }

        private static Button FindAcceptButton(Window mainWindow)
        {
            try
            {
                // First try to find by automation ID (most reliable)
                var acceptButton = mainWindow.FindFirstDescendant(cf =>
                    cf.ByAutomationId("accept-button").And(cf.ByControlType(ControlType.Button)));

                if (acceptButton != null)
                {
                    return acceptButton.AsButton();
                }

                // If that doesn't work, try by name (in different languages)
                string[] possibleNames = { "Accept", "Akzeptieren", "Accepter", "Aceptar", "Aceitar", "接受", "수락" };

                foreach (var name in possibleNames)
                {
                    var button = mainWindow.FindFirstDescendant(cf =>
                        cf.ByName(name).And(cf.ByControlType(ControlType.Button)));

                    if (button != null)
                    {
                        return button.AsButton();
                    }
                }

                // If specific identifiers fail, we'll try to find buttons in the match found section
                var matchFoundText = mainWindow.FindFirstDescendant(cf =>
                    cf.ByName("MATCH FOUND").Or(cf.ByName("Match Found")));

                if (matchFoundText != null)
                {
                    // Look for a button in the parent container of the "MATCH FOUND" text
                    var parent = matchFoundText.Parent;
                    if (parent != null)
                    {
                        var buttons = parent.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
                        if (buttons.Length > 0)
                        {
                            // Assuming the first or largest button is the accept button
                            return buttons[0].AsButton();
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding accept button: {ex.Message}");
                return null;
            }
        }
    }
}