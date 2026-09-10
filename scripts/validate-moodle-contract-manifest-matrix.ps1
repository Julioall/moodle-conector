[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ManifestPath,
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string[]]$InventoryPath,
    [switch]$RequireComplete
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    throw "Moodle contract manifest matrix validation failed: $Message"
}

if ($InventoryPath.Count -eq 0) {
    Fail 'at least one inventory is required.'
}

$inventoryPaths = @($InventoryPath |
    ForEach-Object { $_ -split ',' } |
    ForEach-Object { $_.Trim() } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($inventoryPaths.Count -eq 0) {
    Fail 'at least one non-empty inventory is required.'
}

$validatorPath = Join-Path $PSScriptRoot 'validate-moodle-contract-manifest.ps1'
if (-not (Test-Path -LiteralPath $validatorPath -PathType Leaf)) {
    Fail "the contract manifest validator was not found: $validatorPath"
}

$resolvedManifest = (Resolve-Path -LiteralPath $ManifestPath -ErrorAction Stop).Path
foreach ($inventory in $inventoryPaths) {
    $resolvedInventory = (Resolve-Path -LiteralPath $inventory -ErrorAction Stop).Path
    Write-Host "Validating manifest against inventory: $resolvedInventory"
    try {
        if ($RequireComplete) {
            & $validatorPath `
                -ManifestPath $resolvedManifest `
                -InventoryPath $resolvedInventory `
                -RequireComplete
        }
        else {
            & $validatorPath `
                -ManifestPath $resolvedManifest `
                -InventoryPath $resolvedInventory
        }
    }
    catch {
        Fail "validation failed for '$inventory': $($_.Exception.Message)"
    }
}

Write-Host "Moodle contract manifest matrix validation passed: $($inventoryPaths.Count) inventory(ies)."
