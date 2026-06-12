using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using RiotAutoLogin.Models;
using Newtonsoft.Json;

namespace RiotAutoLogin.Services
{
    public class UpdateService
    {
        private readonly string _owner;
        private readonly string _repo;
        private readonly HttpClient _httpClient;
        private UpdateSettings _settings = new UpdateSettings();
        private readonly string _githubApiUrl;
        private readonly string _settingsFilePath;

        // Events
        public event Action<UpdateProgress>? UpdateProgressChanged;
        public event Action<UpdateInfo>? UpdateAvailable;

        public UpdateService(string githubOwner, string githubRepo)
        {
            _owner = githubOwner;
            _repo = githubRepo;
            _githubApiUrl = $"https://api.github.com/repos/{githubOwner}/{githubRepo}/releases/latest";
            _settingsFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RiotClientAutoLogin", "update_settings.json");

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "RiotAutoLogin-UpdateChecker");

            LoadSettings();
        }

        public async Task<UpdateInfo> CheckForUpdatesAsync()
        {
            var updateInfo = new UpdateInfo
            {
                CurrentVersion = GetCurrentVersion(),
                LastChecked = DateTime.Now
            };

            try
            {
                ReportProgress(new UpdateProgress
                {
                    Status = UpdateStatus.Checking,
                    Message = "Checking for updates..."
                });

                var response = await _httpClient.GetStringAsync(_githubApiUrl);
                var release = JsonConvert.DeserializeObject<GitHubRelease>(response);

                if (release != null && !release.Draft)
                {
                    // Skip prereleases unless enabled
                    if (release.Prerelease && !_settings.IncludePrereleases)
                    {
                        updateInfo.LatestVersion = updateInfo.CurrentVersion;
                        ReportProgress(new UpdateProgress
                        {
                            Status = UpdateStatus.NoUpdateAvailable,
                            Message = "No updates available (prerelease skipped)"
                        });
                        return updateInfo;
                    }

                    // Parse version from tag (remove 'v' prefix if present)
                    var versionString = release.TagName.TrimStart('v');
                    if (Version.TryParse(versionString, out var latestVersion))
                    {
                        updateInfo.LatestVersion = latestVersion;
                        updateInfo.LatestRelease = release;
                        updateInfo.Changelog = release.Body;

                        // Find the executable asset
                        foreach (var asset in release.Assets)
                        {
                            if (asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                updateInfo.DownloadUrl = asset.BrowserDownloadUrl;
                                updateInfo.FileSize = asset.Size;
                                break;
                            }
                        }

                        if (updateInfo.IsUpdateAvailable)
                        {
                            if (string.IsNullOrWhiteSpace(updateInfo.DownloadUrl))
                            {
                                ReportProgress(new UpdateProgress
                                {
                                    Status = UpdateStatus.Error,
                                    Message = "Update found, but no .exe asset is attached to the latest GitHub release."
                                });

                                return updateInfo;
                            }

                            ReportProgress(new UpdateProgress
                            {
                                Status = UpdateStatus.UpdateAvailable,
                                Message = $"Update available: v{latestVersion}"
                            });

                            UpdateAvailable?.Invoke(updateInfo);
                        }
                        else
                        {
                            ReportProgress(new UpdateProgress
                            {
                                Status = UpdateStatus.NoUpdateAvailable,
                                Message = "You have the latest version"
                            });
                        }
                    }
                }

                _settings.LastCheckTime = DateTime.Now;
                SaveSettings();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking for updates: {ex.Message}");
                ReportProgress(new UpdateProgress
                {
                    Status = UpdateStatus.Error,
                    Message = $"Update check failed: {ex.Message}",
                    Error = ex
                });
            }

            return updateInfo;
        }

        public async Task<bool> DownloadUpdateAsync(UpdateInfo updateInfo, string downloadPath)
        {
            if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
                return false;

            try
            {
                ReportProgress(new UpdateProgress
                {
                    Status = UpdateStatus.Downloading,
                    Message = "Downloading update...",
                    TotalBytes = updateInfo.FileSize ?? 0
                });

                string? downloadDirectory = Path.GetDirectoryName(downloadPath);
                if (!string.IsNullOrWhiteSpace(downloadDirectory))
                    Directory.CreateDirectory(downloadDirectory);

                string tempDownloadPath = downloadPath + ".download";
                if (File.Exists(tempDownloadPath))
                    File.Delete(tempDownloadPath);

                using var response = await _httpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var downloadedBytes = 0L;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using (var fileStream = new FileStream(tempDownloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[8192];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        downloadedBytes += bytesRead;

                        var progressPercentage = totalBytes > 0 ? (int)((downloadedBytes * 100) / totalBytes) : 0;

                        ReportProgress(new UpdateProgress
                        {
                            Status = UpdateStatus.Downloading,
                            Message = $"Downloading... {progressPercentage}%",
                            ProgressPercentage = progressPercentage,
                            BytesDownloaded = downloadedBytes,
                            TotalBytes = totalBytes
                        });
                    }
                }

                if (File.Exists(downloadPath))
                    File.Delete(downloadPath);

                File.Move(tempDownloadPath, downloadPath);

                ReportProgress(new UpdateProgress
                {
                    Status = UpdateStatus.Downloaded,
                    Message = "Download completed",
                    ProgressPercentage = 100
                });

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error downloading update: {ex.Message}");
                ReportProgress(new UpdateProgress
                {
                    Status = UpdateStatus.Error,
                    Message = $"Download failed: {ex.Message}",
                    Error = ex
                });
                return false;
            }
        }

