using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace RiotAutoLogin.Services
{
    public static class AutoAcceptSettingsService
    {
        public const int MaxDelaySeconds = 30;
        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RiotClientAutoLogin", "autoaccept_settings.json");

        static AutoAcceptSettingsService()
        {
            Load();
        }

        public static int DelaySeconds { get; private set; }
        public static int DelayMilliseconds => DelaySeconds * 1000;

        public static void Load()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    DelaySeconds = 0;
                    return;
                }

                string json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AutoAcceptSettings>(json);
                DelaySeconds = NormalizeDelay(settings?.DelaySeconds ?? 0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading auto-accept settings: {ex.Message}");
                DelaySeconds = 0;
            }
        }

        public static int SaveDelaySeconds(int delaySeconds)
        {
            DelaySeconds = NormalizeDelay(delaySeconds);

            try
            {
                string? directory = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var settings = new AutoAcceptSettings
                {
                    DelaySeconds = DelaySeconds
                };

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving auto-accept settings: {ex.Message}");
            }

            return DelaySeconds;
        }

        private static int NormalizeDelay(int delaySeconds)
        {
            return Math.Clamp(delaySeconds, 0, MaxDelaySeconds);
        }

        private sealed class AutoAcceptSettings
        {
            public int DelaySeconds { get; set; }
        }
    }
}
