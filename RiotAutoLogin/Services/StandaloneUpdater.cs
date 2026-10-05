using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services;

// A running Windows executable cannot replace itself. Stage beside it while the UI
// is still open, then let a tiny helper replace it after that exact process exits.
public static class StandaloneUpdater
{
    public static string UpdatesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RiotClientAutoLogin", "Updates");

    private static string FailurePath => Path.Combine(UpdatesDirectory, "last-update-failure.txt");

    public static string? TakeFailureMessage()
    {
        try
        {
            if (!File.Exists(FailurePath)) return null;
            var message = File.ReadAllText(FailurePath);
            File.Delete(FailurePath);
            return message;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static async Task<Process> LaunchAsync(string source, string target, int previousProcessId,
        long previousStartUtcTicks, bool restart = true, string? helperDirectory = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The EXE updater needs Windows.");
        source = Path.GetFullPath(source);
        target = Path.GetFullPath(target);
        if (!File.Exists(source) || !File.Exists(target) ||
            string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The downloaded update or running executable was not found.");

        var targetDirectory = Path.GetDirectoryName(target)!;
        var directory = helperDirectory ?? UpdatesDirectory;
        Directory.CreateDirectory(UpdatesDirectory);
        Directory.CreateDirectory(directory);
        var operation = Guid.NewGuid().ToString("N");
        var pending = Path.Combine(targetDirectory, $".{Path.GetFileName(target)}.{operation}.pending");
        var backup = Path.Combine(targetDirectory, $".{Path.GetFileName(target)}.{operation}.backup");
        var script = Path.Combine(directory, $"update-{operation}.ps1");

        try
        {
            // This fails before shutdown if the app's directory is not writable.
            await Task.Run(() => File.Copy(source, pending));
            string hash;
            await using (var file = File.OpenRead(pending))
                hash = Convert.ToHexString(await SHA256.HashDataAsync(file));
            File.WriteAllText(script, Script);

            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = !restart,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(script);
            start.Environment["RAL_SOURCE_EXE"] = source;
            start.Environment["RAL_TARGET_EXE"] = target;
            start.Environment["RAL_PENDING_EXE"] = pending;
            start.Environment["RAL_BACKUP_EXE"] = backup;
            start.Environment["RAL_EXPECTED_SHA256"] = hash;
            start.Environment["RAL_PREVIOUS_PID"] = previousProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            start.Environment["RAL_PREVIOUS_START_TICKS"] = previousStartUtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);
            start.Environment["RAL_RESTART"] = restart ? "1" : "0";
            start.Environment["RAL_FAILURE_PATH"] = FailurePath;
            return Process.Start(start) ?? throw new IOException("Could not start the update helper.");
        }
        catch
        {
            try { File.Delete(pending); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            try { File.Delete(script); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private const string Script = """
        $ErrorActionPreference = 'Stop'
        $source = $env:RAL_SOURCE_EXE
        $target = $env:RAL_TARGET_EXE
        $pending = $env:RAL_PENDING_EXE
        $backup = $env:RAL_BACKUP_EXE
        $failure = $env:RAL_FAILURE_PATH
        $replaced = $false
        $oldStillRunning = $false
        try {
            $previousProcessId = [int]$env:RAL_PREVIOUS_PID
            $previousStartTicks = [long]$env:RAL_PREVIOUS_START_TICKS
            $deadline = [DateTime]::UtcNow.AddSeconds(60)
            if ($previousProcessId -gt 0) {
                while ($true) {
                    $oldProcess = Get-Process -Id $previousProcessId -ErrorAction SilentlyContinue
                    if ($null -eq $oldProcess) { break }
                    if ($oldProcess.StartTime.ToUniversalTime().Ticks -ne $previousStartTicks) { break }
                    if ([DateTime]::UtcNow -ge $deadline) {
                        $oldStillRunning = $true
                        throw 'The previous application instance did not exit within 60 seconds.'
                    }
                    Start-Sleep -Milliseconds 250
                }
            }

            if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw 'The original executable is missing.' }
            if (-not (Test-Path -LiteralPath $pending -PathType Leaf)) { throw 'The staged update is missing.' }
            $stream = [System.IO.File]::OpenRead($pending)
            $sha256 = [System.Security.Cryptography.SHA256]::Create()
            try {
                $actualHash = [System.BitConverter]::ToString($sha256.ComputeHash($stream)).Replace('-', '')
            } finally {
                $sha256.Dispose()
                $stream.Dispose()
            }
            if ($actualHash -ne $env:RAL_EXPECTED_SHA256) {
                throw 'The staged update failed its checksum check.'
            }
            # pending and target are siblings, so replacement is on the same volume.
            for ($attempt = 0; $attempt -lt 10; $attempt++) {
                try {
                    [System.IO.File]::Replace($pending, $target, $backup, $true)
                    $replaced = $true
                    break
                } catch {
                    if ($attempt -eq 9) { throw }
                    Start-Sleep -Milliseconds 500
                }
            }
            Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $source -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath (Split-Path -Parent $source) -ErrorAction SilentlyContinue
            if ($env:RAL_RESTART -eq '1') {
                Start-Process -FilePath $target -WorkingDirectory (Split-Path -Parent $target)
            }
            Remove-Item -LiteralPath $failure -Force -ErrorAction SilentlyContinue
        } catch {
            try { [System.IO.File]::WriteAllText($failure, "The update could not replace the current EXE: $($_.Exception.Message)") } catch {}
            if (-not $replaced -and -not $oldStillRunning -and $env:RAL_RESTART -eq '1') {
                try { Start-Process -FilePath $target -WorkingDirectory (Split-Path -Parent $target) } catch {}
            }
            exit 1
        } finally {
            Remove-Item -LiteralPath $pending -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
        }
        """;
}
