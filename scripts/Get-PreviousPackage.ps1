param(
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][version]$Version,
    [string]$OutputDir = 'build/packages'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$releases = gh api "repos/$Repository/releases?per_page=100" | ConvertFrom-Json
$previous = $releases | Where-Object {
    !$_.draft -and !$_.prerelease -and $_.tag_name -match '^v?\d+\.\d+\.\d+$' -and
    [version]$_.tag_name.TrimStart('v') -lt $Version -and
    @($_.assets | Where-Object { $_.name -eq 'releases.win.json' }).Count -eq 1
} | Sort-Object { [version]$_.tag_name.TrimStart('v') } -Descending | Select-Object -First 1
if (!$previous) {
    Write-Host 'First packaged release: no previous package exists, so no delta is possible yet.'
    return
}
$full = @($previous.assets | Where-Object { $_.name -like 'RiotAutoLogin-*-full.nupkg' })
if ($full.Count -ne 1) { throw 'Previous release has no unique full package; cannot generate a reliable delta.' }
gh release download $previous.tag_name --repo $Repository --pattern $full[0].name --dir $OutputDir
$path = Join-Path $OutputDir $full[0].name
if ((Get-Item $path).Length -ne $full[0].size) { throw 'Previous package download is incomplete.' }
if ($full[0].digest -notmatch '^sha256:[a-fA-F0-9]{64}$' -or
    "sha256:$((Get-FileHash $path -Algorithm SHA256).Hash)" -ine $full[0].digest) {
    throw 'Previous package checksum is missing or invalid.'
}
Write-Host "Delta base: $($previous.tag_name)"
