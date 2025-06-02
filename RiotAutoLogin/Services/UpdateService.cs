using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
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
                Console.WriteLine($"Error checking for updates: {ex.Message}");
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

                using var response = await _httpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var downloadedBytes = 0L;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None);

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
                Console.WriteLine($"Error downloading update: {ex.Message}");
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

                // Create a batch file to replace the executable and restart the app
                var currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(currentExePath))
                    return false;

                var batchPath = Path.Combine(Path.GetTempPath(), "RiotAutoLoginUpdate.bat");
                var batchContent = $@"
@echo off
timeout /t 2 /nobreak > nul
copy ""{updateFilePath}"" ""{currentExePath}"" /y
del ""{updateFilePath}""
{(restartApp ? $"start \"\" \"{currentExePath}\"" : "")}
del ""{batchPath}""
";

                File.WriteAllText(batchPath, batchContent);

                // Start the batch file and exit current process
                var processInfo = new ProcessStartInfo
                {
                    FileName = batchPath,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                };

                Process.Start(processInfo);

                ReportProgress(new UpdateProgress 
                { 
                    Status = UpdateStatus.Installed, 
                    Message = "Update installed successfully" 
                });

                // Exit current application
                if (restartApp)
                {
                    Application.Current.Shutdown();
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error installing update: {ex.Message}");
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
                Console.WriteLine($"Error loading update settings: {ex.Message}");
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
                Console.WriteLine($"Error saving update settings: {ex.Message}");
            }
        }

        private void ReportProgress(UpdateProgress progress)
        {
            UpdateProgressChanged?.Invoke(progress);
        }
    }
} 