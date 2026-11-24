param(
    [Parameter(Mandatory)] [string[]] $Executable,
    [ValidateRange(1, 10)] [int] $Runs = 4
)
$ErrorActionPreference = 'Stop'
$paths = @($Executable | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })

for ($run = 1; $run -le $Runs; $run++) {
    foreach ($path in $paths) {
        $start = [Diagnostics.ProcessStartInfo]::new($path)
        $start.UseShellExecute = $false
        $start.WorkingDirectory = Split-Path $path
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $process = [Diagnostics.Process]::Start($start)
        try {
            do {
                $process.Refresh()
                if ($process.HasExited) { throw "$path exited before its main window became available." }
                if ($process.MainWindowTitle -eq 'Riot Auto Login' -and $process.WaitForInputIdle(50) -and $process.Responding) {
                    break
                }
                if ($timer.Elapsed.TotalSeconds -ge 30) { throw "Timed out waiting for the main window of $path." }
                Start-Sleep -Milliseconds 20
            } while ($true)
            $timer.Stop()
            Write-Host ("{0} | run {1} | responsive main window {2} ms" -f $path, $run, $timer.ElapsedMilliseconds)
        } finally {
            # The app normally stays in the tray on close. End only the test process we launched.
            if (!$process.HasExited) {
                $process.Kill()
                $process.WaitForExit()
            }
            $process.Dispose()
        }
    }
    [array]::Reverse($paths)
}
