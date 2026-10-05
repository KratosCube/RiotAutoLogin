using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RiotAutoLogin.Models;
using Velopack.Sources;

namespace RiotAutoLogin.Services;

// Reuse the checked release list and cap the feed at the version shown in the dialog.
// Quiet releases between the current and target version remain available as delta bases.
internal sealed class ReleaseSnapshotSource : GithubSource
{
    private readonly GithubRelease[] _releases;

    public ReleaseSnapshotSource(string repoUrl, IEnumerable<GitHubRelease> releases, Version current, Version target,
        IFileDownloader? downloader = null)
        : base(repoUrl, null, false, downloader)
    {
        _releases = releases.Where(r => !r.Draft && !r.Prerelease && ReleasePolicy.HasPackage(r) &&
                ReleasePolicy.ParseVersion(r.TagName) is { } v && v > current && v <= target)
            .OrderByDescending(r => ReleasePolicy.ParseVersion(r.TagName))
            .Take(10) // Velopack falls back to the full package if the chain is incomplete.
            .Select(r => new GithubRelease
            {
                Name = r.Name, Prerelease = r.Prerelease, PublishedAt = r.PublishedAt,
                Assets = r.Assets.Select(a => new GithubReleaseAsset
                {
                    Name = a.Name, BrowserDownloadUrl = a.BrowserDownloadUrl, ContentType = a.ContentType
                }).ToArray()
            }).ToArray();
    }

    protected override Task<GithubRelease[]> GetReleases(bool includePrereleases) => Task.FromResult(_releases);
}
