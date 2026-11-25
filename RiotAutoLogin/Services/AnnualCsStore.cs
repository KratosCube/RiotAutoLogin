using RiotAutoLogin.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RiotAutoLogin.Services;

// Large match history is loaded only when Farming opens, outside the startup account file.
internal sealed class AnnualCsStore(string path, string? legacyPath = null)
{
    internal static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        BuildChannel.DataDirectoryName, "cs-history.json");
    internal static string DefaultLegacyPath => Path.Combine(Path.GetDirectoryName(DefaultPath)!, "cs-legacy-accounts.json");

    internal static bool PreserveLegacy(string accountsJson, string backupPath)
    {
        if (!accountsJson.Contains("\"AnnualCs\"", StringComparison.Ordinal) || File.Exists(backupPath)) return true;
        try { File.WriteAllText(backupPath, accountsJson); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    internal static string Identity(Account account) =>
        $"{RemotePlayerProfileLinks.RegionSlug(account.Region)}/{account.GameName.Trim().ToUpperInvariant()}#{account.TagLine.Trim().ToUpperInvariant()}";

    internal Dictionary<string, AnnualCs> Load()
    {
        if (File.Exists(path)) return JsonSerializer.Deserialize<Dictionary<string, AnnualCs>>(File.ReadAllText(path)) ?? new();
        var saved = new Dictionary<string, AnnualCs>();
        if (legacyPath == null || !File.Exists(legacyPath)) return saved;
        using var document = JsonDocument.Parse(File.ReadAllText(legacyPath));
        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (!row.TryGetProperty("AnnualCs", out var history) || history.ValueKind != JsonValueKind.Object) continue;
            var identity = new Account
            {
                GameName = row.GetProperty("GameName").GetString() ?? "",
                TagLine = row.GetProperty("TagLine").GetString() ?? "",
                Region = row.GetProperty("Region").GetString() ?? ""
            };
            saved[Identity(identity)] = history.Deserialize<AnnualCs>() ?? new();
        }
        Save(saved); // Only remove the migration snapshot after the standalone history is durable.
        File.Delete(legacyPath);
        return saved;
    }

    internal void Save(Dictionary<string, AnnualCs> snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));
        File.Move(temporary, path, true);
    }
}
