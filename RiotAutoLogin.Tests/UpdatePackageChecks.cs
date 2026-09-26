using System.IO.Compression;
using System.Security.Cryptography;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.NuGet;
using Velopack.Sources;

internal static class UpdatePackageChecks
{
    public static async Task RunAsync(string packageDirectory)
    {
        int checks = 0;
        void Check(bool value, string reason)
        {
            if (!value) throw new Exception(reason);
            checks++;
        }
        var feed = VelopackAssetFeed.FromJson(await File.ReadAllTextAsync(Path.Combine(packageDirectory, "releases.win.json")));
        var full = feed.Assets.Where(a => a.Type == VelopackAssetType.Full).OrderBy(a => a.Version).ToArray();
        Check(full.Length == 2, "The smoke test requires two real release packages.");
        var old = full[0]; var latest = full[1];
        var delta = feed.Assets.Single(a => a.Type == VelopackAssetType.Delta && a.Version == latest.Version);
        Check(delta.Size < latest.Size, "A version-only update should be smaller than the full package.");
        string temporary = Path.Combine(Path.GetTempPath(), "RiotAutoLogin-package-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            (UpdateManager Manager, CountingSource Source, string Cache) Create(string name, bool corrupt = false, bool seed = true)
            {
                var root = Path.Combine(temporary, name);
                var cache = Path.Combine(root, "packages");
                Directory.CreateDirectory(cache);
                var oldPath = Path.Combine(packageDirectory, old.FileName);
                if (seed) File.Copy(oldPath, Path.Combine(cache, old.FileName));
                var updater = Path.Combine(root, "Update.exe");
                File.WriteAllBytes(updater, new ZipPackage(oldPath, loadUpdateExe: true).UpdateExeBytes
                    ?? throw new Exception("Missing updater in the package."));
                var locator = new TestVelopackLocator("RiotAutoLogin", old.Version.ToString(), cache, root, root, updater, "win");
                var source = new CountingSource(packageDirectory, corrupt);
                return (new UpdateManager(source, locator: locator), source, cache);
            }

            var normal = Create("normal");
            var plan = await normal.Manager.CheckForUpdatesAsync() ?? throw new Exception("No update detected.");
            Check(plan.DeltasToTarget.Length == 1, "The installed package must enable a delta update.");
            await normal.Manager.DownloadUpdatesAsync(plan);
            Check(normal.Source.Downloads.SequenceEqual(new[] { delta.FileName }), "A successful delta must not download the full runtime again.");
            Check(PayloadsEqual(Path.Combine(packageDirectory, latest.FileName), Path.Combine(normal.Cache, latest.FileName)),
                "Every reconstructed file must match the new full package.");
            int requests = normal.Source.Downloads.Count;
            await normal.Manager.DownloadUpdatesAsync(plan);
            Check(normal.Source.Downloads.Count == requests, "A downloaded update is reused after choosing Later.");
            Check(normal.Manager.UpdatePendingRestart?.Version == latest.Version, "Prepared update remains available for explicit installation.");

            var corrupted = Create("corrupted", corrupt: true);
            await corrupted.Manager.DownloadUpdatesAsync((await corrupted.Manager.CheckForUpdatesAsync())!);
            Check(corrupted.Source.Downloads.Contains(latest.FileName), "A damaged delta falls back to the full package.");
            Check(PayloadsEqual(Path.Combine(packageDirectory, latest.FileName), Path.Combine(corrupted.Cache, latest.FileName)),
                "Fallback produces a complete valid update.");

            var missingBase = Create("missing-base", seed: false);
            var fullPlan = (await missingBase.Manager.CheckForUpdatesAsync())!;
            Check(fullPlan.DeltasToTarget.Length == 0, "Without a cached base, only a full update is safe.");
            await missingBase.Manager.DownloadUpdatesAsync(fullPlan);
            Check(missingBase.Source.Downloads.SequenceEqual(new[] { latest.FileName }), "Missing-base fallback downloads one full package.");

            var cancelled = Create("cancelled");
            using var tokenSource = new CancellationTokenSource(); tokenSource.Cancel();
            try
            {
                await cancelled.Manager.DownloadUpdatesAsync((await cancelled.Manager.CheckForUpdatesAsync())!, cancelToken: tokenSource.Token);
                throw new Exception("A cancelled update was allowed to complete.");
            }
            catch (OperationCanceledException) { checks++; }
            Check(!File.Exists(Path.Combine(cancelled.Cache, latest.FileName)), "Cancellation cannot leave a supposedly ready update.");

            Console.WriteLine($"Passed {checks} package checks. Full: {latest.Size / 1048576.0:F2} MiB; delta: {delta.Size / 1048576.0:F2} MiB ({delta.Size * 100.0 / latest.Size:F2}%).");
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static bool PayloadsEqual(string expected, string actual)
    {
        Dictionary<string, string> Hashes(string path)
        {
            using var archive = ZipFile.OpenRead(path);
            return archive.Entries.Where(e => !e.FullName.EndsWith('/')).ToDictionary(e => e.FullName, e =>
            {
                using var stream = e.Open();
                return Convert.ToHexString(SHA256.HashData(stream));
            });
        }
        var first = Hashes(expected); var second = Hashes(actual);
        return first.Count == second.Count && first.All(pair => second.TryGetValue(pair.Key, out var hash) && hash == pair.Value);
    }

    private sealed class CountingSource : SimpleFileSource
    {
        private readonly bool _corrupt;
        public List<string> Downloads { get; } = new();
        public CountingSource(string directory, bool corrupt) : base(new DirectoryInfo(directory)) => _corrupt = corrupt;
        public override async Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry,
            string localFile, Action<int> progress, CancellationToken cancelToken)
        {
            cancelToken.ThrowIfCancellationRequested();
            Downloads.Add(releaseEntry.FileName);
            await base.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken);
            if (_corrupt && releaseEntry.Type == VelopackAssetType.Delta)
                await File.WriteAllTextAsync(localFile, "damaged patch", cancelToken);
        }
    }
}
