param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$OutputDir = 'build/packages',
    [string]$AssetDir = 'build/assets',
    [switch]$SkipCompatibilityExe
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$project = 'RiotAutoLogin/RiotAutoLogin.csproj'
$publishDir = "build/publish-$Version"
dotnet publish $project -c Release -r win-x64 --self-contained true -o $publishDir `
    /p:PublishSingleFile=false /p:PublishReadyToRun=true /p:DebugType=None `
    "/p:Version=$Version" "/p:AssemblyVersion=$Version.0" "/p:FileVersion=$Version.0"

dotnet tool run vpk pack --packId RiotAutoLogin --packVersion $Version --packDir $publishDir `
    --mainExe RiotAutoLogin.exe --packTitle 'Riot Auto Login' --packAuthors KratosCube `
    --icon RiotAutoLogin/resources/logoAcc.ico --channel win --runtime win-x64 `
    --outputDir $OutputDir --delta BestSpeed

New-Item -ItemType Directory -Force $AssetDir | Out-Null
$feed = Get-Content (Join-Path $OutputDir 'releases.win.json') -Raw | ConvertFrom-Json
# A GitHub release must contain only its own full/delta packages. Older bases stay in older releases.
$feed.Assets = @($feed.Assets | Where-Object { $_.Version -eq $Version })
if (@($feed.Assets | Where-Object { $_.FileName -like '*-full.nupkg' }).Count -ne 1) {
    throw 'The release must contain exactly one full update package.'
}
foreach ($asset in $feed.Assets) {
    Copy-Item (Join-Path $OutputDir $asset.FileName) $AssetDir -Force
}
$feed | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $AssetDir 'releases.win.json') -Encoding utf8NoBOM

$setup = @(Get-ChildItem $OutputDir -Filter '*Setup.exe')
if ($setup.Count -ne 1) { throw 'Expected exactly one Velopack installer.' }
$staging = Join-Path $AssetDir 'setup-staging'
New-Item -ItemType Directory -Force $staging | Out-Null
Copy-Item $setup[0].FullName (Join-Path $staging 'RiotAutoLogin-Setup.exe') -Force
# Older updaters select the first .exe asset. Wrapping setup prevents them from overwriting the app with an installer.
Compress-Archive -Path (Join-Path $staging 'RiotAutoLogin-Setup.exe') `
    -DestinationPath (Join-Path $AssetDir 'RiotAutoLogin-Setup.zip') -Force
Remove-Item $staging -Recurse -Force
$portable = @(Get-ChildItem $OutputDir -Filter '*Portable.zip')
if ($portable.Count -ne 1) { throw 'Expected exactly one portable archive.' }
Copy-Item $portable[0].FullName (Join-Path $AssetDir 'RiotAutoLogin-Portable.zip') -Force

if (!$SkipCompatibilityExe) {
    $legacyDir = "build/standalone-$Version"
    dotnet publish $project -c Release -r win-x64 --self-contained true -o $legacyDir `
        /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:EnableCompressionInSingleFile=true /p:PublishReadyToRun=true /p:DebugType=None `
        "/p:Version=$Version" "/p:AssemblyVersion=$Version.0" "/p:FileVersion=$Version.0"
    Copy-Item (Join-Path $legacyDir 'RiotAutoLogin.exe') `
        (Join-Path $AssetDir "RiotAutoLogin-v$Version-win-x64.exe") -Force
}

$sizes = Get-ChildItem $AssetDir -File | Select-Object Name, Length,
    @{Name='MiB'; Expression={[Math]::Round($_.Length / 1MB, 2)}}
$sizes | Format-Table | Out-Host
if ($env:GITHUB_STEP_SUMMARY) {
    "### Release $Version package sizes" | Add-Content $env:GITHUB_STEP_SUMMARY
    '| Asset | MiB |' | Add-Content $env:GITHUB_STEP_SUMMARY
    '| --- | ---: |' | Add-Content $env:GITHUB_STEP_SUMMARY
    foreach ($size in $sizes) { "| $($size.Name) | $($size.MiB) |" | Add-Content $env:GITHUB_STEP_SUMMARY }
}
