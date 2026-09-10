[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InputPath,
    [Parameter(Mandatory)]
    [string]$OutputPath,
    [string]$InventoryPath,
    [string]$Source,
    [string]$MoodleVersion,
    [switch]$RequireComplete
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    throw "Moodle contract manifest preparation failed: $Message"
}

function Get-PropertyValue($Object, [string]$Name) {
    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties |
        Where-Object { $_.Name -ieq $Name } |
        Select-Object -First 1
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Get-StringValue($Object, [string]$Name) {
    $value = Get-PropertyValue $Object $Name
    if ($null -eq $value) {
        return $null
    }

    $text = [string]$value
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $null
    }

    return $text.Trim()
}

function Get-JsonElementProperty(
    [System.Text.Json.JsonElement]$Object,
    [string]$Name) {
    foreach ($property in $Object.EnumerateObject()) {
        if ($property.Name -ieq $Name) {
            return $property.Value
        }
    }

    return $null
}

function Get-JsonCanonicalProperty(
    [System.Text.Json.JsonElement]$Object,
    [string]$Name) {
    $property = Get-JsonElementProperty $Object $Name
    if ($null -eq $property) {
        return $null
    }

    return [System.Text.Json.Nodes.JsonNode]::Parse($property.GetRawText()).ToJsonString()
}

function Get-JsonStringProperty(
    [System.Text.Json.JsonElement]$Object,
    [string]$Name) {
    $property = Get-JsonElementProperty $Object $Name
    if ($null -eq $property -or $property.ValueKind -ne [System.Text.Json.JsonValueKind]::String) {
        return $null
    }

    $value = $property.GetString()
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $null
    }

    return $value.Trim()
}

function Convert-ToJsonString([object]$Value) {
    if ($null -eq $Value) {
        return 'null'
    }

    return ConvertTo-Json -InputObject $Value -Depth 100 -Compress
}

function Get-StringArray([object]$Value) {
    if ($null -eq $Value) {
        return @()
    }

    if ($Value -is [string]) {
        return @([string]$Value)
    }

    return @($Value | ForEach-Object {
        $text = [string]$_
        if (-not [string]::IsNullOrWhiteSpace($text)) {
            $text.Trim()
        }
    })
}

function New-CanonicalPagination([object]$Pagination) {
    if ($null -eq $Pagination) {
        return $null
    }

    return [pscustomobject][ordered]@{
        Mode = (Get-StringValue $Pagination 'mode') ?? 'none'
        OffsetParameter = Get-StringValue $Pagination 'offsetParameter'
        LimitParameter = Get-StringValue $Pagination 'limitParameter'
        PageParameter = Get-StringValue $Pagination 'pageParameter'
        PageSizeParameter = Get-StringValue $Pagination 'pageSizeParameter'
        CursorParameter = Get-StringValue $Pagination 'cursorParameter'
        HasMoreProperty = Get-StringValue $Pagination 'hasMoreProperty'
        TotalProperty = Get-StringValue $Pagination 'totalProperty'
        NextCursorProperty = Get-StringValue $Pagination 'nextCursorProperty'
    }
}

function New-CanonicalFileHandling([object]$FileHandling) {
    if ($null -eq $FileHandling) {
        return $null
    }

    $acceptsFiles = Get-PropertyValue $FileHandling 'acceptsFiles'
    $returnsFiles = Get-PropertyValue $FileHandling 'returnsFiles'
    $allowedMimeTypesProperty = Get-PropertyValue $FileHandling 'allowedMimeTypes'
    $allowedMimeTypes = if ($null -eq $allowedMimeTypesProperty) {
        $null
    } else {
        [object[]]@(Get-StringArray $allowedMimeTypesProperty)
    }
    return [pscustomobject][ordered]@{
        AcceptsFiles = if ($null -eq $acceptsFiles) { $false } else { [bool]$acceptsFiles }
        ReturnsFiles = if ($null -eq $returnsFiles) { $false } else { [bool]$returnsFiles }
        Destination = Get-StringValue $FileHandling 'destination'
        AllowedMimeTypes = $allowedMimeTypes
    }
}

