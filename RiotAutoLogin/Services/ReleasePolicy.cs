using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RiotAutoLogin.Models;

namespace RiotAutoLogin.Services;

public static class ReleasePolicy
{
    public const string InstallerAssetName = "RiotAutoLogin-Setup.zip";
    public const string FeedAssetName = "releases.win.json";
    private static readonly Regex NotificationMarker = new(
        @"<!--\s*riotautologin:notify\s*=\s*(true|false)\s*-->",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) ||
            !Version.TryParse(tag.Trim().TrimStart('v', 'V'), out var version)) return null;
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    }

    public static bool ShouldAnnounce(GitHubRelease release) =>
        !NotificationMarker.Matches(release.Body ?? string.Empty)
            .Any(match => match.Groups[1].Value.Equals("false", StringComparison.OrdinalIgnoreCase));

    public static string CleanNotes(string? body) => NotificationMarker.Replace(body ?? string.Empty, "").Trim();

    public static GitHubAsset? FindStandaloneAsset(GitHubRelease release) => release.Assets?.FirstOrDefault(asset =>
        asset.Name.Equals($"RiotAutoLogin-{release.TagName}-win-x64.exe", StringComparison.OrdinalIgnoreCase));

    public static GitHubAsset? FindInstaller(GitHubRelease release) => release.Assets?.FirstOrDefault(asset =>
        asset.Name.Equals(InstallerAssetName, StringComparison.OrdinalIgnoreCase));

    public static bool HasPackage(GitHubRelease release) => release.Assets?.Any(a => a.Name == FeedAssetName) == true &&
        release.Assets.Any(a => a.Name.EndsWith("-full.nupkg", StringComparison.OrdinalIgnoreCase));

    public static GitHubRelease? Select(IEnumerable<GitHubRelease> releases, Version current,
        bool manual, bool packaged, bool allowMigration = false) => releases
        .Where(r => !r.Draft && !r.Prerelease && ParseVersion(r.TagName) is { } v &&
            (v > current || (allowMigration && v == current && FindInstaller(r) != null)))
        .Where(r => manual || ShouldAnnounce(r))
        .Where(r => packaged ? HasPackage(r) : FindInstaller(r) != null || FindStandaloneAsset(r) != null)
        .OrderByDescending(r => ParseVersion(r.TagName))
        .FirstOrDefault();
}
