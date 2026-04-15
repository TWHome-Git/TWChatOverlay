param(
    [string]$InputJsonPath = ".\EquipmentData.json",
    [string]$OutputCsvPath = ".\EquipmentData_edit.csv",
    [string]$InputJsonUrl = "https://raw.githubusercontent.com/TWHome-Git/TWHomeDB/main/EquipmentData.json",
    [switch]$ForceRemote
)

$ErrorActionPreference = "Stop"

function Write-Utf8BomText {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $dir = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($dir) -and -not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir | Out-Null
    }

    $utf8Bom = New-Object System.Text.UTF8Encoding($true)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8Bom)
}

function Try-ParseJson {
    param([string]$Text)
    try {
        if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
        $normalized = $Text.Trim()
        if ($normalized.Length -gt 0 -and [int][char]$normalized[0] -eq 0xFEFF) {
            $normalized = $normalized.Substring(1)
        }
        $normalized = $normalized.Replace([string][char]0xFEFF, "")
        return ($normalized | ConvertFrom-Json)
    } catch {
        return $null
    }
}

function Try-FetchRemoteJsonText {
    param([string]$Url)
    try {
        $resp = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 20 -Headers @{ "User-Agent" = "TWChatOverlay" }
        return $resp.Content
    } catch {
        return $null
    }
}

$scriptDir = Split-Path -Parent $PSCommandPath
if (-not [System.IO.Path]::IsPathRooted($InputJsonPath)) {
    $InputJsonPath = Join-Path $scriptDir $InputJsonPath
}
if (-not [System.IO.Path]::IsPathRooted($OutputCsvPath)) {
    $OutputCsvPath = Join-Path $scriptDir $OutputCsvPath
}

$synthesisKey = ([char]0xD569).ToString() + ([char]0xC131).ToString()
$requirementKey = ([char]0xC870).ToString() + ([char]0xAC74).ToString()

$raw = $null
$sourceLabel = ""
$parsed = $null

if (-not $ForceRemote -and (Test-Path $InputJsonPath)) {
    $raw = Get-Content -Raw -Path $InputJsonPath
    $sourceLabel = $InputJsonPath
    $parsed = Try-ParseJson -Text $raw
} else {
    $urlCandidates = @(
        $InputJsonUrl,
        "https://github.com/TWHome-Git/TWHomeDB/raw/main/EquipmentData.json",
        "https://raw.githubusercontent.com/TWHome-Git/TWHomeDB/main/EquipmentData_new.json"
    )

    foreach ($u in $urlCandidates) {
        $raw = Try-FetchRemoteJsonText -Url $u
        $parsed = Try-ParseJson -Text $raw
        if ($null -ne $parsed) {
            $sourceLabel = $u
            break
        }
    }

    if ($null -eq $parsed) {
        try {
            $apiUrl = "https://api.github.com/repos/TWHome-Git/TWHomeDB/contents/EquipmentData.json"
            $apiText = Try-FetchRemoteJsonText -Url $apiUrl
            $apiObj = Try-ParseJson -Text $apiText
            if ($null -ne $apiObj -and $apiObj.content) {
                $b64 = ([string]$apiObj.content).Replace("`r", "").Replace("`n", "")
                $bytes = [System.Convert]::FromBase64String($b64)
                $raw = [System.Text.Encoding]::UTF8.GetString($bytes)
                $parsed = Try-ParseJson -Text $raw
                if ($null -ne $parsed) {
                    $sourceLabel = $apiUrl
                }
            }
        } catch {
        }
    }
}

if ($null -eq $parsed -and (Test-Path $InputJsonPath)) {
    $raw = Get-Content -Raw -Path $InputJsonPath
    $parsed = Try-ParseJson -Text $raw
    if ($null -ne $parsed) {
        $sourceLabel = $InputJsonPath
    }
}

if ($null -eq $parsed) {
    $preview = ""
    if ($raw) {
        $trimmed = $raw.Trim()
        if ($trimmed.Length -gt 120) { $preview = $trimmed.Substring(0, 120) } else { $preview = $trimmed }
    }
    throw "Failed to parse JSON. Source=$sourceLabel Preview=$preview"
}

$items = @()
if ($parsed -is [System.Collections.IEnumerable] -and -not ($parsed -is [string])) {
    if ($parsed.PSObject.Properties.Name -contains "items") {
        $items = @($parsed.items)
    } else {
        $items = @($parsed)
    }
}

if ($items.Count -eq 0) {
    throw "No equipment rows found in: $InputJsonPath"
}

$rows = foreach ($it in $items) {
    $characters = @()
    if ($it.characters) { $characters = @($it.characters) }

    $synthesisValue = ""
    if ($it.PSObject.Properties.Name -contains $synthesisKey) {
        $synthesisValue = [string]$it.$synthesisKey
    }

    $requirementValue = ""
    if ($it.PSObject.Properties.Name -contains $requirementKey) {
        $requirementValue = [string]$it.$requirementKey
    }

    [PSCustomObject][ordered]@{
        id             = [int]$it.id
        major_category = [string]$it.major_category
        sub_category   = [string]$it.sub_category
        name           = [string]$it.name
        image          = [string]$it.image
        synthesis      = $synthesisValue
        requirement    = $requirementValue
        attack_type    = [string]$it.attack_type
        characters     = ($characters -join "|")
        stats_json     = ($it.stats | ConvertTo-Json -Depth 20 -Compress)
    }
}

$rows = @($rows | Sort-Object @{ Expression = { [int]$_.id } }, @{ Expression = { $_.name } })

$csv = $rows | ConvertTo-Csv -NoTypeInformation
Write-Utf8BomText -Path $OutputCsvPath -Content (($csv -join [Environment]::NewLine))

Write-Host "Exported $($rows.Count) rows"
Write-Host "Input: $sourceLabel"
Write-Host "CSV: $OutputCsvPath"