function Get-ContractHash(
    [System.Text.Json.JsonElement]$Contract,
    [string]$FunctionName,
    [string]$Effect,
    [string]$MoodleRelease,
    [string]$ExternalFunctionVersion,
    [string]$Component,
    [string]$PluginVersion,
    [string]$ContractSource,
    [string[]]$RequiredScopes,
    [object]$MoodleCapabilities,
    [string]$PlatformPermission,
    [object]$Pagination,
    [object]$FileHandling,
    [bool]$AdministrativeOnly) {
    $canonicalParts = [System.Collections.Generic.List[string]]::new()
    $canonicalParts.Add($FunctionName.Trim().ToLowerInvariant())
    $canonicalParts.Add($Effect.Trim().ToLowerInvariant())
    $canonicalParts.Add((Get-JsonCanonicalProperty $Contract 'inputSchema'))
    $canonicalParts.Add((Get-JsonCanonicalProperty $Contract 'outputSchema'))
    $canonicalParts.Add($MoodleRelease ?? '')
    if (-not [string]::IsNullOrWhiteSpace($ExternalFunctionVersion)) {
        $canonicalParts.Add($ExternalFunctionVersion.Trim())
    }
    $canonicalParts.Add($Component ?? '')
    $canonicalParts.Add($PluginVersion ?? '')
    $canonicalParts.Add($ContractSource ?? '')
    $scopeValues = @(Get-StringArray $RequiredScopes)
    $scopeJson = if ($scopeValues.Count -eq 0) { '[]' } else { Convert-ToJsonString $scopeValues }
    $canonicalParts.Add($scopeJson)
    if ($null -ne $MoodleCapabilities) {
        $canonicalParts.Add((Convert-ToJsonString @(Get-StringArray $MoodleCapabilities)))
    }
    $canonicalParts.Add($PlatformPermission ?? '')
    $canonicalParts.Add((Convert-ToJsonString $Pagination))
    $canonicalParts.Add((Convert-ToJsonString $FileHandling))
    if ($AdministrativeOnly) {
        $canonicalParts.Add('administrativeOnly:true')
    }

    $canonical = [string]::Join("`n", $canonicalParts)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($canonical)
    return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Set-OrAddProperty($Object, [string]$Name, [object]$Value) {
    $property = $Object.PSObject.Properties |
        Where-Object { $_.Name -ieq $Name } |
        Select-Object -First 1
    if ($null -ne $property) {
        $property.Value = $Value
    } else {
        $Object | Add-Member -MemberType NoteProperty -Name $Name -Value $Value
    }
}

function Get-RequiredString($Object, [string]$Name, [string]$Path) {
    $value = Get-StringValue $Object $Name
    if ([string]::IsNullOrWhiteSpace($value)) {
        Fail "$Path requires '$Name'."
    }

    return $value
}

if ($RequireComplete -and [string]::IsNullOrWhiteSpace($InventoryPath)) {
    Fail '-RequireComplete requires -InventoryPath.'
}

$resolvedInputPath = (Resolve-Path -LiteralPath $InputPath -ErrorAction Stop).Path
try {
    $inputRoot = Get-Content -LiteralPath $resolvedInputPath -Raw -Encoding utf8 | ConvertFrom-Json
}
catch {
    Fail "the input is not valid JSON: $InputPath"
}

$inputContracts = @(Get-PropertyValue $inputRoot 'contracts')
if ($null -eq $inputRoot -or $inputContracts.Count -eq 0 -and $null -eq (Get-PropertyValue $inputRoot 'contracts')) {
    Fail "the input must contain an array named 'contracts'."
}

$rootMoodleVersion = if ([string]::IsNullOrWhiteSpace($MoodleVersion)) {
    Get-StringValue $inputRoot 'moodleVersion'
} else {
    $MoodleVersion.Trim()
}
$rootSource = if ([string]::IsNullOrWhiteSpace($Source)) {
    Get-StringValue $inputRoot 'source'
} else {
    $Source.Trim()
}

$outputContracts = [System.Collections.Generic.List[object]]::new()
$seenIdentities = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$contractIndex = 0
foreach ($inputContract in $inputContracts) {
    $contractIndex++
    $path = "contracts[$($contractIndex - 1)]"
    if ($null -eq $inputContract) {
        Fail "$path cannot be null."
    }

    $functionName = Get-RequiredString $inputContract 'functionName' $path
    $effect = Get-RequiredString $inputContract 'effect' $path
    if ($effect.ToLowerInvariant() -notin @('read', 'write', 'unknown')) {
        Fail "$path.effect must be read, write, or unknown."
    }

    $inputSchema = Get-PropertyValue $inputContract 'inputSchema'
    $outputSchema = Get-PropertyValue $inputContract 'outputSchema'
    if ($null -eq $inputSchema -or $inputSchema -isnot [pscustomobject]) {
        Fail "$path.inputSchema must be a JSON object."
    }
    if ($null -eq $outputSchema -or $outputSchema -isnot [pscustomobject]) {
        Fail "$path.outputSchema must be a JSON object."
    }

    $contractMoodleVersion = Get-StringValue $inputContract 'moodleVersion'
    if ([string]::IsNullOrWhiteSpace($contractMoodleVersion)) {
        $contractMoodleVersion = $rootMoodleVersion
    }
    $externalFunctionVersion = Get-StringValue $inputContract 'externalFunctionVersion'
    $component = Get-StringValue $inputContract 'component'
    $pluginVersion = Get-StringValue $inputContract 'pluginVersion'
    $contractSource = Get-StringValue $inputContract 'source'
    if ([string]::IsNullOrWhiteSpace($contractSource)) {
        $contractSource = $rootSource
    }
    $requiredScopes = @(Get-StringArray (Get-PropertyValue $inputContract 'requiredScopes'))
    $platformPermission = Get-StringValue $inputContract 'platformPermission'
    $administrativeOnlyValue = Get-PropertyValue $inputContract 'administrativeOnly'
    $administrativeOnly = if ($null -eq $administrativeOnlyValue) { $false } else { [bool]$administrativeOnlyValue }
    if ($administrativeOnly -and [string]::IsNullOrWhiteSpace($platformPermission)) {
        Fail "$path marked administrativeOnly=true must declare platformPermission."
    }

    $status = Get-StringValue $inputContract 'status'
    if ([string]::IsNullOrWhiteSpace($status)) {
        $status = 'missing'
    }
    if ($status.ToLowerInvariant() -notin @('verified', 'missing', 'stale', 'conflicting', 'invalid')) {
        Fail "$path.status is not a supported contract status."
    }

    $identity = "{0}|{1}|{2}" -f $functionName.ToLowerInvariant(), ($contractMoodleVersion ?? ''), ($externalFunctionVersion ?? '')
    if (-not $seenIdentities.Add($identity)) {
        Fail "duplicate contract identity: $identity"
    }

    $outputContractValues = [ordered]@{}
    foreach ($property in $inputContract.PSObject.Properties) {
        $outputContractValues[$property.Name] = $property.Value
    }
    $outputContract = [pscustomobject]$outputContractValues
    Set-OrAddProperty $outputContract 'functionName' $functionName
    Set-OrAddProperty $outputContract 'effect' $effect.ToLowerInvariant()
    Set-OrAddProperty $outputContract 'moodleVersion' $contractMoodleVersion
    Set-OrAddProperty $outputContract 'externalFunctionVersion' $externalFunctionVersion
    Set-OrAddProperty $outputContract 'component' $component
    Set-OrAddProperty $outputContract 'pluginVersion' $pluginVersion
    Set-OrAddProperty $outputContract 'source' $contractSource
    $requiredScopesValue = [object[]]$requiredScopes
    Set-OrAddProperty $outputContract 'requiredScopes' $requiredScopesValue
    Set-OrAddProperty $outputContract 'platformPermission' $platformPermission
    Set-OrAddProperty $outputContract 'administrativeOnly' $administrativeOnly
    Set-OrAddProperty $outputContract 'status' $status.ToLowerInvariant()
    Set-OrAddProperty $outputContract 'contractHash' ''
    $outputContracts.Add($outputContract)
}

$outputRoot = [pscustomobject][ordered]@{
    schemaVersion = 1
    source = $rootSource
    moodleVersion = $rootMoodleVersion
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    contracts = @($outputContracts)
}

# Serialize once to obtain the exact schema text that runtime will read. The
# runtime hash is deliberately based on JsonElement.GetRawText().
$candidateJson = $outputRoot | ConvertTo-Json -Depth 100 -Compress
$candidateDocument = [System.Text.Json.JsonDocument]::Parse($candidateJson)
try {
    $contractElements = (Get-JsonElementProperty $candidateDocument.RootElement 'contracts').EnumerateArray()
    $contractIndex = 0
    foreach ($contractElement in $contractElements) {
        $contractIndex++
        $functionName = Get-JsonStringProperty $contractElement 'functionName'
        $effect = Get-JsonStringProperty $contractElement 'effect'
        $moodleRelease = Get-JsonStringProperty $contractElement 'moodleVersion'
        $externalFunctionVersion = Get-JsonStringProperty $contractElement 'externalFunctionVersion'
        $component = Get-JsonStringProperty $contractElement 'component'
        $pluginVersion = Get-JsonStringProperty $contractElement 'pluginVersion'
        $contractSource = Get-JsonStringProperty $contractElement 'source'
        $platformPermission = Get-JsonStringProperty $contractElement 'platformPermission'
        $requiredScopesElement = Get-JsonElementProperty $contractElement 'requiredScopes'
        $requiredScopes = if ($null -eq $requiredScopesElement -or $requiredScopesElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Array) {
            @()
        } else {
            @($requiredScopesElement.EnumerateArray() | Where-Object ValueKind -eq String | ForEach-Object GetString)
        }
        $moodleCapabilitiesElement = Get-JsonElementProperty $contractElement 'moodleCapabilities'
        $moodleCapabilities = if ($null -eq $moodleCapabilitiesElement -or $moodleCapabilitiesElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Array) {
            $null
        } else {
            @($moodleCapabilitiesElement.EnumerateArray() | Where-Object ValueKind -eq String | ForEach-Object GetString)
        }
        $administrativeElement = Get-JsonElementProperty $contractElement 'administrativeOnly'
        $administrativeOnly = $null -ne $administrativeElement -and $administrativeElement.ValueKind -eq [System.Text.Json.JsonValueKind]::True
        $paginationObject = Get-PropertyValue $outputContracts[$contractIndex - 1] 'pagination'
        $fileHandlingObject = Get-PropertyValue $outputContracts[$contractIndex - 1] 'fileHandling'
        $canonicalPagination = New-CanonicalPagination $paginationObject
        $canonicalFileHandling = New-CanonicalFileHandling $fileHandlingObject
        $hash = Get-ContractHash $contractElement $functionName $effect $moodleRelease $externalFunctionVersion $component $pluginVersion $contractSource $requiredScopes $moodleCapabilities $platformPermission $canonicalPagination $canonicalFileHandling $administrativeOnly
        Set-OrAddProperty $outputContracts[$contractIndex - 1] 'contractHash' $hash
    }
}
finally {
    $candidateDocument.Dispose()
}

$outputRoot.contracts = @($outputContracts)
$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}
$outputRoot | ConvertTo-Json -Depth 100 -Compress | Set-Content -LiteralPath $resolvedOutputPath -Encoding utf8

Write-Host "Prepared Moodle contract manifest: $resolvedOutputPath"
Write-Host "Contracts prepared: $($outputContracts.Count)"

if (-not [string]::IsNullOrWhiteSpace($InventoryPath)) {
    & (Join-Path $PSScriptRoot 'validate-moodle-contract-manifest.ps1') `
        -ManifestPath $resolvedOutputPath `
        -InventoryPath $InventoryPath `
        -RequireComplete:$RequireComplete
}
