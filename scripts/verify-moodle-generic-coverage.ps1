[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Alias,
    [string]$BaseUrl,
    [string]$ManifestPath,
    [string]$InventoryPath,
    [string]$GapReportPath,
    [switch]$RequireCompleteManifest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    throw "Moodle generic coverage verification failed: $Message"
}

function Normalize-MoodleRelease([string]$Release) {
    if ([string]::IsNullOrWhiteSpace($Release)) {
        return ''
    }

    $match = [regex]::Match($Release.Trim(), '(?<!\d)(\d+\.\d+\.\d+)(?!\d)')
    if ($match.Success) {
        return $match.Groups[1].Value
    }

    return $Release.Trim()
}

function Test-MoodleReleaseCompatible([string]$ContractRelease, [string]$DiscoveredRelease) {
    if ([string]::IsNullOrWhiteSpace($ContractRelease)) {
        return $true
    }

    return [string]::Equals(
        (Normalize-MoodleRelease $ContractRelease),
        (Normalize-MoodleRelease $DiscoveredRelease),
        [StringComparison]::OrdinalIgnoreCase)
}

if ($Alias -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$') {
    Fail 'Alias must contain only letters, numbers, hyphens or underscores and be at most 32 characters.'
}
$prefix = $Alias.ToUpperInvariant()
if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    $BaseUrl = [Environment]::GetEnvironmentVariable("LIVE_${prefix}_URL")
}
$accessToken = [Environment]::GetEnvironmentVariable("LIVE_${prefix}_TOKEN")
$username = [Environment]::GetEnvironmentVariable("LIVE_${prefix}_USERNAME")
$password = [Environment]::GetEnvironmentVariable("LIVE_${prefix}_PASSWORD")

if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    Fail "configure LIVE_${prefix}_URL (or pass -BaseUrl)."
}

if ([string]::IsNullOrWhiteSpace($accessToken) -and
    ([string]::IsNullOrWhiteSpace($username) -or [string]::IsNullOrWhiteSpace($password))) {
    Fail "configure LIVE_${prefix}_TOKEN or LIVE_${prefix}_USERNAME and LIVE_${prefix}_PASSWORD."
}

if ($RequireCompleteManifest -and [string]::IsNullOrWhiteSpace($ManifestPath)) {
    Fail '-RequireCompleteManifest requires -ManifestPath.'
}

try {
    $baseUri = [Uri]($BaseUrl.TrimEnd('/'))
}
catch {
    Fail 'the Moodle base URL is invalid.'
}

if ($baseUri.Scheme -ne 'https' -and $baseUri.Host -notin @('localhost', '127.0.0.1', '::1')) {
    Fail 'the Moodle base URL must use HTTPS outside local development.'
}

if ([string]::IsNullOrWhiteSpace($accessToken)) {
    $tokenQuery = @{
        username = $username
        password = $password
        service  = 'moodle_mobile_app'
    }
    $tokenUri = "$($baseUri.AbsoluteUri.TrimEnd('/'))/login/token.php?" +
        (($tokenQuery.GetEnumerator() | ForEach-Object {
            "{0}={1}" -f [Uri]::EscapeDataString([string]$_.Key), [Uri]::EscapeDataString([string]$_.Value)
        }) -join '&')

    try {
        $tokenResponse = Invoke-RestMethod -Uri $tokenUri -Method Get
    }
    catch {
        Fail 'the Moodle token endpoint could not be reached.'
    }

    $accessToken = [string]$tokenResponse.token
    if ([string]::IsNullOrWhiteSpace($accessToken)) {
        Fail 'Moodle did not return an access token for the configured test account.'
    }
}

$siteInfoQuery = @{
    wstoken            = $accessToken
    wsfunction         = 'core_webservice_get_site_info'
    moodlewsrestformat = 'json'
}
$siteInfoUri = "$($baseUri.AbsoluteUri.TrimEnd('/'))/webservice/rest/server.php?" +
    (($siteInfoQuery.GetEnumerator() | ForEach-Object {
        "{0}={1}" -f [Uri]::EscapeDataString([string]$_.Key), [Uri]::EscapeDataString([string]$_.Value)
    }) -join '&')

try {
    $siteInfo = Invoke-RestMethod -Uri $siteInfoUri -Method Get
}
catch {
    Fail 'core_webservice_get_site_info could not be queried.'
}

