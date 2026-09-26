param(
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][ValidatePattern('^v\d+\.\d+\.\d+$')][string]$Tag,
    [Parameter(Mandatory)][string]$Commit,
    [ValidateSet('true', 'false', 'preserve')][string]$NotifyUsers = 'preserve',
    [string]$AssetDir = 'build/assets'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$releases = gh api "repos/$Repository/releases?per_page=100" | ConvertFrom-Json
$release = $releases | Where-Object { $_.tag_name -eq $Tag } | Select-Object -First 1
$body = if ($release) { $release.body } else {
    $notesRequest = @{ tag_name = $Tag; target_commitish = $Commit } | ConvertTo-Json
    $notesRequest | Set-Content build/notes-request.json -Encoding utf8NoBOM
    (gh api "repos/$Repository/releases/generate-notes" --method POST --input build/notes-request.json | ConvertFrom-Json).body
}
$marker = '<!--\s*riotautologin:notify\s*=\s*(true|false)\s*-->'
if ($NotifyUsers -eq 'preserve') {
    if ($body -match '(?i)<!--\s*riotautologin:notify\s*=\s*false\s*-->') { $NotifyUsers = 'false' }
    elseif ($body -match '(?i)<!--\s*riotautologin:notify\s*=\s*true\s*-->') { $NotifyUsers = 'true' }
    else { $NotifyUsers = (Get-Content release-policy.json -Raw | ConvertFrom-Json).notifyUsers.ToString().ToLowerInvariant() }
}
$body = [regex]::Replace(($body ?? ''), $marker, '', 'IgnoreCase').Trim()
$body = "<!-- riotautologin:notify=$NotifyUsers -->`n`n$body"
if (!$release) {
    @{ tag_name=$Tag; target_commitish=$Commit; name="RiotAutoLogin $Tag"; body=$body; draft=$true } |
        ConvertTo-Json | Set-Content build/release-request.json -Encoding utf8NoBOM
    $release = gh api "repos/$Repository/releases" --method POST --input build/release-request.json | ConvertFrom-Json
}
else {
    # Set the policy before making any replacement assets visible.
    @{ body=$body; make_latest=$NotifyUsers } |
        ConvertTo-Json | Set-Content build/release-policy-request.json -Encoding utf8NoBOM
    gh api "repos/$Repository/releases/$($release.id)" --method PATCH --input build/release-policy-request.json | Out-Null
}
# Publish the feed last so a partially uploaded release is not offered by packaged clients.
$files = @(Get-ChildItem $AssetDir -File | Where-Object { $_.Name -ne 'releases.win.json' } | ForEach-Object FullName)
gh release upload $Tag @files --repo $Repository --clobber
gh release upload $Tag (Join-Path $AssetDir 'releases.win.json') --repo $Repository --clobber
@{ body=$body; draft=$false; make_latest=$NotifyUsers } |
    ConvertTo-Json | Set-Content build/release-request.json -Encoding utf8NoBOM
gh api "repos/$Repository/releases/$($release.id)" --method PATCH --input build/release-request.json | Out-Null
Write-Host "Published $Tag; notify users: $NotifyUsers"
