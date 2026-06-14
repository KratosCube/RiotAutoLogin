param(
    [int]$PreviewLength = 1800,
    [string]$OutputPath = "rune-debug-output.json"
)

$ErrorActionPreference = "Stop"

function Shorten-Body {
    param([string]$Body, [int]$Length)

    if ([string]::IsNullOrWhiteSpace($Body)) {
        return ""
    }

    $compact = $Body -replace "`r", " " -replace "`n", " "
    if ($compact.Length -le $Length) {
        return $compact
    }

    return $compact.Substring(0, $Length) + "..."
}

function Get-JsonValue {
    param($Object, [string]$Name, $Default = $null)

    if ($null -eq $Object) {
        return $Default
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $Default
    }

    return $property.Value
}

function Normalize-Position {
    param([string]$Position)

    if ([string]::IsNullOrWhiteSpace($Position)) {
        return "UNKNOWN"
    }

    switch ($Position.Trim().ToUpperInvariant()) {
        "SUPPORT" { return "UTILITY" }
        "UTILITY" { return "UTILITY" }
        "ADC" { return "BOTTOM" }
        "BOTTOM" { return "BOTTOM" }
        "MID" { return "MIDDLE" }
        "MIDDLE" { return "MIDDLE" }
        "JUNGLE" { return "JUNGLE" }
        "TOP" { return "TOP" }
        default { return $Position.Trim().ToUpperInvariant() }
    }
}

function Invoke-LcuGet {
    param([string]$Endpoint)

    try {
        $response = Invoke-WebRequest `
            -Uri "https://127.0.0.1:$Port/$Endpoint" `
            -Headers $Headers `
            -Method GET `
            -UseBasicParsing `
            -SkipCertificateCheck `
            -TimeoutSec 5

        return [ordered]@{
            endpoint = $Endpoint
            status = [int]$response.StatusCode
            contentLength = $response.Content.Length
            preview = Shorten-Body -Body $response.Content -Length $PreviewLength
        }
    }
    catch {
        $statusCode = 999
        $body = $_.Exception.Message

        if ($_.Exception.Response) {
            try {
                $statusCode = [int]$_.Exception.Response.StatusCode
                $stream = $_.Exception.Response.GetResponseStream()
                if ($stream) {
                    $reader = New-Object System.IO.StreamReader($stream)
                    $body = $reader.ReadToEnd()
                }
            }
            catch {
                $body = $_.Exception.Message
            }
        }

        return [ordered]@{
            endpoint = $Endpoint
            status = $statusCode
            contentLength = if ($body) { $body.Length } else { 0 }
            preview = Shorten-Body -Body $body -Length $PreviewLength
        }
    }
}

$client = Get-CimInstance Win32_Process -Filter "name = 'LeagueClientUx.exe'" | Select-Object -First 1
if (-not $client) {
    throw "LeagueClientUx.exe is not running. Open League Client and enter champion select first."
}

$commandLine = [string]$client.CommandLine
$portMatch = [regex]::Match($commandLine, '--app-port="?(\d+)"?')
$tokenMatch = [regex]::Match($commandLine, '--remoting-auth-token=([a-zA-Z0-9_-]+)')

if (-not $portMatch.Success -or -not $tokenMatch.Success) {
    throw "Could not read League Client LCU port/auth token from LeagueClientUx command line."
}

$Port = $portMatch.Groups[1].Value
$authToken = $tokenMatch.Groups[1].Value
$auth = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("riot:$authToken"))
$Headers = @{ Authorization = "Basic $auth" }

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

$sessionResult = Invoke-LcuGet "lol-champ-select/v1/session"
$session = $null
try {
    if ($sessionResult.status -eq 200 -and -not [string]::IsNullOrWhiteSpace($sessionResult.preview)) {
        $session = $sessionResult.preview | ConvertFrom-Json
    }
}
catch {
    $session = $null
}

$localPlayerCellId = Get-JsonValue -Object $session -Name "localPlayerCellId" -Default $null
$localPlayer = $null
if ($null -ne $session -and $null -ne $session.myTeam) {
    $localPlayer = $session.myTeam | Where-Object { $_.cellId -eq $localPlayerCellId } | Select-Object -First 1
}

$selectedChampionId = [int](Get-JsonValue -Object $localPlayer -Name "championId" -Default 0)
$pickIntentChampionId = [int](Get-JsonValue -Object $localPlayer -Name "championPickIntent" -Default 0)
$championId = if ($selectedChampionId -gt 0) { $selectedChampionId } else { $pickIntentChampionId }
$assignedPosition = [string](Get-JsonValue -Object $localPlayer -Name "assignedPosition" -Default "")
$normalizedPosition = Normalize-Position $assignedPosition
$mapId = [int](Get-JsonValue -Object $session -Name "mapId" -Default 0)
$queueId = [int](Get-JsonValue -Object $session -Name "queueId" -Default 0)
$effectiveMapId = if ($mapId -gt 0) { $mapId } else { 11 }

$endpoints = New-Object System.Collections.Generic.List[string]
$endpoints.Add("lol-champ-select/v1/session")
$endpoints.Add("lol-perks/v1/currentpage")
$endpoints.Add("lol-perks/v1/pages")
$endpoints.Add("lol-perks/v1/styles")
$endpoints.Add("lol-perks/v1/inventory")
$endpoints.Add("lol-perks/v1/recommended-pages")

if ($championId -gt 0) {
    $endpoints.Add("lol-perks/v1/recommended-pages?championId=$championId&position=$normalizedPosition&mapId=$effectiveMapId")
    $endpoints.Add("lol-perks/v1/recommended-pages/champion/$championId/position/$normalizedPosition/map/$effectiveMapId")
    $endpoints.Add("lol-perks/v1/recommended-pages/champion/$championId/position/$normalizedPosition")
    $endpoints.Add("lol-perks/v1/recommended-pages/champion/$championId/map/$effectiveMapId")
    $endpoints.Add("lol-perks/v1/recommended-pages/champion/$championId")
    $endpoints.Add("lol-perks/v1/recommended-pages/$championId/$normalizedPosition/$effectiveMapId")
    $endpoints.Add("lol-perks/v1/recommended-pages/$championId")
}

$results = foreach ($endpoint in ($endpoints | Select-Object -Unique)) {
    Invoke-LcuGet $endpoint
}

$output = [ordered]@{
    generatedAt = (Get-Date).ToString("o")
    lcuPort = $Port
    phase = Get-JsonValue -Object (Get-JsonValue -Object $session -Name "timer" -Default $null) -Name "phase" -Default ""
    selectedChampionId = $selectedChampionId
    pickIntentChampionId = $pickIntentChampionId
    championId = $championId
    assignedPosition = $assignedPosition
    normalizedPosition = $normalizedPosition
    mapId = $mapId
    effectiveMapId = $effectiveMapId
    queueId = $queueId
    endpointResults = $results
}

$output | ConvertTo-Json -Depth 30 | Set-Content -Path $OutputPath -Encoding UTF8
Write-Host "Rune debug saved to: $((Resolve-Path $OutputPath).Path)"
Write-Host "Attach rune-debug-output.json here. It does not include your LCU auth token."
