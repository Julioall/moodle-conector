[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]]$Alias,
    [Parameter(Mandatory)]
    [string]$ManifestPath,
    [string]$InventoryDirectory = './artifacts/moodle-contracts',
    [switch]$RequireCompleteManifest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    throw "Moodle generic coverage matrix verification failed: $Message"
}

if ($Alias.Count -eq 0) {
    Fail 'at least one Moodle alias is required.'
}

foreach ($currentAlias in $Alias) {
    if ($currentAlias -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$') {
        Fail "invalid Moodle alias '$currentAlias'."
    }
}

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    Fail "the contract manifest was not found: $ManifestPath"
}

$gatePath = Join-Path $PSScriptRoot 'verify-moodle-generic-coverage.ps1'
$matrixPath = Join-Path $PSScriptRoot 'validate-moodle-contract-manifest-matrix.ps1'
if (-not (Test-Path -LiteralPath $gatePath -PathType Leaf)) {
    Fail "the per-alias coverage gate was not found: $gatePath"
}
if (-not (Test-Path -LiteralPath $matrixPath -PathType Leaf)) {
    Fail "the manifest matrix validator was not found: $matrixPath"
}

$resolvedInventoryDirectory = [System.IO.Path]::GetFullPath($InventoryDirectory)
New-Item -ItemType Directory -Path $resolvedInventoryDirectory -Force | Out-Null

$inventoryPaths = [System.Collections.Generic.List[string]]::new()
foreach ($currentAlias in ($Alias | ForEach-Object { $_.Trim().ToLowerInvariant() } | Sort-Object -Unique)) {
    $inventoryPath = Join-Path $resolvedInventoryDirectory "$currentAlias-inventory.json"
    Write-Host "Verifying Moodle generic coverage for alias: $currentAlias"

    # The per-alias gate reads LIVE_<ALIAS>_URL and either
    # LIVE_<ALIAS>_TOKEN or LIVE_<ALIAS>_USERNAME/PASSWORD. It writes only a
    # sanitized inventory containing release and function name/version pairs.
    & $gatePath `
        -Alias $currentAlias `
        -ManifestPath $ManifestPath `
        -InventoryPath $inventoryPath

    $inventoryPaths.Add($inventoryPath)
}

$matrixArguments = @{
    ManifestPath = $ManifestPath
    InventoryPath = $inventoryPaths.ToArray()
}
if ($RequireCompleteManifest) {
    $matrixArguments.RequireComplete = $true
}

& $matrixPath @matrixArguments
Write-Host "Moodle generic coverage matrix verification passed: $($inventoryPaths.Count) alias(es)."
