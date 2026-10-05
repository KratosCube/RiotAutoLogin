using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using RiotAutoLogin.Models;
using Newtonsoft.Json;

namespace RiotAutoLogin.Services
{
    public sealed class UpdateService : IDisposable
    {
        private readonly HttpClient _httpClient = new() { Timeout = Timeout.InfiniteTimeSpan };
        private readonly SemaphoreSlim _checkGate = new(1, 1);
        private readonly ConditionalWeakTable<UpdateInfo, PackageSession> _packages = new();
        private readonly string _repoUrl;
        private readonly string _githubApiUrl;
        private readonly string _settingsFilePath;
        private UpdateSettings _settings = new();
        public bool SupportsDeltaUpdates { get; }
        public bool NotificationsEnabled => _settings.NotificationsEnabled;

        public event Action<UpdateProgress>? UpdateProgressChanged;
        public event Action<UpdateInfo>? UpdateAvailable;

        private sealed record PackageSession(Velopack.UpdateManager Manager, Velopack.UpdateInfo Plan)
        {
            public bool Downloaded { get; set; }
        }

        public UpdateService(string githubOwner, string githubRepo)
        {
            _repoUrl = $"https://github.com/{githubOwner}/{githubRepo}";
            _githubApiUrl = $"https://api.github.com/repos/{githubOwner}/{githubRepo}/releases?per_page=100";
            _settingsFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RiotClientAutoLogin", "update_settings.json");
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("RiotAutoLogin-UpdateChecker");
            LoadSettings();
            SupportsDeltaUpdates = new Velopack.UpdateManager(
                new Velopack.Sources.GithubSource(_repoUrl, null, false)).IsInstalled;
        }

        public async Task<UpdateInfo> CheckForUpdatesAsync(bool manual = true, bool allowMigration = false,
            CancellationToken cancellationToken = default)
        {
            var info = new UpdateInfo { CurrentVersion = GetCurrentVersion(), LastChecked = DateTime.Now };
            if (!manual && !ShouldCheckForUpdates()) return info;
            if (!await _checkGate.WaitAsync(0, cancellationToken))
            {
                info.ErrorMessage = "Another update check is already running.";
                return info;
            }
            try
            {
                ReportProgress(new() { Status = UpdateStatus.Checking, Message = "Checking for updates..." });
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                var json = await _httpClient.GetStringAsync(_githubApiUrl, timeout.Token);
                var releases = JsonConvert.DeserializeObject<List<GitHubRelease>>(json)
                    ?? throw new InvalidDataException("GitHub returned an invalid release list.");
                var release = ReleasePolicy.Select(releases, info.CurrentVersion, manual, SupportsDeltaUpdates,
                    allowMigration && !SupportsDeltaUpdates);
                if (release == null)
                {
                    _settings.LastCheckTime = DateTime.Now;
                    SaveSettings();
                    ReportProgress(new() { Status = UpdateStatus.NoUpdateAvailable,
                        Message = manual ? "You have the latest available version." : "No announced updates available." });
                    return info;
                }

                info.LatestVersion = ReleasePolicy.ParseVersion(release.TagName)!;
                info.LatestRelease = release;
                info.Changelog = ReleasePolicy.CleanNotes(release.Body);
                if (SupportsDeltaUpdates)
                {
                    var manager = new Velopack.UpdateManager(new ReleaseSnapshotSource(
                        _repoUrl, releases, info.CurrentVersion, info.LatestVersion));
                    var plan = await manager.CheckForUpdatesAsync().WaitAsync(timeout.Token);
                    if (plan == null || plan.IsDowngrade ||
                        ReleasePolicy.ParseVersion(plan.TargetFullRelease.Version.ToString()) != info.LatestVersion)
                        throw new InvalidDataException("The update packages are not ready yet. Please try again later.");
                    _packages.Add(info, new PackageSession(manager, plan));
                    info.Delivery = UpdateDelivery.Package;
                    info.UsesDelta = plan.DeltasToTarget.Length is > 0 and <= 10 &&
                        plan.DeltasToTarget.Sum(a => a.Size) <= plan.TargetFullRelease.Size;
                    info.FullDownloadSize = plan.TargetFullRelease.Size;
                    info.FileSize = info.UsesDelta ? plan.DeltasToTarget.Sum(a => a.Size) : plan.TargetFullRelease.Size;
                }
                else
                {
                    // A normal update keeps the executable at its existing path. Only the
                    // explicit "Enable smaller updates" action migrates to the installer.
                    var asset = allowMigration ? ReleasePolicy.FindInstaller(release)! : ReleasePolicy.FindStandaloneAsset(release)!;
                    info.Delivery = allowMigration ? UpdateDelivery.Installer : UpdateDelivery.Standalone;
                    info.DownloadUrl = asset.BrowserDownloadUrl;
                    info.DownloadDigest = asset.Digest;
                    info.FileSize = asset.Size;
                    if (info.Delivery == UpdateDelivery.Installer && string.IsNullOrWhiteSpace(asset.Digest))
                        throw new InvalidDataException("The installer checksum is not available yet. Please try again later.");
                }

                _settings.LastCheckTime = DateTime.Now;
                SaveSettings();
                if (manual || (_settings.NotificationsEnabled &&
                    _settings.LastNotifiedVersion != info.LatestVersion.ToString()))
                {
                    ReportProgress(new() { Status = UpdateStatus.UpdateAvailable,
                        Message = $"Update available: v{info.LatestVersion.ToString(3)}" });
                    UpdateAvailable?.Invoke(info);
                    if (!manual)
                    {
                        _settings.LastNotifiedVersion = info.LatestVersion.ToString();
                        SaveSettings();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                info.ErrorMessage = $"Update check failed: {ex.Message}";
                ReportProgress(new() { Status = UpdateStatus.Error, Message = info.ErrorMessage, Error = ex });
            }
            finally { _checkGate.Release(); }
            return info;
        }

        public async Task<bool> DownloadUpdateAsync(UpdateInfo info, string downloadPath,
            CancellationToken cancellationToken = default)
        {
            string partial = downloadPath + ".download";
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMinutes(15));
                ReportProgress(new() { Status = UpdateStatus.Downloading, Message = "Downloading update..." });
                if (info.Delivery == UpdateDelivery.Package)
                {
                    if (!_packages.TryGetValue(info, out var session))
                        throw new InvalidOperationException("Please check for updates again.");
                    await session.Manager.DownloadUpdatesAsync(session.Plan, percent => ReportProgress(new()
                    {
                        Status = UpdateStatus.Downloading, ProgressPercentage = percent,
                        Message = $"Downloading and preparing update... {percent}%"
                    }), timeout.Token);
                    session.Downloaded = true;
                }
                else
                {
                    if (!Uri.TryCreate(info.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                        throw new InvalidDataException("The update download URL is invalid.");
                    Directory.CreateDirectory(Path.GetDirectoryName(downloadPath)!);
                    using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    response.EnsureSuccessStatusCode();
                    long total = response.Content.Headers.ContentLength ?? info.FileSize ?? 0;
                    long downloaded = 0;
                    using var content = await response.Content.ReadAsStreamAsync(timeout.Token);
                    await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None,
                        81920, useAsync: true))
                    {
                        var buffer = new byte[81920];
                        int count, lastPercent = -1;
                        while ((count = await content.ReadAsync(buffer, timeout.Token)) > 0)
                        {
                            await file.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                            downloaded += count;
                            if (info.FileSize is > 0 && downloaded > info.FileSize)
                                throw new InvalidDataException("The update is larger than the published asset.");
                            int percent = total > 0 ? (int)(downloaded * 100 / total) : 0;
                            if (percent == lastPercent) continue;
                            lastPercent = percent;
                            ReportProgress(new() { Status = UpdateStatus.Downloading, ProgressPercentage = percent,
                                BytesDownloaded = downloaded, TotalBytes = total, Message = $"Downloading... {percent}%" });
                        }
                    }
                    await UpdateDownloadVerifier.VerifyAsync(partial, info.FileSize, info.DownloadDigest, timeout.Token);
                    File.Move(partial, downloadPath, overwrite: true);
                }
                ReportProgress(new() { Status = UpdateStatus.Downloaded, ProgressPercentage = 100,
                    Message = "Download complete. Preparing installation..." });
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                ReportProgress(new() { Status = UpdateStatus.Error, Message = $"Download failed: {ex.Message}", Error = ex });
                return false;
            }
            finally
            {
                try { if (File.Exists(partial)) File.Delete(partial); } catch (IOException) { }
            }
        }

