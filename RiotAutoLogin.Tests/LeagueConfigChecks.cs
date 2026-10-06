using RiotAutoLogin.Services;

internal static class LeagueConfigChecks
{
    public static int Run(string directory)
    {
        var gameRoot = Path.Combine(directory, "League of Legends");
        var config = Path.Combine(gameRoot, "Config");
        Directory.CreateDirectory(config);
        var files = new[] { "PersistedSettings.json", "game.cfg", "input.ini" }
            .Select(name => Path.Combine(config, name)).ToArray();
        foreach (var file in files) File.WriteAllText(file, "player settings");
        var settingsPath = Path.Combine(directory, "league-config-test.json");
        var service = new LeagueConfigSettingsService(settingsPath);
        var chosen = service.ChooseDirectory(gameRoot);
        if (chosen.Directory != config || chosen.AllReadOnly) throw new Exception("Config folder selection failed.");
        var locked = service.SetEnabled(true);
        if (!locked.AllReadOnly || files.Any(f => (File.GetAttributes(f) & FileAttributes.ReadOnly) == 0))
            throw new Exception("Persistent settings must mark all existing files read-only.");
        if (files.Any(f => File.ReadAllText(f) != "player settings"))
            throw new Exception("The toggle must not alter player settings contents.");
        var reloaded = new LeagueConfigSettingsService(settingsPath);
        if (!reloaded.Enabled || !reloaded.Refresh().AllReadOnly)
            throw new Exception("Persistent settings must survive app restart.");
        try
        {
            reloaded.ChooseDirectory(directory);
            throw new Exception("Changing folders while locked must be rejected.");
        }
        catch (InvalidOperationException) { }
        File.SetAttributes(files[2], FileAttributes.Normal);
        if (!reloaded.Refresh().AllReadOnly)
            throw new Exception("A replaced settings file must be locked again on refresh.");
        var unlocked = reloaded.SetEnabled(false);
        if (unlocked.AllReadOnly || files.Any(f => (File.GetAttributes(f) & FileAttributes.ReadOnly) != 0))
            throw new Exception("Disabling must restore write access to each existing file.");
        File.Delete(files[2]);
        if (reloaded.SetEnabled(true).Files.Length != 2 || File.Exists(files[2]))
            throw new Exception("Missing optional config files must not be created.");
        reloaded.SetEnabled(false);
        return 7;
    }
}