$functionEntries = @($siteInfo.functions) |
    Where-Object { $null -ne $_ -and -not [string]::IsNullOrWhiteSpace([string]$_.name) } |
    ForEach-Object {
        [pscustomobject]@{
            name    = [string]$_.name.Trim()
            version = if ([string]::IsNullOrWhiteSpace([string]$_.version)) { $null } else { [string]$_.version.Trim() }
        }
    } |
    Sort-Object name -Unique

$functions = @($functionEntries | ForEach-Object { $_.name })

if ($functions.Count -eq 0 -or $functions -notcontains 'core_webservice_get_site_info') {
    Fail 'the token returned no usable External Functions inventory.'
}

$manifestContracts = @()
if (-not [string]::IsNullOrWhiteSpace($ManifestPath)) {
    $resolvedManifestPath = (Resolve-Path -LiteralPath $ManifestPath -ErrorAction Stop).Path
    try {
        $manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw -Encoding utf8 | ConvertFrom-Json
    }
    catch {
        Fail 'the configured contract manifest is not valid JSON.'
    }

    if ($null -eq $manifest -or $manifest.schemaVersion -ne 1 -or $null -eq $manifest.contracts) {
        Fail 'the contract manifest must declare schemaVersion=1 and contain contracts.'
    }

    $manifestContracts = @($manifest.contracts)
}

$release = [string]$siteInfo.release
$functionVersions = @{}
foreach ($entry in $functionEntries) {
    $functionVersions[[string]$entry.name] = [string]$entry.version
}

$compatibleVerified = @($manifestContracts | Where-Object {
    $functionName = [string]$_.functionName
    $requiredFunctionVersion = [string]$_.externalFunctionVersion
    ([string]$_.status).ToLowerInvariant() -eq 'verified' -and
    (Test-MoodleReleaseCompatible ([string]$_.moodleVersion) $release) -and
    ([string]::IsNullOrWhiteSpace($requiredFunctionVersion) -or
     ($functionVersions.ContainsKey($functionName) -and
      $functionVersions[$functionName] -eq $requiredFunctionVersion.Trim()))
})
$verifiedNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$policyBlockedNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

function Test-AdministrativeContract($Contract) {
    $property = $Contract.PSObject.Properties['administrativeOnly']
    return $null -ne $property -and [bool]$property.Value
}

foreach ($contract in $compatibleVerified) {
    if (-not [string]::IsNullOrWhiteSpace([string]$contract.functionName)) {
        $functionName = [string]$contract.functionName.Trim()
        $null = $verifiedNames.Add($functionName)
        if (Test-AdministrativeContract $contract) {
            $null = $policyBlockedNames.Add($functionName)
        }
    }
}

$missing = @($functions | Where-Object { -not $verifiedNames.Contains($_) })
$discoveredCount = @($functions).Count
$missingCount = @($missing).Count
$coveredNames = @($functions | Where-Object {
    $verifiedNames.Contains($_) -and -not $policyBlockedNames.Contains($_)
})
$coveredCount = @($coveredNames).Count
$policyBlockedCount = @($functions | Where-Object { $policyBlockedNames.Contains($_) }).Count
$executableDiscoveredCount = $discoveredCount - $policyBlockedCount
$coveragePercent = if ($executableDiscoveredCount -eq 0) {
    $null
}
else {
    [Math]::Round($coveredCount * 100 / $executableDiscoveredCount, 2)
}
$coverageDisplay = if ($null -eq $coveragePercent) { 'n/a' } else { "$coveragePercent%" }

$summary = [string]::Format(
    "Moodle generic coverage: alias={0}; release={1}; discovered={2}; verified-compatible-contracts={3}; covered={4}; policy-blocked={5}; missing={6}; executable-discovered={7}; coverage-executable={8}",
    [object[]]@($Alias, $release, $discoveredCount, $verifiedNames.Count, $coveredCount, $policyBlockedCount, $missingCount, $executableDiscoveredCount, $coverageDisplay))
Write-Host $summary

