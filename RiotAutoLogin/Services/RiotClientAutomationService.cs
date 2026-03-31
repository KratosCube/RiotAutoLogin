using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using RiotAutoLogin.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace RiotAutoLogin.Services
{
    using Application = FlaUI.Core.Application;

    public static class RiotClientAutomationService
    {
        private static readonly HttpClient _httpClient = new();

        static RiotClientAutomationService()
        {
            _httpClient.DefaultRequestHeaders.Add(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public static async Task LaunchAndLoginAsync(string username, string password)
        {
            Debug.WriteLine("Starting Riot Client process...");
            Process? riotClientProcess = null;

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
                    string riotClientPath = FindRiotClientPath();
                    if (string.IsNullOrEmpty(riotClientPath))
                    {
                        MessageBox.Show(
                            "Riot Client not found in common installation locations:\n" +
                            "• C:\\Riot Games\\Riot Client\\RiotClientServices.exe\n" +
                            "• C:\\Program Files\\Riot Games\\Riot Client\\RiotClientServices.exe\n" +
                            "• C:\\Program Files (x86)\\Riot Games\\Riot Client\\RiotClientServices.exe\n\n" +
                            "Please make sure Riot Client is installed or manually start it before using auto-login.",
                            "Client Not Found",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return;
                    }

                    ProcessStartInfo startInfo = new()
                    {
                        FileName = riotClientPath,
                        Arguments = "--launch-product=league_of_legends --launch-patchline=live"
                    };

                    riotClientProcess = Process.Start(startInfo);
                    Debug.WriteLine($"Riot Client launched from: {riotClientPath}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Error launching Riot Client: {ex.Message}",
                        "Launch Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
            }

            Debug.WriteLine("Waiting for login form...");
            await Task.Delay(800);

            using var automation = new UIA3Automation();

            if (riotClientProcess != null && !riotClientProcess.HasExited)
            {
                await Task.Delay(200);
                await AutomateLoginAsync(riotClientProcess, automation, username, password);
            }
            else
            {
                Debug.WriteLine("Riot Client process is not available or already exited.");
                MessageBox.Show(
                    "Riot Client process is not available. Please try again.",
                    "Login Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static async Task AutomateLoginAsync(Process process, UIA3Automation automation, string username, string password)
        {
            var app = Application.Attach(process);
            var mainWindow = app.GetMainWindow(automation);

            if (mainWindow == null)
            {
                Debug.WriteLine("Could not find main window.");
                MessageBox.Show(
                    "Could not find Riot Client main window.",
                    "Login Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            var riotClientPane = mainWindow.FindFirstDescendant(
                cf => cf.ByName("Riot Client").And(cf.ByControlType(ControlType.Pane)));

            var parentElement = riotClientPane ?? mainWindow;

            var usernameEdit = parentElement.FindFirstDescendant(
                cf => cf.ByAutomationId("username").And(cf.ByControlType(ControlType.Edit)));

            if (usernameEdit == null)
            {
                Debug.WriteLine("Username field not found.");
                MessageBox.Show(
                    "Could not find username field. Make sure Riot Client login screen is visible.",
                    "Login Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            try
            {
                usernameEdit.Focus();
                usernameEdit.Patterns.Value.Pattern.SetValue(string.Empty);
                usernameEdit.Patterns.Value.Pattern.SetValue(username);
                Debug.WriteLine("Username filled successfully.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error filling username: {ex.Message}");
                MessageBox.Show(
                    $"Error filling username: {ex.Message}",
                    "Login Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            var passwordEdit = parentElement.FindFirstDescendant(
                cf => cf.ByAutomationId("password").And(cf.ByControlType(ControlType.Edit)));

            if (passwordEdit == null)
            {
                Debug.WriteLine("Password field not found.");
                MessageBox.Show(
                    "Could not find password field. Make sure Riot Client login screen is visible.",
                    "Login Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            try
            {
                passwordEdit.Focus();
                passwordEdit.Patterns.Value.Pattern.SetValue(string.Empty);
                passwordEdit.Patterns.Value.Pattern.SetValue(password);
                Debug.WriteLine("Password filled successfully.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error filling password: {ex.Message}");
                MessageBox.Show(
                    $"Error filling password: {ex.Message}",
                    "Login Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            try
            {
                Debug.WriteLine("Pressing Enter to submit login form...");

                await Task.Delay(100);
                passwordEdit.Focus();
                await Task.Delay(50);

                FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ENTER);

                Debug.WriteLine("Enter key pressed successfully. Waiting for response...");
                await Task.Delay(500);

                Console.WriteLine("✅ Login attempt completed. Check Riot Client for any authentication prompts.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error pressing Enter key: {ex.Message}");
                MessageBox.Show(
                    $"Error submitting login form: {ex.Message}",
                    "Login Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public static async Task<string> GetRankAsync(string gameName, string tagLine, string region)
        {
            if (string.IsNullOrWhiteSpace(gameName) || string.IsNullOrWhiteSpace(tagLine))
            {
                Debug.WriteLine("Invalid game name or tag line provided");
                return "Error: Invalid game name or tag line";
            }

            if (string.IsNullOrWhiteSpace(region))
            {
                Debug.WriteLine("Invalid region provided");
                return "Error: Invalid region";
            }

            try
            {
                Debug.WriteLine($"Starting rank lookup for {gameName}#{tagLine} in region {region}");

                string apiKey = await GetApiKeyAsync();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    Debug.WriteLine("No API key available");
                    return "Error: No API key available. Please set your Riot API key in Settings.";
                }

                Debug.WriteLine("API key found, proceeding with account lookup");

                string? puuid = await GetAccountPuuidByRiotIdAsync(gameName, tagLine, apiKey);
                if (string.IsNullOrWhiteSpace(puuid))
                {
                    Debug.WriteLine($"Account not found for {gameName}#{tagLine}");
                    return $"Error: Account '{gameName}#{tagLine}' not found. Please check spelling and ensure the account exists.";
                }

                Debug.WriteLine($"Account found, getting rank info for region {region}");

                RankData? rankInfo = await GetRankInfoAsync(puuid, region, apiKey);

                if (rankInfo == null)
                {
                    Debug.WriteLine($"Rank lookup completed for {gameName}#{tagLine}: Unranked");
                    return "Unranked";
                }

                string result = FormatRankInfo(rankInfo);
                Debug.WriteLine($"Rank lookup completed for {gameName}#{tagLine}: {result}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting rank for {gameName}#{tagLine}: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        private static Task<string> GetApiKeyAsync()
        {
            string apiKey = ApiKeyManager.GetApiKey()?.Trim() ?? string.Empty;
            return Task.FromResult(apiKey);
        }

        private static async Task<string> SendRiotGetAsync(string url, string apiKey)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("X-Riot-Token", apiKey);

            using var response = await _httpClient.SendAsync(request);
            string content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw CreateRiotApiException(response.StatusCode, content);
            }

            return content;
        }

        private static Exception CreateRiotApiException(HttpStatusCode statusCode, string responseBody)
        {
            int code = (int)statusCode;

            return code switch
            {
                400 => new InvalidOperationException("Bad Riot API request."),
                401 => new InvalidOperationException("Unauthorized Riot API request."),
                403 => new InvalidOperationException("Invalid or expired Riot API key."),
                404 => new InvalidOperationException("Riot account or summoner was not found."),
                429 => new InvalidOperationException("Riot API rate limit exceeded. Try again in a moment."),
                500 => new InvalidOperationException("Riot API internal server error."),
                503 => new InvalidOperationException("Riot API is temporarily unavailable."),
                _ => new InvalidOperationException($"Riot API error {code}: {responseBody}")
            };
        }

        private static async Task<string?> GetAccountPuuidByRiotIdAsync(string gameName, string tagLine, string apiKey)
        {
            try
            {
                string url =
                    $"https://europe.api.riotgames.com/riot/account/v1/accounts/by-riot-id/" +
                    $"{Uri.EscapeDataString(gameName)}/{Uri.EscapeDataString(tagLine)}";

                Debug.WriteLine($"Making account API request: {url}");

                string response = await SendRiotGetAsync(url, apiKey);

                using var doc = JsonDocument.Parse(response);
                JsonElement root = doc.RootElement;

                if (!root.TryGetProperty("puuid", out JsonElement puuidProp))
                {
                    Debug.WriteLine($"No PUUID found in response for {gameName}#{tagLine}");
                    return null;
                }

                string? puuid = puuidProp.GetString();
                if (string.IsNullOrWhiteSpace(puuid))
                {
                    Debug.WriteLine($"Empty PUUID in response for {gameName}#{tagLine}");
                    return null;
                }

                Debug.WriteLine($"Successfully got PUUID for {gameName}#{tagLine}");
                return puuid;
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine($"Account not found for Riot ID {gameName}#{tagLine}");
                return null;
            }
        }

        private static async Task<RankData?> GetRankInfoAsync(string puuid, string region, string apiKey)
        {
            try
            {
                Debug.WriteLine($"Getting rank info for PUUID in region: {region}");

                // Use ranked entries directly by PUUID instead of resolving summonerId first
                string leagueUrl =
                    $"https://{region}.api.riotgames.com/lol/league/v4/entries/by-puuid/{Uri.EscapeDataString(puuid)}";

                Debug.WriteLine($"Making league API request by PUUID: {leagueUrl}");

                string leagueResponse = await SendRiotGetAsync(leagueUrl, apiKey);

                using var leagueDoc = JsonDocument.Parse(leagueResponse);

                foreach (JsonElement entry in leagueDoc.RootElement.EnumerateArray())
                {
                    if (!entry.TryGetProperty("queueType", out JsonElement queueTypeProp))
                        continue;

                    if (queueTypeProp.GetString() != "RANKED_SOLO_5x5")
                        continue;

                    RankData rankData = new()
                    {
                        Tier = entry.TryGetProperty("tier", out JsonElement tierProp)
                            ? tierProp.GetString() ?? string.Empty
                            : string.Empty,

                        Rank = entry.TryGetProperty("rank", out JsonElement rankProp)
                            ? rankProp.GetString() ?? string.Empty
                            : string.Empty,

                        LeaguePoints = entry.TryGetProperty("leaguePoints", out JsonElement lpProp)
                            ? lpProp.GetInt32()
                            : 0,

                        Wins = entry.TryGetProperty("wins", out JsonElement winsProp)
                            ? winsProp.GetInt32()
                            : 0,

                        Losses = entry.TryGetProperty("losses", out JsonElement lossesProp)
                            ? lossesProp.GetInt32()
                            : 0
                    };

                    Debug.WriteLine($"Extracted rank data: {rankData.Tier} {rankData.Rank} {rankData.LeaguePoints} LP");
                    return rankData;
                }

                Debug.WriteLine("No RANKED_SOLO_5x5 entry found - player is unranked");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting rank info: {ex.Message}");
                throw;
            }
        }

        private static string? TryResolveSummonerId(JsonElement root)
        {
            string[] candidateNames =
            {
                "id",
                "summonerId",
                "summoner_id",
                "encryptedSummonerId"
            };

            foreach (string candidate in candidateNames)
            {
                if (root.TryGetProperty(candidate, out JsonElement value) &&
                    value.ValueKind == JsonValueKind.String)
                {
                    string? parsed = value.GetString();
                    if (!string.IsNullOrWhiteSpace(parsed))
                        return parsed;
                }
            }

            if (root.TryGetProperty("data", out JsonElement data) &&
                data.ValueKind == JsonValueKind.Object)
            {
                foreach (string candidate in candidateNames)
                {
                    if (data.TryGetProperty(candidate, out JsonElement value) &&
                        value.ValueKind == JsonValueKind.String)
                    {
                        string? parsed = value.GetString();
                        if (!string.IsNullOrWhiteSpace(parsed))
                            return parsed;
                    }
                }
            }

            return null;
        }

        private static string FormatRankInfo(RankData? rankInfo)
        {
            if (rankInfo == null)
                return "Unranked";

            try
            {
                string tier = rankInfo.Tier ?? "Unknown";
                string rank = rankInfo.Rank ?? string.Empty;
                int lp = rankInfo.LeaguePoints;
                int wins = rankInfo.Wins;
                int losses = rankInfo.Losses;

                if (string.IsNullOrWhiteSpace(rank) ||
                    tier.Equals("MASTER", StringComparison.OrdinalIgnoreCase) ||
                    tier.Equals("GRANDMASTER", StringComparison.OrdinalIgnoreCase) ||
                    tier.Equals("CHALLENGER", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{tier} ({lp} LP, {wins}W/{losses}L)";
                }

                return $"{tier} {rank} ({lp} LP, {wins}W/{losses}L)";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error formatting rank info: {ex.Message}");
                return "Unranked";
            }
        }

        public static bool LaunchRiotClient(string username, string password, string region, bool rememberMe = true)
        {
            try
            {
                string riotClientPath = FindRiotClientPath();
                if (string.IsNullOrEmpty(riotClientPath))
                {
                    Debug.WriteLine("Riot Client not found");
                    return false;
                }

                ProcessStartInfo startInfo = new()
                {
                    FileName = riotClientPath,
                    Arguments = BuildLaunchArguments(username, password, region, rememberMe),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                Debug.WriteLine($"Riot Client launched with PID: {process?.Id}");
                return process != null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error launching Riot Client: {ex.Message}");
                return false;
            }
        }

        private static string FindRiotClientPath()
        {
            string[] possiblePaths =
            {
                @"C:\Riot Games\Riot Client\RiotClientServices.exe",
                @"C:\Program Files\Riot Games\Riot Client\RiotClientServices.exe",
                @"C:\Program Files (x86)\Riot Games\Riot Client\RiotClientServices.exe",
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Riot Games\Riot Client\RiotClientServices.exe"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    @"Riot Games\Riot Client\RiotClientServices.exe"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    @"Riot Games\Riot Client\RiotClientServices.exe")
            };

            foreach (string path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    Debug.WriteLine($"Found Riot Client at: {path}");
                    return path;
                }
            }

            Debug.WriteLine("Riot Client not found in any common installation location.");
            return string.Empty;
        }

        private static string BuildLaunchArguments(string username, string password, string region, bool rememberMe)
        {
            StringBuilder args = new();
            args.Append("--launch-product=league_of_legends");
            args.Append(" --launch-patchline=live");

            if (!string.IsNullOrEmpty(username))
                args.Append($" --username=\"{username}\"");

            if (!string.IsNullOrEmpty(password))
                args.Append($" --password=\"{password}\"");

            if (!string.IsNullOrEmpty(region))
                args.Append($" --region=\"{region}\"");

            if (rememberMe)
                args.Append(" --remember-me");

            return args.ToString();
        }

        public static bool CloseRiotClient()
        {
            try
            {
                var processes = Process.GetProcessesByName("RiotClientServices")
                    .Concat(Process.GetProcessesByName("LeagueClient"))
                    .Concat(Process.GetProcessesByName("LeagueClientUx"));

                foreach (Process process in processes)
                {
                    try
                    {
                        process.CloseMainWindow();
                        if (!process.WaitForExit(5000))
                            process.Kill();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error closing process {process.ProcessName}: {ex.Message}");
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error closing Riot Client: {ex.Message}");
                return false;
            }
        }

        public static void ShowRiotClient()
        {
            try
            {
                var process = Process.GetProcessesByName("RiotClientUx")
                    .Concat(Process.GetProcessesByName("LeagueClient"))
                    .FirstOrDefault();

                if (process != null)
                {
                    IntPtr hWnd = process.MainWindowHandle;
                    if (hWnd != IntPtr.Zero)
                    {
                        ShowWindow(hWnd, 9);
                        SetForegroundWindow(hWnd);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing Riot Client: {ex.Message}");
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}