        public async Task<bool> InstallUpdateAsync(UpdateInfo info, string downloadPath)
        {
            try
            {
                if (info.Delivery == UpdateDelivery.Package)
                {
                    if (!_packages.TryGetValue(info, out var session) || !session.Downloaded)
                        throw new InvalidOperationException("Download the update before installing it.");
                    // Let WPF save tracking history and close monitors before replacing files.
                    session.Manager.WaitExitThenApplyUpdates(session.Plan.TargetFullRelease, restart: true);
                }
                else
                {
                    await UpdateDownloadVerifier.VerifyAsync(downloadPath, info.FileSize, info.DownloadDigest, CancellationToken.None);
                    if (info.Delivery == UpdateDelivery.Standalone)
                    {
                        using var runningProcess = Process.GetCurrentProcess();
                        var target = runningProcess.MainModule?.FileName
                            ?? throw new IOException("Could not find the running application executable.");
                        using var updater = await StandaloneUpdater.LaunchAsync(downloadPath, target,
                            runningProcess.Id, runningProcess.StartTime.ToUniversalTime().Ticks);
                        // A helper that fails to start must not make us close the working app.
                        await Task.Delay(300);
                        if (updater.HasExited)
                            throw new IOException($"The update helper exited before the application closed ({updater.ExitCode}).");
                    }
                    else
                    {
                        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "RiotClientAutoLogin", "Updates", "Setup", Guid.NewGuid().ToString("N"));
                        var installer = await Task.Run(() => UpdateDownloadVerifier.ExtractInstaller(downloadPath, directory));
                        if (Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true }) == null)
                            throw new IOException("Could not start the installer.");
                        try { File.Delete(downloadPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    }
                }
                Application.Current.Shutdown();
                return true;
            }
            catch (Exception ex)
            {
                ReportProgress(new() { Status = UpdateStatus.Error, Message = $"Installation failed: {ex.Message}", Error = ex });
                return false;
            }
        }

        public bool ShouldCheckForUpdates() => _settings.AutoCheckEnabled && _settings.NotificationsEnabled &&
            (DateTime.Now - _settings.LastCheckTime).TotalHours >= Math.Max(1, _settings.CheckIntervalHours);

        public void SetNotificationsEnabled(bool enabled)
        {
            if (_settings.NotificationsEnabled == enabled && _settings.AutoCheckEnabled == enabled) return;
            _settings.NotificationsEnabled = enabled;
            _settings.AutoCheckEnabled = enabled;
            if (enabled) _settings.LastCheckTime = DateTime.MinValue;
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


        private void ReportProgress(UpdateProgress progress) => UpdateProgressChanged?.Invoke(progress);

        public void Dispose() => _httpClient.Dispose();
    }
}
