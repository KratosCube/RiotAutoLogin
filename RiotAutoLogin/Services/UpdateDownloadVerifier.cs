using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services;

public static class UpdateDownloadVerifier
{
    public static async Task VerifyAsync(string path, long? expectedSize, string? digest, CancellationToken token)
    {
        if (expectedSize is > 0 && new FileInfo(path).Length != expectedSize)
            throw new InvalidDataException("The update download is incomplete. Please try again.");
        if (string.IsNullOrWhiteSpace(digest)) return; // Releases predating GitHub's digest field.
        if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || digest.Length != 71)
            throw new InvalidDataException("The release has an unsupported checksum.");
        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        if (!actual.Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update checksum does not match. Please download it again.");
    }

    public static string ExtractInstaller(string archivePath, string destinationDirectory)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        // Extract only this exact root entry. Never trust a path supplied by the ZIP.
        var entries = archive.Entries.Where(e => e.FullName == "RiotAutoLogin-Setup.exe").ToArray();
        if (entries.Length != 1 || entries[0].Length <= 0 || entries[0].Length > 512L * 1024 * 1024)
            throw new InvalidDataException("The update does not contain a valid installer.");
        Directory.CreateDirectory(destinationDirectory);
        var path = Path.Combine(destinationDirectory, "RiotAutoLogin-Setup.exe");
        entries[0].ExtractToFile(path, overwrite: false);
        return path;
    }
}
