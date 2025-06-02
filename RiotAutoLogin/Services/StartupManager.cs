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
        // Use the assembly name as the registry value name for uniqueness.
        private static readonly string AppName = Assembly.GetEntryAssembly()?.GetName().Name ?? "RiotAutoLogin";
        private static readonly string AppPath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;


        public static bool IsRegisteredForStartup()
        {
            if (string.IsNullOrEmpty(AppName)) return false;

            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false))
                {
                    if (key == null)
                    {
                        Debug.WriteLine($"StartupManager: Registry key '{RegistryKeyPath}' not found.");
                        return false;
                    }
                    object? value = key.GetValue(AppName);
                    return value != null && value.ToString()?.Equals(AppPath, StringComparison.OrdinalIgnoreCase) == true;
                }
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
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true)) // true for writable
                {
                    if (key == null)
                    {
                        // This should ideally not happen for HKCU path unless major OS issues or very restrictive permissions.
                        // Attempt to create it if it doesn't exist, though CurrentVersion\Run should always exist.
                        // For robustness, let's assume OpenSubKey might return null if the path needs creation,
                        // though CreateSubKey is more direct for that.
                        // Re-opening with create if null might be an option or directly using CreateSubKey from CurrentUser.
                        // For now, let's log and fail if it's null directly after OpenSubKey.
                        Debug.WriteLine($"StartupManager: Could not open or create registry key '{RegistryKeyPath}' for writing.");
                        return false;
                    }
                    key.SetValue(AppName, $"\"{AppPath}\""); // Ensure path is quoted if it contains spaces
                    Debug.WriteLine($"StartupManager: Application '{AppName}' added to startup with path '{AppPath}'.");
                    return true;
                }
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
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true)) // true for writable
                {
                    if (key == null)
                    {
                        Debug.WriteLine($"StartupManager: Registry key '{RegistryKeyPath}' not found. Nothing to remove.");
                        return true; // Technically successful if it's not there.
                    }
                    if (key.GetValue(AppName) != null)
                    {
                        key.DeleteValue(AppName, false); // false: do not throw if not found
                        Debug.WriteLine($"StartupManager: Application '{AppName}' removed from startup.");
                    }
                    else
                    {
                        Debug.WriteLine($"StartupManager: Application '{AppName}' was not found in startup. Nothing to remove.");
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StartupManager: Error removing from startup registry - {ex.Message}");
                return false;
            }
        }
    }
} 