        public bool InstallUpdate(string updateFilePath, bool restartApp = true)
        {
            try
            {
                ReportProgress(new UpdateProgress
                {
                    Status = UpdateStatus.Installing,
                    Message = "Installing update..."
                });

                if (string.IsNullOrWhiteSpace(updateFilePath) || !File.Exists(updateFilePath))
                {
                    ReportProgress(new UpdateProgress
                    {
                        Status = UpdateStatus.Error,
                        Message = "Installation failed: downloaded update file was not found."
                    });
                    return false;
                }

                var currentProcess = Process.GetCurrentProcess();
                var currentExePath = currentProcess.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(currentExePath) || !File.Exists(currentExePath))
                {
                    ReportProgress(new UpdateProgress
                    {
                        Status = UpdateStatus.Error,
                        Message = "Installation failed: could not locate the running application executable."
                    });
                    return false;
                }

                string? currentExeDirectory = Path.GetDirectoryName(currentExePath);
                if (string.IsNullOrWhiteSpace(currentExeDirectory))
                {
                    ReportProgress(new UpdateProgress
                    {
                        Status = UpdateStatus.Error,
                        Message = "Installation failed: could not locate the application directory."
                    });
                    return false;
                }

                var updaterDirectory = Path.Combine(Path.GetTempPath(), "RiotAutoLogin", "Updater");
                Directory.CreateDirectory(updaterDirectory);

                var batchPath = Path.Combine(updaterDirectory, $"RiotAutoLoginUpdate-{Guid.NewGuid():N}.bat");
                var logPath = Path.Combine(updaterDirectory, "RiotAutoLoginUpdate.log");
                var restartCommand = restartApp ? $"start \"\" /D \"{currentExeDirectory}\" \"{currentExePath}\"" : "rem Restart disabled";

                var batchLines = new[]
                {
                    "@echo off",
                    "setlocal",
                    $"set \"SOURCE={updateFilePath}\"",
                    $"set \"TARGET={currentExePath}\"",
                    $"set \"TARGET_DIR={currentExeDirectory}\"",
                    $"set \"APP_PID={currentProcess.Id}\"",
                    $"set \"LOG={logPath}\"",
                    string.Empty,
                    "echo [%date% %time%] RiotAutoLogin updater started. > \"%LOG%\"",
                    "echo Waiting for RiotAutoLogin process %APP_PID% to exit... >> \"%LOG%\"",
                    string.Empty,
                    "for /L %%i in (1,1,60) do (",
                    "    tasklist /FI \"PID eq %APP_PID%\" 2>NUL | find \"%APP_PID%\" >NUL",
                    "    if errorlevel 1 goto replace",
                    "    timeout /t 1 /nobreak >NUL",
                    ")",
                    string.Empty,
                    "echo Process did not exit in time. Trying replacement anyway. >> \"%LOG%\"",
                    string.Empty,
                    ":replace",
                    "if not exist \"%SOURCE%\" (",
                    "    echo Update file not found: %SOURCE% >> \"%LOG%\"",
                    "    goto end",
                    ")",
                    string.Empty,
                    "copy /Y \"%SOURCE%\" \"%TARGET%\" >> \"%LOG%\" 2>&1",
                    "if errorlevel 1 (",
                    "    echo Failed to copy update to target. >> \"%LOG%\"",
                    "    goto end",
                    ")",
                    string.Empty,
                    "del \"%SOURCE%\" >NUL 2>&1",
                    "echo Update installed successfully. >> \"%LOG%\"",
                    restartCommand,
                    string.Empty,
                    ":end",
                    "endlocal",
                    "del \"%~f0\" >NUL 2>&1"
                };

                var batchContent = string.Join(Environment.NewLine, batchLines) + Environment.NewLine;
                File.WriteAllText(batchPath, batchContent);

                var processInfo = new ProcessStartInfo
                {
                    FileName = batchPath,
                    WorkingDirectory = updaterDirectory,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = true,
                    CreateNoWindow = true
                };

                Process.Start(processInfo);

                ReportProgress(new UpdateProgress
                {
                    Status = UpdateStatus.Installed,
                    Message = "Updater started. The application will close and restart."
                });

                Application.Current.Shutdown();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error installing update: {ex.Message}");
                ReportProgress(new UpdateProgress
                {
                    Status = UpdateStatus.Error,
                    Message = $"Installation failed: {ex.Message}",
                    Error = ex
                });
                return false;
            }
        }

        public bool ShouldCheckForUpdates()
        {
            if (!_settings.AutoCheckEnabled)
                return false;

            var timeSinceLastCheck = DateTime.Now - _settings.LastCheckTime;
            return timeSinceLastCheck.TotalHours >= _settings.CheckIntervalHours;
        }

        public UpdateSettings GetSettings() => _settings;

        public void UpdateSettings(UpdateSettings newSettings)
        {
            _settings = newSettings;
            SaveSettings();
        }

        private Version GetCurrentVersion()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return version ?? new Version("1.0.0");
            }
            catch
            {
                return new Version("1.0.0");
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    _settings = JsonConvert.DeserializeObject<UpdateSettings>(json) ?? new UpdateSettings();
                }
                else
                {
                    _settings = new UpdateSettings();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading update settings: {ex.Message}");
                _settings = new UpdateSettings();
            }
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
                var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving update settings: {ex.Message}");
            }
        }

        private void ReportProgress(UpdateProgress progress)
        {
            UpdateProgressChanged?.Invoke(progress);
        }
    }
}
