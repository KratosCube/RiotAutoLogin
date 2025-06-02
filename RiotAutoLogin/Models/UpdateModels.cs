using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace RiotAutoLogin.Models
{
    public class UpdateInfo
    {
        public Version CurrentVersion { get; set; } = new Version("1.0.0");
        public Version? LatestVersion { get; set; }
        public bool IsUpdateAvailable => LatestVersion != null && LatestVersion > CurrentVersion;
        public GitHubRelease? LatestRelease { get; set; }
        public DateTime LastChecked { get; set; }
        public string? DownloadUrl { get; set; }
        public long? FileSize { get; set; }
        public string? Changelog { get; set; }
    }

    public class GitHubRelease
    {
        [JsonProperty("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("body")]
        public string Body { get; set; } = string.Empty;

        [JsonProperty("published_at")]
        public DateTime PublishedAt { get; set; }

        [JsonProperty("draft")]
        public bool Draft { get; set; }

        [JsonProperty("prerelease")]
        public bool Prerelease { get; set; }

        [JsonProperty("assets")]
        public List<GitHubAsset> Assets { get; set; } = new List<GitHubAsset>();

        [JsonProperty("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;
    }

    public class GitHubAsset
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("content_type")]
        public string ContentType { get; set; } = string.Empty;
    }

    public class UpdateSettings
    {
        public bool AutoCheckEnabled { get; set; } = true;
        public bool AutoDownloadEnabled { get; set; } = false;
        public bool AutoInstallEnabled { get; set; } = false;
        public bool IncludePrereleases { get; set; } = false;
        public int CheckIntervalHours { get; set; } = 24; // Check daily by default
        public DateTime LastCheckTime { get; set; } = DateTime.MinValue;
        public bool NotificationsEnabled { get; set; } = true;
    }

    public enum UpdateStatus
    {
        Checking,
        UpdateAvailable,
        NoUpdateAvailable,
        Downloading,
        Downloaded,
        Installing,
        Installed,
        Error,
        Cancelled
    }

    public class UpdateProgress
    {
        public UpdateStatus Status { get; set; }
        public string Message { get; set; } = string.Empty;
        public int ProgressPercentage { get; set; }
        public long BytesDownloaded { get; set; }
        public long TotalBytes { get; set; }
        public Exception? Error { get; set; }
    }
} 