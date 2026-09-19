using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace RiotAutoLogin
{
    public partial class MainWindow
    {
        private string _runningExecutablePath = string.Empty;

        private void InitializeApplicationLocation()
        {
            _runningExecutablePath = ResolveRunningExecutablePath();
            txtExecutablePath.Text = _runningExecutablePath;
            txtExecutablePath.ToolTip = _runningExecutablePath;
        }

        private static string ResolveRunningExecutablePath()
        {
            try
            {
                string? processPath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(processPath))
                    return Path.GetFullPath(processPath);

                using Process currentProcess = Process.GetCurrentProcess();
                string? mainModulePath = currentProcess.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(mainModulePath))
                    return Path.GetFullPath(mainModulePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not resolve the running executable path: {ex.Message}");
            }

            return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        }

        private void btnCopyExecutablePath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_runningExecutablePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"The application path could not be copied:\n{ex.Message}",
                    "Copy Path Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void btnOpenExecutableFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string? directory = File.Exists(_runningExecutablePath)
                    ? Path.GetDirectoryName(_runningExecutablePath)
                    : Directory.Exists(_runningExecutablePath)
                        ? _runningExecutablePath
                        : null;

                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                    throw new DirectoryNotFoundException("The application folder no longer exists.");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true
                };

                startInfo.Arguments = File.Exists(_runningExecutablePath)
                    ? $"/select,\"{_runningExecutablePath}\""
                    : $"\"{directory}\"";

                Process.Start(startInfo)?.Dispose();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"The application folder could not be opened:\n{ex.Message}",
                    "Open Folder Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}
