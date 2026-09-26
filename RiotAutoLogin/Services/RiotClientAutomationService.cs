using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using RiotAutoLogin.Models;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services
{
    using Application = FlaUI.Core.Application;

    public static class RiotClientAutomationService
    {
        private static readonly HttpClient _httpClient = new();
        private static readonly ConcurrentDictionary<string, string> PuuidCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim LoginGate = new(1, 1);
        private static readonly string[] LoginProcessNames =
        {
            "RiotClientUx",
            "Riot Client",
            "RiotClientServices"
        };

        private static readonly TimeSpan LoginFormTimeout = TimeSpan.FromSeconds(60);
        private const int SwShow = 5;
        private const int SwRestore = 9;

        private delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int maximumCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr windowHandle, StringBuilder className, int maximumCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        static RiotClientAutomationService()
        {
            _httpClient.DefaultRequestHeaders.Add(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public static async Task<RiotLoginResult> LaunchAndLoginAsync(
            string username,
            string password,
            IProgress<RiotLoginProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                return RiotLoginResult.Failed("The selected account does not contain valid login credentials.");

            if (!await LoginGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                return RiotLoginResult.Failed("Another Riot Client login is already in progress.");

            try
            {
                progress?.Report(new RiotLoginProgress(RiotLoginStage.Preparing, "Preparing Riot Client…"));

                bool restoredExistingWindow = TryRestoreExistingRiotClientWindow();
                if (!restoredExistingWindow)
                {
                    string riotClientPath = FindRiotClientPath();
                    if (string.IsNullOrEmpty(riotClientPath))
                    {
                        return RiotLoginResult.Failed(
                            "Riot Client was not found. Install it in a standard location or start it once manually so RiotClientInstalls.json can be discovered.");
                    }

                    progress?.Report(new RiotLoginProgress(RiotLoginStage.LaunchingClient, "Starting Riot Client…"));
                    StartRiotClient(riotClientPath);
                }
                else
                {
                    progress?.Report(new RiotLoginProgress(
                        RiotLoginStage.LaunchingClient,
                        "Restoring Riot Client from the system tray…"));
                }

                progress?.Report(new RiotLoginProgress(
                    RiotLoginStage.WaitingForClient,
                    "Waiting for the Riot login screen…"));

                Stopwatch timer = Stopwatch.StartNew();
                string lastDetail = "Riot Client is still starting.";
                int nextProgressUpdateSecond = 5;

                while (timer.Elapsed < LoginFormTimeout)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    LoginAttempt attempt = await Task.Run(
                        () => TrySubmitCredentials(username, password),
                        cancellationToken).ConfigureAwait(false);

                    if (attempt.Submitted)
                    {
                        progress?.Report(new RiotLoginProgress(RiotLoginStage.Submitting, "Credentials submitted…"));
                        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                        progress?.Report(new RiotLoginProgress(RiotLoginStage.Completed, "Login sent to Riot Client."));
                        return RiotLoginResult.Succeeded("Riot Client is signing in. Complete any verification prompt in the client.");
                    }

                    lastDetail = attempt.Detail;
                    if (timer.Elapsed.TotalSeconds >= nextProgressUpdateSecond)
                    {
                        progress?.Report(new RiotLoginProgress(
                            RiotLoginStage.WaitingForClient,
                            $"Waiting for Riot Client… {Math.Ceiling(timer.Elapsed.TotalSeconds):0}s"));
                        nextProgressUpdateSecond += 5;
                    }

                    await Task.Delay(attempt.WindowFound ? 250 : 500, cancellationToken).ConfigureAwait(false);
                }

                return RiotLoginResult.Failed(
                    $"The Riot login form did not become ready within {LoginFormTimeout.TotalSeconds:0} seconds. {lastDetail}");
            }
            catch (OperationCanceledException)
            {
                return RiotLoginResult.Failed("Login was cancelled.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Riot login failed: {ex}");
                return RiotLoginResult.Failed($"Could not start the Riot login: {ex.Message}");
            }
            finally
            {
                LoginGate.Release();
            }
        }

        private static LoginAttempt TrySubmitCredentials(string username, string password)
        {
            bool windowFound = false;
            IReadOnlyList<Process> processes = GetRiotProcesses();

            try
            {
                foreach (Process process in processes)
                {
                    try
                    {
                        process.Refresh();
                        if (process.HasExited ||
                            process.ProcessName.Equals("RiotClientServices", StringComparison.OrdinalIgnoreCase))
                            continue;

                        IntPtr riotWindowHandle = FindBestTopLevelWindow(process);
                        if (riotWindowHandle == IntPtr.Zero)
                            continue;

                        windowFound = true;
                        RestoreWindow(riotWindowHandle);
                        using var automation = new UIA3Automation();
                        using var app = Application.Attach(process);
                        var mainWindow = app.GetMainWindow(automation);
                        if (mainWindow == null)
                            continue;

                        var riotClientPane = mainWindow.FindFirstDescendant(
                            cf => cf.ByName("Riot Client").And(cf.ByControlType(ControlType.Pane)));
                        var parentElement = riotClientPane ?? mainWindow;

                        var usernameEdit = parentElement.FindFirstDescendant(
                            cf => cf.ByAutomationId("username").And(cf.ByControlType(ControlType.Edit)));
                        var passwordEdit = parentElement.FindFirstDescendant(
                            cf => cf.ByAutomationId("password").And(cf.ByControlType(ControlType.Edit)));

                        if (usernameEdit == null || passwordEdit == null)
                            continue;

                        mainWindow.Focus();
                        usernameEdit.Focus();
                        usernameEdit.Patterns.Value.Pattern.SetValue(string.Empty);
                        usernameEdit.Patterns.Value.Pattern.SetValue(username);

                        passwordEdit.Focus();
                        passwordEdit.Patterns.Value.Pattern.SetValue(string.Empty);
                        passwordEdit.Patterns.Value.Pattern.SetValue(password);
                        passwordEdit.Focus();

                        FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ENTER);
                        Debug.WriteLine($"Login credentials submitted through {process.ProcessName} ({process.Id}).");
                        return new LoginAttempt(true, true, "Credentials were submitted.");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Riot UI process {process.Id} is not ready yet: {ex.Message}");
                    }
                }
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }

            return windowFound
                ? new LoginAttempt(false, true, "The Riot window is visible, but its login fields are not ready yet.")
                : new LoginAttempt(false, false, "No Riot Client window is visible yet.");
        }

        private static void StartRiotClient(string riotClientPath)
        {
            Process? launcher = Process.Start(new ProcessStartInfo
            {
                FileName = riotClientPath,
                WorkingDirectory = Path.GetDirectoryName(riotClientPath) ?? string.Empty,
                Arguments = "--launch-product=league_of_legends --launch-patchline=live",
                UseShellExecute = false
            });

            launcher?.Dispose();
            Debug.WriteLine($"Riot Client launched from: {riotClientPath}");
        }

        private static bool TryRestoreExistingRiotClientWindow()
        {
            IReadOnlyList<Process> processes = GetRiotProcesses();
            try
            {
                foreach (Process process in processes)
                {
                    try
                    {
                        if (process.HasExited ||
                            process.ProcessName.Equals("RiotClientServices", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        IntPtr windowHandle = FindBestTopLevelWindow(process);
                        if (windowHandle == IntPtr.Zero)
                            continue;

                        RestoreWindow(windowHandle);
                        Debug.WriteLine($"Restored Riot Client window for {process.ProcessName} ({process.Id}).");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Could not restore Riot process {process.Id}: {ex.Message}");
                    }
                }
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }

            return false;
        }

        private static IntPtr FindBestTopLevelWindow(Process process)
        {
            IntPtr bestHandle = IntPtr.Zero;
            int bestScore = int.MinValue;

            try
            {
                process.Refresh();
                IntPtr mainWindowHandle = process.MainWindowHandle;
                uint targetProcessId = (uint)process.Id;

                EnumWindows((windowHandle, _) =>
                {
                    GetWindowThreadProcessId(windowHandle, out uint windowProcessId);
                    if (windowProcessId != targetProcessId)
                        return true;

                    if (!GetWindowRect(windowHandle, out NativeRect rectangle))
                        return true;

                    int width = rectangle.Right - rectangle.Left;
                    int height = rectangle.Bottom - rectangle.Top;
                    if (width < 160 || height < 120)
                        return true;

                    var titleBuilder = new StringBuilder(512);
                    GetWindowText(windowHandle, titleBuilder, titleBuilder.Capacity);
                    string title = titleBuilder.ToString();

                    var classBuilder = new StringBuilder(256);
                    GetClassName(windowHandle, classBuilder, classBuilder.Capacity);
                    string className = classBuilder.ToString();

                    bool looksLikeRiotWindow =
                        title.Contains("Riot", StringComparison.OrdinalIgnoreCase) ||
                        title.Contains("League of Legends", StringComparison.OrdinalIgnoreCase) ||
                        className.Contains("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase);
                    if (!looksLikeRiotWindow && windowHandle != mainWindowHandle)
                        return true;

                    int score = 0;
                    if (windowHandle == mainWindowHandle)
                        score += 1000;
                    if (title.Contains("Riot Client", StringComparison.OrdinalIgnoreCase))
                        score += 500;
                    if (className.Contains("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase))
                        score += 250;
                    if (IsWindowVisible(windowHandle))
                        score += 100;
                    score += Math.Min((width * height) / 10000, 100);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestHandle = windowHandle;
                    }

                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not enumerate Riot Client windows: {ex.Message}");
            }

            return bestHandle;
        }

        private static void RestoreWindow(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
                return;

            if (IsIconic(windowHandle))
                ShowWindowAsync(windowHandle, SwRestore);
            else if (!IsWindowVisible(windowHandle))
                ShowWindowAsync(windowHandle, SwShow);

            SetForegroundWindow(windowHandle);
        }

        private static IReadOnlyList<Process> GetRiotProcesses()
        {
            var seenProcessIds = new HashSet<int>();
            var result = new List<Process>();
            foreach (string processName in LoginProcessNames)
            {
                Process[] processes;
                try
                {
                    processes = Process.GetProcessesByName(processName);
                }
                catch
                {
                    continue;
                }

                foreach (Process process in processes)
                {
                    if (seenProcessIds.Add(process.Id))
                        result.Add(process);
                    else
                        process.Dispose();
                }
            }

            return result;
        }

        private sealed record LoginAttempt(bool Submitted, bool WindowFound, string Detail);

        public static async Task<Dictionary<RankedQueue, RankData>> GetRanksAsync(string gameName, string tagLine, string region)
        {
            if (string.IsNullOrWhiteSpace(gameName) || string.IsNullOrWhiteSpace(tagLine) || string.IsNullOrWhiteSpace(region))
                throw new InvalidOperationException("A Riot ID and region are required.");

            string apiKey = await GetApiKeyAsync();
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Set your Riot API key in Settings to refresh ranks.");

            string identity = $"{region}/{gameName}#{tagLine}";
            if (!PuuidCache.TryGetValue(identity, out string? puuid))
            {
                puuid = await GetAccountPuuidByRiotIdAsync(gameName, tagLine, apiKey);
                if (string.IsNullOrWhiteSpace(puuid)) throw new InvalidOperationException("Riot account not found.");
                PuuidCache[identity] = puuid;
            }

            string url = $"https://{region}.api.riotgames.com/lol/league/v4/entries/by-puuid/{Uri.EscapeDataString(puuid)}";
            string response = await SendRiotGetAsync(url, apiKey);
            using var doc = JsonDocument.Parse(response);
            return RankedQueues.ParseRanks(doc.RootElement);
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

        private static string FindRiotClientPath()
        {
            string installMetadataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Riot Games",
                "RiotClientInstalls.json");

            string? metadataPath = TryFindRiotClientPathInMetadata(installMetadataPath);
            if (!string.IsNullOrEmpty(metadataPath))
                return metadataPath;

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

        private static string? TryFindRiotClientPathInMetadata(string metadataPath)
        {
            try
            {
                if (!File.Exists(metadataPath))
                    return null;

                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(metadataPath));
                return FindRiotClientExecutable(document.RootElement);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not read Riot install metadata: {ex.Message}");
                return null;
            }
        }

        private static string? FindRiotClientExecutable(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                string? candidate = element.GetString();
                if (!string.IsNullOrWhiteSpace(candidate) &&
                    candidate.EndsWith("RiotClientServices.exe", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(candidate))
                    return candidate;
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string? found = FindRiotClientExecutable(property.Value);
                    if (!string.IsNullOrEmpty(found))
                        return found;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    string? found = FindRiotClientExecutable(item);
                    if (!string.IsNullOrEmpty(found))
                        return found;
                }
            }

            return null;
        }
    }
}