if (-not [string]::IsNullOrWhiteSpace($GapReportPath)) {
    $resolvedGapReportPath = [System.IO.Path]::GetFullPath($GapReportPath)
    $gapReportDirectory = Split-Path -Parent $resolvedGapReportPath
    if (-not [string]::IsNullOrWhiteSpace($gapReportDirectory)) {
        New-Item -ItemType Directory -Path $gapReportDirectory -Force | Out-Null
    }

    $gapItems = foreach ($entry in $functionEntries) {
        $functionName = [string]$entry.name
        $matchingContracts = @($manifestContracts | Where-Object {
            [string]$_.functionName -eq $functionName
        })
        $compatibleContract = @($compatibleVerified | Where-Object {
            [string]$_.functionName -eq $functionName
        } | Select-Object -First 1)
        $selectedContract = @($matchingContracts | Select-Object -First 1)

        $state = if ($verifiedNames.Contains($functionName)) {
            if ($policyBlockedNames.Contains($functionName)) { 'policy_blocked' } else { 'covered' }
        }
        elseif ($matchingContracts.Count -eq 0) {
            'missing_contract'
        }
        elseif (@($matchingContracts | Where-Object {
            ([string]$_.status).ToLowerInvariant() -ne 'verified'
        }).Count -gt 0) {
            'contract_not_verified'
        }
        else {
            'contract_incompatible'
        }

        [ordered]@{
            functionName            = $functionName
            externalFunctionVersion = if ([string]::IsNullOrWhiteSpace([string]$entry.version)) { $null } else { [string]$entry.version }
            state                    = $state
            manifestStatus           = if ($selectedContract.Count -eq 0) { $null } else { [string]$selectedContract[0].status }
            manifestMoodleVersion    = if ($selectedContract.Count -eq 0) { $null } else { [string]$selectedContract[0].moodleVersion }
            manifestContractVersion  = if ($selectedContract.Count -eq 0) { $null } else { [string]$selectedContract[0].externalFunctionVersion }
            administrativeOnly       = if ($compatibleContract.Count -eq 0) { $false } else { Test-AdministrativeContract $compatibleContract[0] }
        }
    }

    [ordered]@{
        schemaVersion             = 1
        reportType                = 'moodle-generic-coverage'
        alias                     = $Alias
        release                   = $release
        generatedAtUtc            = [DateTimeOffset]::UtcNow.ToString('O')
        discoveredCount           = $discoveredCount
        verifiedCompatibleCount   = $verifiedNames.Count
        coveredCount              = $coveredCount
        policyBlockedCount        = $policyBlockedCount
        missingCount              = $missingCount
        executableDiscoveredCount = $executableDiscoveredCount
        executableCoveragePercent = $coveragePercent
        functions                 = @($gapItems)
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resolvedGapReportPath -Encoding utf8
    Write-Host "Sanitized gap report written: $resolvedGapReportPath"
}

if (-not [string]::IsNullOrWhiteSpace($InventoryPath)) {
    $resolvedInventoryPath = [System.IO.Path]::GetFullPath($InventoryPath)
    $inventoryDirectory = Split-Path -Parent $resolvedInventoryPath
    if (-not [string]::IsNullOrWhiteSpace($inventoryDirectory)) {
        New-Item -ItemType Directory -Path $inventoryDirectory -Force | Out-Null
    }

    [ordered]@{
        schemaVersion = 1
        alias = $Alias
        release = $release
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        functions = @($functionEntries)
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resolvedInventoryPath -Encoding utf8
    Write-Host "Sanitized inventory written: $resolvedInventoryPath"

    if (-not [string]::IsNullOrWhiteSpace($ManifestPath)) {
        $validatorPath = Join-Path $PSScriptRoot 'validate-moodle-contract-manifest.ps1'
        if (-not (Test-Path -LiteralPath $validatorPath -PathType Leaf)) {
            Fail "the contract manifest validator was not found: $validatorPath"
        }

        try {
            if ($RequireCompleteManifest) {
                & $validatorPath `
                    -ManifestPath $resolvedManifestPath `
                    -InventoryPath $resolvedInventoryPath `
                    -RequireComplete
            }
            else {
                & $validatorPath `
                    -ManifestPath $resolvedManifestPath `
                    -InventoryPath $resolvedInventoryPath
            }
        }
        catch {
            Fail "contract manifest validation failed: $($_.Exception.Message)"
        }
    }
}

if ($RequireCompleteManifest -and $missingCount -gt 0) {
    $sample = ($missing | Select-Object -First 10) -join ', '
    Fail "$missingCount discovered function(s) have no verified compatible contract. First entries: $sample"
}

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    Write-Host 'No manifest supplied; inventory-only verification completed.'
}
else {
    Write-Host "Manifest verification completed: $resolvedManifestPath"
}
