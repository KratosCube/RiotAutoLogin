using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using RiotAutoLogin.Models;
using RiotAutoLogin.Services;
using Velopack.Logging;
using Velopack.Sources;

internal static class UpdateChecks
{
    public static async Task<int> RunAsync(string directory)
    {
        int checks = 0;
        void Check(bool value, string reason)
        {
            if (!value) throw new Exception(reason);
            checks++;
        }
        var current = new Version(1, 4, 0, 0);
        GitHubRelease Release(string tag, string notes = "") => new()
        {
            TagName = tag, Body = notes,
            Assets = new()
            {
                new() { Name = $"RiotAutoLogin-{tag}-win-x64.exe" },
                new() { Name = ReleasePolicy.InstallerAssetName },
                new() { Name = ReleasePolicy.FeedAssetName, BrowserDownloadUrl = $"https://example.test/{tag}/feed" },
                new() { Name = $"RiotAutoLogin-{tag}-full.nupkg" }
            }
        };
        var important = Release("v1.4.1", "Important fix");
        var quiet = Release("v1.4.2", "<!-- riotautologin:notify=false -->\nSmall fix");
        var releases = new[] { quiet, important };
        Check(ReleasePolicy.Select(releases, current, false, false) == important, "A newer quiet release must not hide an announced release.");
        Check(ReleasePolicy.Select(releases, current, true, false) == quiet, "Manual checks include quiet releases.");
        Check(ReleasePolicy.Select(new[] { quiet }, current, false, false) == null, "Quiet releases never trigger startup prompts.");
        Check(ReleasePolicy.Select(new[] { quiet }, current, true, true) == quiet, "Packaged clients can manually select quiet releases.");
        Check(ReleasePolicy.CleanNotes(quiet.Body) == "Small fix", "Release metadata is hidden from the displayed changelog.");
        Check(ReleasePolicy.ShouldAnnounce(important), "Existing releases default to announced.");
        Check(!ReleasePolicy.ShouldAnnounce(Release("v1.5.0", "<!-- RIOTAUTOLOGIN:notify = FALSE -->")), "Whitespace and casing are supported.");
        Check(ReleasePolicy.ParseVersion("V1.4.0") == current, "Versions normalize missing revision fields.");
        Check(ReleasePolicy.ParseVersion("v1.4.0-preview") == null, "A preview cannot masquerade as a stable release.");
        Check(ReleasePolicy.ParseVersion("latest") == null, "Invalid tags are ignored.");
        var installed = Release("v1.4.0");
        Check(ReleasePolicy.Select(new[] { installed }, current, true, false) == null, "The installed version is not an update.");
        Check(ReleasePolicy.Select(new[] { installed }, current, true, false, true) == installed, "Standalone users can migrate without waiting for another version.");
        Check(ReleasePolicy.Select(new[] { Release("v1.3.9") }, current, true, false, true) == null, "Migration cannot downgrade the application.");
        var draft = Release("v9.0.0"); draft.Draft = true;
        var preview = Release("v8.0.0"); preview.Prerelease = true;
        Check(ReleasePolicy.Select(new[] { draft, preview, quiet }, current, true, true) == quiet, "Drafts and previews stay out of the stable channel.");
        var incomplete = Release("v2.0.0"); incomplete.Assets.Clear();
        incomplete.Assets.Add(new() { Name = "RiotAutoLogin-Setup.exe" });
        Check(ReleasePolicy.Select(new[] { incomplete }, current, true, false) == null, "Never replace the running EXE with an arbitrarily named installer.");
        incomplete.Assets.Add(new() { Name = ReleasePolicy.FeedAssetName });
        Check(!ReleasePolicy.HasPackage(incomplete), "A feed without a full package is not installable.");
        Check(ReleasePolicy.Select(new[] { Release("v1.4.9"), Release("v1.4.10") }, current, true, false)?.TagName == "v1.4.10", "Versions sort numerically.");

        var downloader = new RecordingDownloader();
        var source = new ReleaseSnapshotSource("https://github.com/KratosCube/RiotAutoLogin",
            new[] { installed, important, quiet, Release("v1.4.3") }, current, new Version(1, 4, 2, 0), downloader);
        await source.GetReleaseFeed(new NullVelopackLogger(), "RiotAutoLogin", "win");
        Check(downloader.Urls.SequenceEqual(new[] { "https://example.test/v1.4.2/feed", "https://example.test/v1.4.1/feed" }),
            "The package feed is pinned to the offered release and keeps intermediate quiet/announced versions.");

        var path = Path.Combine(directory, "download.bin");
        await File.WriteAllTextAsync(path, "verified update");
        var length = new FileInfo(path).Length;
        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        await UpdateDownloadVerifier.VerifyAsync(path, length, digest, default); checks++;
        async Task Reject(Func<Task> operation, string reason)
        {
            try { await operation(); }
            catch (InvalidDataException) { checks++; return; }
            throw new Exception(reason);
        }
        await Reject(() => UpdateDownloadVerifier.VerifyAsync(path, length + 1, digest, default), "Truncated downloads must fail.");
        await File.WriteAllTextAsync(path, "tampered update");
        await Reject(() => UpdateDownloadVerifier.VerifyAsync(path, null, digest, default), "Corrupted downloads must fail checksum verification.");
        await Reject(() => UpdateDownloadVerifier.VerifyAsync(path, null, "md5:invalid", default), "Unsupported checksums must fail closed.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await UpdateDownloadVerifier.VerifyAsync(path, null, digest, cancelled.Token); throw new Exception("Cancellation ignored."); }
        catch (OperationCanceledException) { checks++; }

        var zip = Path.Combine(directory, "setup.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("RiotAutoLogin-Setup.exe").Open())) writer.Write("installer");
            using (var writer = new StreamWriter(archive.CreateEntry("../outside.txt").Open())) writer.Write("must not be extracted");
        }
        var extracted = UpdateDownloadVerifier.ExtractInstaller(zip, Path.Combine(directory, "setup"));
        Check(File.ReadAllText(extracted) == "installer", "Extract the expected installer entry.");
        Check(!File.Exists(Path.Combine(directory, "outside.txt")), "ZIP traversal entries cannot write outside the setup folder.");
        var wrongZip = Path.Combine(directory, "wrong.zip");
        using (var archive = ZipFile.Open(wrongZip, ZipArchiveMode.Create)) archive.CreateEntry("nested/RiotAutoLogin-Setup.exe");
        await Reject(() => Task.Run(() => UpdateDownloadVerifier.ExtractInstaller(wrongZip, Path.Combine(directory, "wrong"))),
            "Only the exact root installer entry is accepted.");
        return checks;
    }

    private sealed class RecordingDownloader : IFileDownloader
    {
        public List<string> Urls { get; } = new();
        public Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        {
            Urls.Add(url);
            return Task.FromResult(Encoding.UTF8.GetBytes("{\"Assets\":[]}"));
        }
        public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30) => throw new NotSupportedException();
        public Task DownloadFile(string url, string targetFile, Action<int> progress, IDictionary<string, string>? headers = null,
            double timeout = 30, CancellationToken cancelToken = default) => throw new NotSupportedException();
    }
}
