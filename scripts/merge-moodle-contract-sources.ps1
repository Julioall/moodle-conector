[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string[]]$InputPath,
    [Parameter(Mandatory)]
    [string]$OutputPath,
    [string]$Source = 'controlled-export://merged-moodle-contract-sources'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    throw "Moodle contract source merge failed: $Message"
}

function Get-OptionalString($Object, [string]$Name) {
    $property = $Object.PSObject.Properties |
        Where-Object { $_.Name -ieq $Name } |
        Select-Object -First 1
    if ($null -eq $property -or $null -eq $property.Value) {
        return ''
    }

    return ([string]$property.Value).Trim()
}

if ($InputPath.Count -eq 0) {
    Fail 'at least one input source is required.'
}

$inputPaths = @($InputPath |
    ForEach-Object { $_ -split ',' } |
    ForEach-Object { $_.Trim() } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($inputPaths.Count -eq 0) {
    Fail 'at least one non-empty input source is required.'
}

$contracts = [System.Collections.Generic.List[object]]::new()
$identities = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$moodleVersions = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

foreach ($input in $inputPaths) {
    $resolvedInput = (Resolve-Path -LiteralPath $input -ErrorAction Stop).Path
    try {
        $document = Get-Content -LiteralPath $resolvedInput -Raw -Encoding utf8 | ConvertFrom-Json -Depth 100
    }
    catch {
        Fail "input '$input' is not valid JSON."
    }

    $schemaProperty = if ($null -eq $document) { $null } else { $document.PSObject.Properties['schemaVersion'] }
    $contractsProperty = if ($null -eq $document) { $null } else { $document.PSObject.Properties['contracts'] }
    if ($null -eq $document -or
        $null -eq $schemaProperty -or
        $schemaProperty.Value -ne 1 -or
        $null -eq $contractsProperty) {
        Fail "input '$input' must declare schemaVersion=1 and contain contracts."
    }

    $rootMoodleVersion = Get-OptionalString $document 'moodleVersion'
    if (-not [string]::IsNullOrWhiteSpace($rootMoodleVersion)) {
        $null = $moodleVersions.Add($rootMoodleVersion)
    }

    foreach ($contract in @($contractsProperty.Value)) {
        if ($null -eq $contract) {
            Fail "input '$input' contains a contract without functionName."
        }

        $functionName = Get-OptionalString $contract 'functionName'
        if ([string]::IsNullOrWhiteSpace($functionName)) {
            Fail "input '$input' contains a contract without functionName."
        }

        if (-not [string]::IsNullOrWhiteSpace($rootMoodleVersion) -and
            $null -eq $contract.PSObject.Properties['moodleVersion']) {
            $contract | Add-Member -NotePropertyName moodleVersion -NotePropertyValue $rootMoodleVersion
        }

        $moodleVersion = Get-OptionalString $contract 'moodleVersion'
        $externalVersion = Get-OptionalString $contract 'externalFunctionVersion'
        $component = Get-OptionalString $contract 'component'
        $pluginVersion = Get-OptionalString $contract 'pluginVersion'
        $identity = '{0}|{1}|{2}|{3}|{4}' -f `
            $functionName.ToLowerInvariant(),
            $moodleVersion,
            $externalVersion,
            $component,
            $pluginVersion

        if (-not $identities.Add($identity)) {
            Fail "conflicting or duplicate contract identity '$functionName' from '$input'. Resolve it before merging."
        }

        if (-not [string]::IsNullOrWhiteSpace($moodleVersion)) {
            $null = $moodleVersions.Add($moodleVersion)
        }
        $contracts.Add($contract)
    }
}

if ($contracts.Count -eq 0) {
    Fail 'the merged source contains no contracts.'
}

$orderedContracts = @($contracts | Sort-Object `
    @{ Expression = { Get-OptionalString $_ 'functionName' }; Ascending = $true }, `
    @{ Expression = { Get-OptionalString $_ 'moodleVersion' }; Ascending = $true }, `
    @{ Expression = { Get-OptionalString $_ 'externalFunctionVersion' }; Ascending = $true }, `
    @{ Expression = { Get-OptionalString $_ 'component' }; Ascending = $true })

$output = [ordered]@{
    schemaVersion = 1
    source = $Source
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    moodleVersions = @($moodleVersions | Sort-Object)
    contracts = $orderedContracts
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutput
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$temporaryOutput = "$resolvedOutput.$([Guid]::NewGuid().ToString('N')).tmp"
try {
    $output | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $temporaryOutput -Encoding utf8
    Move-Item -LiteralPath $temporaryOutput -Destination $resolvedOutput -Force
}
finally {
    if (Test-Path -LiteralPath $temporaryOutput) {
        Remove-Item -LiteralPath $temporaryOutput -Force
    }
}

Write-Host "Merged Moodle contract sources: $resolvedOutput"
Write-Host "Contracts merged: $($orderedContracts.Count); releases: $($moodleVersions.Count)"
