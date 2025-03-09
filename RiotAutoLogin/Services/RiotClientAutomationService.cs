using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using RiotAutoLogin.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services
{
    using Application = FlaUI.Core.Application;
    public static class RiotClientAutomationService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public static async Task LaunchAndLoginAsync(string username, string password)
        {
            Debug.WriteLine("Starting Riot Client process...");
            Process riotClientProcess = null;

            Process[] processes = Process.GetProcessesByName("Riot Client");
            if (processes.Length > 0)
            {
                riotClientProcess = processes[0];
                Debug.WriteLine("Riot Client already running...");
            }
            else
            {
                Debug.WriteLine("Launching Riot Client...");
                try
                {
                    string riotClientPath = @"C:\Riot Games\Riot Client\RiotClientServices.exe";
                    if (!File.Exists(riotClientPath))
                    {
                        System.Windows.MessageBox.Show("Riot Client not found. Please update the path in the code.",
                            "Client Not Found", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        return;
                    }
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = riotClientPath,
                        Arguments = "--launch-product=league_of_legends --launch-patchline=live"
                    };
                    riotClientProcess = Process.Start(startInfo);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error launching Riot Client: {ex.Message}", "Launch Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return;
                }
            }

            Debug.WriteLine("Waiting for login form...");
            await Task.Delay(500);

            using (var automation = new UIA3Automation())
            {
                if (riotClientProcess != null && !riotClientProcess.HasExited)
                {
                    await AutomateLoginAsync(riotClientProcess, automation, username, password);
                }
                else
                {
                    Debug.WriteLine("Riot Client process is not available or already exited.");
                }
            }
        }

        private static async Task AutomateLoginAsync(Process process, UIA3Automation automation, string username, string password)
        {
            var app = Application.Attach(process);
            var mainWindow = app.GetMainWindow(automation);
            if (mainWindow == null)
            {
                Debug.WriteLine("Could not find main window.");
                return;
            }

            var riotClientPane = mainWindow.FindFirstDescendant(
                cf => cf.ByName("Riot Client").And(cf.ByControlType(ControlType.Pane))
            );
            var parentElement = riotClientPane ?? mainWindow;

            var usernameEdit = parentElement.FindFirstDescendant(
                cf => cf.ByAutomationId("username").And(cf.ByControlType(ControlType.Edit))
            );
            if (usernameEdit == null)
            {
                Debug.WriteLine("Username field not found.");
                return;
            }
            usernameEdit.Focus();
            usernameEdit.Patterns.Value.Pattern.SetValue(string.Empty);
            usernameEdit.Patterns.Value.Pattern.SetValue(username);

            var passwordEdit = parentElement.FindFirstDescendant(
                cf => cf.ByAutomationId("password").And(cf.ByControlType(ControlType.Edit))
            );
            if (passwordEdit == null)
            {
                Debug.WriteLine("Password field not found.");
                return;
            }
            passwordEdit.Focus();
            passwordEdit.Patterns.Value.Pattern.SetValue(string.Empty);
            passwordEdit.Patterns.Value.Pattern.SetValue(password);

            var loginButton = parentElement.FindFirstDescendant(
                cf => cf.ByName("Přihlásit se").And(cf.ByControlType(ControlType.Button))
            );
            if (loginButton != null)
            {
                Debug.WriteLine("Clicking 'Přihlásit se' button...");
                loginButton.AsButton().Click();
            }
            else
            {
                var allButtons = parentElement.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
                Debug.WriteLine($"Found {allButtons.Length} buttons, looking for the 9th...");
                if (allButtons.Length >= 9)
                {
                    Debug.WriteLine("Clicking the 9th button to log in...");
                    allButtons[8].AsButton().Click();
                }
                else
                {
                    Debug.WriteLine("Less than 9 buttons found. Could not click the 9th button.");
                    return;
                }
            }

            Debug.WriteLine("Login button clicked. Waiting...");
            await Task.Delay(3000);
        }

        public static async Task<string> GetRankAsync(string gameName, string tagLine, string region)
        {
            string apiKey = ApiKeyManager.GetApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return "No API key provided.";
            }
            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                string accountUrl = $"https://europe.api.riotgames.com/riot/account/v1/accounts/by-riot-id/{Uri.EscapeDataString(gameName)}/{Uri.EscapeDataString(tagLine)}?api_key={apiKey}";

                HttpResponseMessage accountResponse = await _httpClient.GetAsync(accountUrl);
                if (!accountResponse.IsSuccessStatusCode)
                {
                    string err = await accountResponse.Content.ReadAsStringAsync();
                    Debug.WriteLine("Account Error: " + err);
                    return "Error retrieving account info.";
                }

                string accountJson = await accountResponse.Content.ReadAsStringAsync();
                string puuid = null;
                using (JsonDocument doc = JsonDocument.Parse(accountJson))
                {
                    if (!doc.RootElement.TryGetProperty("puuid", out var puuidElement))
                    {
                        return "Error: puuid not found.";
                    }
                    puuid = puuidElement.GetString();
                }

                if (string.IsNullOrEmpty(puuid))
                    return "Error retrieving puuid.";

                string leagueUrl = $"https://{region}.api.riotgames.com/lol/league/v4/entries/by-puuid/{Uri.EscapeDataString(puuid)}?api_key={apiKey}";
                HttpResponseMessage leagueResponse = await _httpClient.GetAsync(leagueUrl);

                if (!leagueResponse.IsSuccessStatusCode)
                {
                    string errLeague = await leagueResponse.Content.ReadAsStringAsync();
                    Debug.WriteLine("League Error: " + errLeague);
                    return "Error retrieving league info.";
                }

                string leagueJson = await leagueResponse.Content.ReadAsStringAsync();
                var entries = JsonSerializer.Deserialize<System.Collections.Generic.List<LeagueEntry>>(leagueJson);

                if (entries == null || entries.Count == 0)
                    return "Unranked";

                var soloEntry = entries.Find(x => x.queueType == "RANKED_SOLO_5x5");
                if (soloEntry == null)
                    return "Unranked";

                return $"{soloEntry.tier} {soloEntry.rank} ({soloEntry.leaguePoints} LP, {soloEntry.wins}W/{soloEntry.losses}L)";
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception in GetRankAsync: " + ex.Message);
                return "Error retrieving rank.";
            }
        }
    }
}
