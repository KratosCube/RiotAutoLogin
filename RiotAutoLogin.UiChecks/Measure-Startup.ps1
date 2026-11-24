param(
    [Parameter(Mandatory)] [string[]] $Executable,
    [ValidateRange(1, 10)] [int] $Runs = 4,
    [switch] $SampleAccounts
)
$ErrorActionPreference = 'Stop'
$paths = @($Executable | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })

$accountsPath = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'RiotClientAutoLogin/accounts.json'
$savedAccounts = $null
$fixtureDirectory = $null
if ($SampleAccounts -and (Test-Path -LiteralPath $accountsPath)) {
    $savedAccounts = [IO.File]::ReadAllBytes($accountsPath)
}

try {
    if ($SampleAccounts) {
        Add-Type -AssemblyName System.Drawing
        $fixtureDirectory = Join-Path ([IO.Path]::GetTempPath()) ("riot-startup-" + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $fixtureDirectory | Out-Null
        $accounts = @(foreach ($i in 1..8) {
            $avatar = Join-Path $fixtureDirectory "avatar-$i.png"
            $bitmap = [Drawing.Bitmap]::new(3840, 2160)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([Drawing.Color]::FromArgb(20 + 20 * $i, 40, 90))
                $graphics.FillEllipse([Drawing.Brushes]::Teal, 600, 200, 1900, 1900)
                $bitmap.Save($avatar, [Drawing.Imaging.ImageFormat]::Png)
            } finally {
                $graphics.Dispose()
                $bitmap.Dispose()
            }
            @{
                AccountName = "startup-$i"
                GameName = "Sample $i"
                TagLine = "TEST"
                Region = "eun1"
                AvatarPath = $avatar
                QueueRanks = @{
                    SoloDuo = @{ Tier = 'EMERALD'; Rank = 'II'; LeaguePoints = 50; Wins = 24; Losses = 16 }
                }
            }
        })
        New-Item -ItemType Directory -Force (Split-Path $accountsPath) | Out-Null
        $accounts | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $accountsPath -Encoding utf8NoBOM
        Write-Host 'Measuring with eight saved accounts and eight 3840 x 2160 PNG avatars.'
    }
    $measurements = @(for ($run = 1; $run -le $Runs; $run++) {
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
                [pscustomobject]@{ Executable = $path; Run = $run; Milliseconds = $timer.ElapsedMilliseconds }
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
    })
    foreach ($group in ($measurements | Group-Object Executable)) {
        $values = @($group.Group.Milliseconds | Sort-Object)
        $middle = [int][Math]::Floor($values.Count / 2)
        $median = if ($values.Count % 2) { $values[$middle] } else { ($values[$middle - 1] + $values[$middle]) / 2 }
        Write-Host ("{0} | median responsive window {1} ms" -f $group.Name, $median)
    }
} finally {
    if ($SampleAccounts) {
        if ($null -ne $savedAccounts) {
            [IO.File]::WriteAllBytes($accountsPath, $savedAccounts)
        } else {
            Remove-Item -LiteralPath $accountsPath -ErrorAction SilentlyContinue
        }
        if ($fixtureDirectory -and (Test-Path -LiteralPath $fixtureDirectory)) {
            Remove-Item -LiteralPath $fixtureDirectory -Recurse -Force
        }
    }
}
