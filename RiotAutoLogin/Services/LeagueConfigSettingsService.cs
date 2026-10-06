using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RiotAutoLogin.Services;

public sealed record LeagueConfigState(string? Directory, string[] Files, bool AllReadOnly);

public sealed class LeagueConfigSettingsService
{
    private static readonly string[] SettingFiles = ["PersistedSettings.json", "game.cfg", "input.ini"];
    private readonly string _settingsPath;
    private Preference _preference;

    private sealed class Preference
    {
        public Preference() { }
        public string? ConfigDirectory { get; set; }
        public bool Enabled { get; set; }
    }

    public bool Enabled => _preference.Enabled;

    public LeagueConfigSettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RiotClientAutoLogin", "league_config_settings.json");
        try
        {
            _preference = File.Exists(_settingsPath)
                ? JsonSerializer.Deserialize<Preference>(File.ReadAllText(_settingsPath)) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _preference = new();
        }
    }

    public LeagueConfigState Refresh()
    {
        string? directory = ResolveDirectory();
        if (directory != null && _preference.Enabled)
            SetAttributes(directory, readOnly: true);
        return GetState(directory);
    }

    public LeagueConfigState Inspect() => GetState(ResolveDirectory());

    public LeagueConfigState SetEnabled(bool enabled)
    {
        string directory = ResolveDirectory() ?? throw new DirectoryNotFoundException(
            "League Config was not found. Choose its folder first.");
        SetAttributes(directory, enabled);
        _preference.Enabled = enabled;
        _preference.ConfigDirectory = directory;
        SavePreference();
        return GetState(directory);
    }

    public LeagueConfigState ChooseDirectory(string selectedPath)
    {
        if (_preference.Enabled)
            throw new InvalidOperationException("Turn off Persistent Settings before changing the Config folder.");
        string? directory = ExistingConfig(selectedPath);
        if (directory == null && !string.IsNullOrWhiteSpace(selectedPath))
            directory = ExistingConfig(Path.Combine(selectedPath, "Config"));
        if (directory == null)
            throw new DirectoryNotFoundException("Choose the League of Legends Config folder containing player settings.");
        _preference.ConfigDirectory = directory;
        SavePreference();
        return GetState(directory);
    }

    private string? ResolveDirectory()
    {
        var chosen = ExistingConfig(_preference.ConfigDirectory);
        if (chosen != null) return chosen;

        foreach (string name in new[] { "LeagueClient", "LeagueClientUx", "League of Legends" })
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        string? path = Path.GetDirectoryName(process.MainModule?.FileName);
                        for (int i = 0; i < 3 && path != null; i++)
                        {
                            var found = ExistingConfig(Path.Combine(path, "Config"));
                            if (found != null) return found;
                            path = Path.GetDirectoryName(path);
                        }
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
                    {
                        // The process can exit while its executable path is being read.
                    }
                }
            }
        }

        string? drive = Path.GetPathRoot(Environment.SystemDirectory);
        foreach (string? path in new[]
        {
            drive == null ? null : Path.Combine(drive, "Riot Games", "League of Legends", "Config"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Riot Games", "League of Legends", "Config")
        })
        {
            var found = ExistingConfig(path);
            if (found != null) return found;
        }
        return null;
    }

    private static string? ExistingConfig(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return null;
        return SettingFiles.Any(name => File.Exists(Path.Combine(directory, name)))
            ? Path.GetFullPath(directory) : null;
    }

    private static LeagueConfigState GetState(string? directory)
    {
        if (directory == null) return new(null, [], false);
        var files = SettingFiles.Where(name => File.Exists(Path.Combine(directory, name))).ToArray();
        return new(directory, files, files.Length > 0 && files.All(name =>
            (File.GetAttributes(Path.Combine(directory, name)) & FileAttributes.ReadOnly) != 0));
    }

    private static void SetAttributes(string directory, bool readOnly)
    {
        var files = SettingFiles.Select(name => Path.Combine(directory, name)).Where(File.Exists).ToArray();
        if (files.Length == 0) throw new FileNotFoundException("No League player settings files were found.");
        var changed = new List<(string Path, FileAttributes Attributes)>();
        try
        {
            foreach (string path in files)
            {
                var attributes = File.GetAttributes(path);
                var wanted = readOnly ? attributes | FileAttributes.ReadOnly : attributes & ~FileAttributes.ReadOnly;
                if (wanted == attributes) continue;
                File.SetAttributes(path, wanted);
                changed.Add((path, attributes));
            }
        }
        catch
        {
            foreach (var (path, attributes) in changed)
            {
                try { File.SetAttributes(path, attributes); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            throw;
        }
    }

    private void SavePreference()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        string temporary = _settingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_preference));
        File.Move(temporary, _settingsPath, overwrite: true);
    }
}
