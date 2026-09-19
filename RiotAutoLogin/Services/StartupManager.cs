using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace RiotAutoLogin.Services
{
    public static class StartupManager
    {
        private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private static readonly string AppName = Assembly.GetEntryAssembly()?.GetName().Name ?? "RiotAutoLogin";
        private static readonly string AppPath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;

        public static bool IsRegisteredForStartup()
        {
            if (string.IsNullOrEmpty(AppName) || string.IsNullOrEmpty(AppPath))
                return false;

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
                string command = key?.GetValue(AppName)?.ToString() ?? string.Empty;
                string registeredPath = ExtractExecutablePath(command);
                return registeredPath.Equals(AppPath, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StartupManager: Error checking startup registry - {ex.Message}");
                return false;
            }
        }

        public static bool AddToStartup()
        {
            if (string.IsNullOrEmpty(AppName) || string.IsNullOrEmpty(AppPath))
            {
                Debug.WriteLine("StartupManager: Application name or path is invalid, cannot add to startup.");
                return false;
            }
            if (!File.Exists(AppPath))
            {
                 Debug.WriteLine($"StartupManager: Application executable not found at '{AppPath}', cannot add to startup.");
                 return false;
            }

            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath, true);
                if (key == null)
                    return false;

                key.SetValue(AppName, $"\"{AppPath}\"");
                Debug.WriteLine($"StartupManager: Application '{AppName}' added to startup with path '{AppPath}'.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StartupManager: Error adding to startup registry - {ex.Message}");
                return false;
            }
        }

        public static bool RemoveFromStartup()
        {
            if (string.IsNullOrEmpty(AppName))
            {
                 Debug.WriteLine("StartupManager: Application name is invalid, cannot remove from startup.");
                return false;
            }

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
                if (key == null)
                {
                    Debug.WriteLine($"StartupManager: Registry key '{RegistryKeyPath}' not found. Nothing to remove.");
                    return true;
                }

                key.DeleteValue(AppName, false);
                Debug.WriteLine($"StartupManager: Application '{AppName}' removed from startup.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StartupManager: Error removing from startup registry - {ex.Message}");
                return false;
            }
        }

        private static string ExtractExecutablePath(string command)
        {
            string value = command.Trim();
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value[0] == '"')
            {
                int closingQuote = value.IndexOf('"', 1);
                return closingQuote > 1 ? value[1..closingQuote] : value.Trim('"');
            }

            int executableEnd = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return executableEnd >= 0 ? value[..(executableEnd + 4)] : value;
        }
    }
}
