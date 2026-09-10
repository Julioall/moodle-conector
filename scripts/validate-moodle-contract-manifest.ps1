[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ManifestPath,
    [Parameter(Mandatory)]
    [string]$InventoryPath,
    [switch]$RequireComplete
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    throw "Moodle contract manifest validation failed: $Message"
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

function Test-MoodleReleaseCompatible([string]$ContractRelease, [string]$InventoryRelease) {
    if ([string]::IsNullOrWhiteSpace($ContractRelease)) {
        return $true
    }

    return [string]::Equals(
        (Normalize-MoodleRelease $ContractRelease),
        (Normalize-MoodleRelease $InventoryRelease),
        [StringComparison]::OrdinalIgnoreCase)
}

function Read-JsonDocument([string]$Path, [string]$Label) {
    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    try {
        return [System.Text.Json.JsonDocument]::Parse(
            [System.IO.File]::ReadAllText($resolved, [System.Text.Encoding]::UTF8))
    }
    catch {
        Fail "$Label is not valid JSON."
    }
}

function Get-Property([System.Text.Json.JsonElement]$Object, [string]$Name) {
    if ($Object.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
        return $null
    }

    foreach ($property in $Object.EnumerateObject()) {
        if ([string]::Equals($property.Name, $Name, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $property.Value
        }
    }

    return $null
}

function Get-CanonicalJsonProperty([System.Text.Json.JsonElement]$Object, [string]$Name) {
    $property = Get-Property $Object $Name
    if ($null -eq $property) {
        return $null
    }

    return [System.Text.Json.Nodes.JsonNode]::Parse($property.GetRawText()).ToJsonString()
}

function Get-StringProperty([System.Text.Json.JsonElement]$Object, [string]$Name) {
    $property = Get-Property $Object $Name
    if ($null -ne $property -and $property.ValueKind -eq [System.Text.Json.JsonValueKind]::String) {
        return $property.GetString()
    }

    return $null
}

function Get-IntProperty([System.Text.Json.JsonElement]$Object, [string]$Name) {
    $property = Get-Property $Object $Name
    $value = 0
    if ($null -ne $property -and $property.ValueKind -eq [System.Text.Json.JsonValueKind]::Number -and $property.TryGetInt32([ref]$value)) {
        return $value
    }

    return $null
}

function Add-Error([System.Collections.Generic.List[string]]$Errors, [string]$Message) {
    $Errors.Add($Message)
}

function Get-StringArrayProperty([System.Text.Json.JsonElement]$Object, [string]$Name) {
    $property = Get-Property $Object $Name
    if ($null -eq $property -or $property.ValueKind -ne [System.Text.Json.JsonValueKind]::Array) {
        return @()
    }

    $values = [System.Collections.Generic.List[string]]::new()
    foreach ($entry in $property.EnumerateArray()) {
        if ($entry.ValueKind -eq [System.Text.Json.JsonValueKind]::String -and
            -not [string]::IsNullOrWhiteSpace($entry.GetString())) {
            $values.Add($entry.GetString())
        }
    }

    return @($values)
}

function Validate-StringArrayProperty(
    [System.Text.Json.JsonElement]$Object,
    [string]$Name,
    [System.Collections.Generic.List[string]]$Errors,
    [string]$Label) {
    $property = Get-Property $Object $Name
    if ($null -eq $property) {
        return
    }

    if ($property.ValueKind -eq [System.Text.Json.JsonValueKind]::Null) {
        return
    }

    if ($property.ValueKind -ne [System.Text.Json.JsonValueKind]::Array) {
        Add-Error $Errors "$Label.$Name must be an array of non-empty strings."
        return
    }

    foreach ($entry in $property.EnumerateArray()) {
        if ($entry.ValueKind -ne [System.Text.Json.JsonValueKind]::String -or
            [string]::IsNullOrWhiteSpace($entry.GetString())) {
            Add-Error $Errors "$Label.$Name must contain only non-empty strings."
        }
    }
}

function Convert-ToJsonString([object]$Value) {
    if ($null -eq $Value) {
        return 'null'
    }

    return ConvertTo-Json -InputObject $Value -Depth 100 -Compress
}

function Get-CanonicalPagination([System.Text.Json.JsonElement]$Contract) {
    $pagination = Get-Property $Contract 'pagination'
    if ($null -eq $pagination -or $pagination.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
        return $null
    }

    $mode = Get-StringProperty $pagination 'mode'
    return [pscustomobject][ordered]@{
        Mode = if ([string]::IsNullOrWhiteSpace($mode)) { 'none' } else { $mode }
        OffsetParameter = Get-StringProperty $pagination 'offsetParameter'
        LimitParameter = Get-StringProperty $pagination 'limitParameter'
        PageParameter = Get-StringProperty $pagination 'pageParameter'
        PageSizeParameter = Get-StringProperty $pagination 'pageSizeParameter'
        CursorParameter = Get-StringProperty $pagination 'cursorParameter'
        HasMoreProperty = Get-StringProperty $pagination 'hasMoreProperty'
        TotalProperty = Get-StringProperty $pagination 'totalProperty'
        NextCursorProperty = Get-StringProperty $pagination 'nextCursorProperty'
    }
}

function Get-CanonicalFileHandling([System.Text.Json.JsonElement]$Contract) {
    $files = Get-Property $Contract 'fileHandling'
    if ($null -eq $files -or $files.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
        return $null
    }

    $acceptsFiles = Get-Property $files 'acceptsFiles'
    $returnsFiles = Get-Property $files 'returnsFiles'
    $allowedMimeTypes = Get-Property $files 'allowedMimeTypes'
    $mimeTypes = $null
    if ($null -ne $allowedMimeTypes -and $allowedMimeTypes.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        $mimeTypes = Get-StringArrayProperty $files 'allowedMimeTypes'
    }

    return [pscustomobject][ordered]@{
        AcceptsFiles = $null -ne $acceptsFiles -and
            $acceptsFiles.ValueKind -eq [System.Text.Json.JsonValueKind]::True
        ReturnsFiles = $null -ne $returnsFiles -and
            $returnsFiles.ValueKind -eq [System.Text.Json.JsonValueKind]::True
        Destination = Get-StringProperty $files 'destination'
        AllowedMimeTypes = $mimeTypes
    }
}

function Get-ContractHash([System.Text.Json.JsonElement]$Contract) {
    $functionName = Get-StringProperty $Contract 'functionName'
    $effect = Get-StringProperty $Contract 'effect'
    $moodleVersion = Get-StringProperty $Contract 'moodleVersion'
    $externalFunctionVersion = Get-StringProperty $Contract 'externalFunctionVersion'
    $component = Get-StringProperty $Contract 'component'
    $pluginVersion = Get-StringProperty $Contract 'pluginVersion'
    $source = Get-StringProperty $Contract 'source'
    $platformPermission = Get-StringProperty $Contract 'platformPermission'
    $administrativeOnly = Get-Property $Contract 'administrativeOnly'
    $isAdministrative = $null -ne $administrativeOnly -and
        $administrativeOnly.ValueKind -eq [System.Text.Json.JsonValueKind]::True

    $canonicalParts = [System.Collections.Generic.List[string]]::new()
    $canonicalParts.Add(([string]$functionName).Trim().ToLowerInvariant())
    $canonicalParts.Add(([string]$effect).Trim().ToLowerInvariant())
    $canonicalParts.Add((Get-CanonicalJsonProperty $Contract 'inputSchema'))
    $canonicalParts.Add((Get-CanonicalJsonProperty $Contract 'outputSchema'))
    $canonicalParts.Add($moodleVersion ?? '')
    if (-not [string]::IsNullOrWhiteSpace($externalFunctionVersion)) {
        $canonicalParts.Add($externalFunctionVersion.Trim())
    }
    $canonicalParts.Add($component ?? '')
    $canonicalParts.Add($pluginVersion ?? '')
    $canonicalParts.Add($source ?? '')
    $scopeValues = @(Get-StringArrayProperty $Contract 'requiredScopes')
    $scopeJson = if ($scopeValues.Count -eq 0) { '[]' } else { Convert-ToJsonString $scopeValues }
    $canonicalParts.Add($scopeJson)
    $moodleCapabilities = Get-Property $Contract 'moodleCapabilities'
    if ($null -ne $moodleCapabilities -and $moodleCapabilities.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        $canonicalParts.Add((Convert-ToJsonString @(Get-StringArrayProperty $Contract 'moodleCapabilities')))
    }
    $canonicalParts.Add($platformPermission ?? '')
    $canonicalParts.Add((Convert-ToJsonString (Get-CanonicalPagination $Contract)))
    $canonicalParts.Add((Convert-ToJsonString (Get-CanonicalFileHandling $Contract)))
    if ($isAdministrative) {
        $canonicalParts.Add('administrativeOnly:true')
    }

    $canonical = [string]::Join("`n", $canonicalParts)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($canonical)
    return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

$manifestDocument = Read-JsonDocument $ManifestPath 'The contract manifest'
$inventoryDocument = Read-JsonDocument $InventoryPath 'The inventory'
$errors = [System.Collections.Generic.List[string]]::new()

try {
    $manifestRoot = $manifestDocument.RootElement
    $inventoryRoot = $inventoryDocument.RootElement

    if ($manifestRoot.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
        Add-Error $errors 'The contract manifest root must be an object.'
    }
    if ($inventoryRoot.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
        Add-Error $errors 'The inventory root must be an object.'
    }

    if ($manifestRoot.ValueKind -eq [System.Text.Json.JsonValueKind]::Object -and
        (Get-IntProperty $manifestRoot 'schemaVersion') -ne 1) {
        Add-Error $errors 'The contract manifest must declare schemaVersion=1.'
    }
    if ($inventoryRoot.ValueKind -eq [System.Text.Json.JsonValueKind]::Object -and
        (Get-IntProperty $inventoryRoot 'schemaVersion') -ne 1) {
        Add-Error $errors 'The inventory must declare schemaVersion=1.'
    }

    $manifestContractsElement = Get-Property $manifestRoot 'contracts'
    $inventoryFunctionsElement = Get-Property $inventoryRoot 'functions'
    if ($null -eq $manifestContractsElement -or
        $manifestContractsElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Array) {
        Add-Error $errors 'The contract manifest must contain an array named contracts.'
    }
    if ($null -eq $inventoryFunctionsElement -or
        $inventoryFunctionsElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Array) {
        Add-Error $errors 'The inventory must contain an array named functions.'
    }

    $inventoryRelease = [string](Get-StringProperty $inventoryRoot 'release')
    $functionVersions = @{}
    $inventoryNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

    if ($null -ne $inventoryFunctionsElement -and
        $inventoryFunctionsElement.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        foreach ($function in $inventoryFunctionsElement.EnumerateArray()) {
            if ($function.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
                Add-Error $errors 'The inventory contains a non-object function entry.'
                continue
            }

            $name = Get-StringProperty $function 'name'
            if ([string]::IsNullOrWhiteSpace($name)) {
                Add-Error $errors 'The inventory contains a function without a name.'
                continue
            }

            if (-not $inventoryNames.Add($name.Trim())) {
                Add-Error $errors "The inventory contains duplicate function '$name'."
                continue
            }

            $functionVersions[$name.Trim()] = Get-StringProperty $function 'version'
        }
    }

    $verifiedCompatible = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $policyBlockedCompatible = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $contractKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $contractCount = 0

    if ($null -ne $manifestContractsElement -and
        $manifestContractsElement.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        foreach ($contract in $manifestContractsElement.EnumerateArray()) {
            $contractCount++
            if ($contract.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
                Add-Error $errors 'The contract manifest contains a non-object contract entry.'
                continue
            }

            $functionName = Get-StringProperty $contract 'functionName'
            $status = ([string](Get-StringProperty $contract 'status')).Trim().ToLowerInvariant()
            $effect = ([string](Get-StringProperty $contract 'effect')).Trim().ToLowerInvariant()
            $moodleVersion = [string](Get-StringProperty $contract 'moodleVersion')
            $externalFunctionVersion = [string](Get-StringProperty $contract 'externalFunctionVersion')
            $platformPermission = [string](Get-StringProperty $contract 'platformPermission')
            $administrativeOnlyElement = Get-Property $contract 'administrativeOnly'
            $contractHash = [string](Get-StringProperty $contract 'contractHash')

            if ([string]::IsNullOrWhiteSpace($functionName)) {
                Add-Error $errors 'A contract is missing functionName.'
                continue
            }
            $functionName = $functionName.Trim()

            $identity = "{0}|{1}|{2}" -f $functionName.ToLowerInvariant(), $moodleVersion.Trim(), $externalFunctionVersion.Trim()
            if (-not $contractKeys.Add($identity)) {
                Add-Error $errors "Duplicate contract identity for '$functionName'."
            }

            if ($status -notin @('verified', 'missing', 'stale', 'conflicting', 'invalid')) {
                Add-Error $errors "Contract '$functionName' has an invalid status '$status'."
            }
            if ($effect -notin @('read', 'write', 'unknown')) {
                Add-Error $errors "Contract '$functionName' has an invalid effect '$effect'."
            }

            $administrativeOnly = $false
            if ($null -ne $administrativeOnlyElement) {
                if ($administrativeOnlyElement.ValueKind -notin @(
                        [System.Text.Json.JsonValueKind]::True,
                        [System.Text.Json.JsonValueKind]::False)) {
                    Add-Error $errors "Contract '$functionName' administrativeOnly must be boolean."
                } else {
                    $administrativeOnly = $administrativeOnlyElement.GetBoolean()
                }
            }
            if ($administrativeOnly -and [string]::IsNullOrWhiteSpace($platformPermission)) {
                Add-Error $errors "Administrative contract '$functionName' must declare platformPermission."
            }

            Validate-StringArrayProperty $contract 'requiredScopes' $errors "Contract '$functionName'"
            Validate-StringArrayProperty $contract 'moodleCapabilities' $errors "Contract '$functionName'"

            $fileHandling = Get-Property $contract 'fileHandling'
            if ($null -ne $fileHandling) {
                if ($fileHandling.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
                    Add-Error $errors "Contract '$functionName' fileHandling must be an object."
                } else {
                    Validate-StringArrayProperty $fileHandling 'allowedMimeTypes' $errors "Contract '$functionName' fileHandling"
                }
            }

            $inputSchema = Get-Property $contract 'inputSchema'
            $outputSchema = Get-Property $contract 'outputSchema'
            if ($status -eq 'verified') {
                if ($effect -notin @('read', 'write')) {
                    Add-Error $errors "Verified contract '$functionName' must declare effect read or write."
                }
                if ($null -eq $inputSchema -or $inputSchema.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
                    Add-Error $errors "Verified contract '$functionName' must contain an object inputSchema."
                }
                if ($null -eq $outputSchema -or $outputSchema.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
                    Add-Error $errors "Verified contract '$functionName' must contain an object outputSchema."
                }
                if ($contractHash -notmatch '^[0-9a-fA-F]{64}$') {
                    Add-Error $errors "Verified contract '$functionName' must contain a 64-character SHA-256 contractHash."
                } elseif ($inputSchema.ValueKind -eq [System.Text.Json.JsonValueKind]::Object -and
                    $outputSchema.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
                    $computedHash = Get-ContractHash $contract
                    if (-not [string]::Equals($contractHash, $computedHash, [StringComparison]::OrdinalIgnoreCase)) {
                        Add-Error $errors "Verified contract '$functionName' contractHash does not match the canonical contract contents."
                    }
                }
            }

            $inventoryVersion = if ($functionVersions.ContainsKey($functionName)) {
                [string]$functionVersions[$functionName]
            } else {
                $null
            }
            $releaseMatches = Test-MoodleReleaseCompatible $moodleVersion $inventoryRelease
            $functionVersionMatches = [string]::IsNullOrWhiteSpace($externalFunctionVersion) -or
                ([string]::Equals($externalFunctionVersion.Trim(), $inventoryVersion.Trim(), [StringComparison]::OrdinalIgnoreCase))
            if ($status -eq 'verified' -and $releaseMatches -and $functionVersionMatches -and $inventoryNames.Contains($functionName)) {
                $null = $verifiedCompatible.Add($functionName)
                if ($administrativeOnly) {
                    $null = $policyBlockedCompatible.Add($functionName)
                }
            }
        }
    }

    $missing = @($inventoryNames | Where-Object { -not $verifiedCompatible.Contains($_) })
    Write-Host ([string]::Format(
        'Moodle contract manifest: release={0}; discovered={1}; contracts={2}; verified-compatible={3}; policy-blocked={4}; missing={5}',
        [object[]]@($inventoryRelease, $inventoryNames.Count, $contractCount, $verifiedCompatible.Count, $policyBlockedCompatible.Count, $missing.Count)))

    if ($RequireComplete -and $missing.Count -gt 0) {
        $sample = ($missing | Select-Object -First 10) -join ', '
        Add-Error $errors "The manifest is incomplete for $($missing.Count) discovered function(s). First entries: $sample"
    }

    if ($errors.Count -gt 0) {
        Fail (($errors | Select-Object -First 20) -join ' ')
    }
}
finally {
    $manifestDocument.Dispose()
    $inventoryDocument.Dispose()
}
