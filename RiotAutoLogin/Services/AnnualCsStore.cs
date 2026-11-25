using RiotAutoLogin.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RiotAutoLogin.Services;

// Large match history is loaded only when Farming opens, outside the startup account file.
internal sealed class AnnualCsStore(string path)
{
    internal static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        BuildChannel.DataDirectoryName, "cs-history.json");

    internal static string Identity(Account account) =>
        $"{RemotePlayerProfileLinks.RegionSlug(account.Region)}/{account.GameName.Trim().ToUpperInvariant()}#{account.TagLine.Trim().ToUpperInvariant()}";

    internal Dictionary<string, AnnualCs> Load()
    {
        if (!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<Dictionary<string, AnnualCs>>(File.ReadAllText(path)) ?? new();
    }

    internal void Save(Dictionary<string, AnnualCs> snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));
        File.Move(temporary, path, true);
    }
}
