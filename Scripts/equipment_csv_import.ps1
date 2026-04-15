param(
    [string]$InputCsvPath = ".\EquipmentData_edit.csv",
    [string]$OutputJsonPath = ".\EquipmentData.json"
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

function To-IntSafe {
    param([string]$Value)
    $n = 0
    $text = ""
    if ($null -ne $Value) { $text = $Value.Trim() }
    if ([int]::TryParse($text, [ref]$n)) { return $n }
    return 0
}

$scriptDir = Split-Path -Parent $PSCommandPath
if (-not [System.IO.Path]::IsPathRooted($InputCsvPath)) {
    $InputCsvPath = Join-Path $scriptDir $InputCsvPath
}
if (-not [System.IO.Path]::IsPathRooted($OutputJsonPath)) {
    $OutputJsonPath = Join-Path $scriptDir $OutputJsonPath
}

if (-not (Test-Path $InputCsvPath)) {
    throw "Input CSV not found: $InputCsvPath"
}

$rows = Import-Csv -Path $InputCsvPath -Encoding UTF8
if (-not $rows -or $rows.Count -eq 0) {
    throw "No rows in CSV: $InputCsvPath"
}

$synthesisKey = ([char]0xD569).ToString() + ([char]0xC131).ToString()
$requirementKey = ([char]0xC870).ToString() + ([char]0xAC74).ToString()

$rowIndex = 0
$items = foreach ($r in $rows) {
    $rowIndex++

    $characters = @()
    if (-not [string]::IsNullOrWhiteSpace($r.characters)) {
        $parts = $r.characters -split '\|'
        foreach ($p in $parts) {
            $v = $p.Trim()
            if (-not [string]::IsNullOrWhiteSpace($v)) {
                $characters += $v
            }
        }
    }

    $stats = @{}
    if (-not [string]::IsNullOrWhiteSpace($r.stats_json)) {
        try {
            $stats = $r.stats_json | ConvertFrom-Json
        } catch {
            throw "Invalid stats_json for id=$($r.id), name=$($r.name)"
        }
    }

    $obj = [ordered]@{}
    $obj["__row_index"] = $rowIndex
    $obj["id"] = To-IntSafe $r.id
    $obj["major_category"] = [string]$r.major_category
    $obj["sub_category"] = [string]$r.sub_category
    $obj["name"] = [string]$r.name
    $obj["image"] = [string]$r.image
    $obj["stats"] = $stats
    $obj[$synthesisKey] = [string]$r.synthesis
    $obj[$requirementKey] = [string]$r.requirement
    $obj["characters"] = $characters
    $obj["attack_type"] = [string]$r.attack_type
    $obj
}

$sortedItems = $items | Sort-Object @{ Expression = { [int]$_.id } }, @{ Expression = { [int]$_.__row_index } }
$cleaned = foreach ($it in $sortedItems) {
    $it.Remove("__row_index") | Out-Null
    $it
}

$json = @($cleaned) | ConvertTo-Json -Depth 30
Write-Utf8BomText -Path $OutputJsonPath -Content $json

Write-Host "Imported $(@($cleaned).Count) rows"
Write-Host "Input: $InputCsvPath"
Write-Host "JSON: $OutputJsonPath"
