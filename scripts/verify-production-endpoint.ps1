[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9.-]+$')]
    [string]$AppDomain,

    [string]$ExpectedGitCommit,

    [switch]$RequireStrictTransportSecurity
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$baseUri = "https://$AppDomain"
$expectedIssuer = "$baseUri/"
$expectedAudience = "$baseUri/mcp"
$failures = [System.Collections.Generic.List[string]]::new()

function Add-Failure([string]$Message) {
    $failures.Add($Message)
}

function Get-HttpStatusCodeFromException($Exception) {
    $responseProperty = $Exception.PSObject.Properties['Response']
    if ($null -eq $responseProperty -or $null -eq $responseProperty.Value) {
        return $null
    }

    return [int]$responseProperty.Value.StatusCode
}

function Get-Response([string]$Path) {
    try {
        return Invoke-WebRequest -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 20 -Uri "$baseUri$Path"
    }
    catch {
        $statusCode = Get-HttpStatusCodeFromException $_.Exception
        Add-Failure "GET $Path failed$(if ($null -ne $statusCode) { " with HTTP $statusCode" }): $($_.Exception.Message)"
        return $null
    }
}

function Read-JsonResponse($Response, [string]$Path) {
    if ($null -eq $Response) {
        return $null
    }

    if ($Response.StatusCode -ne 200) {
        Add-Failure "GET $Path returned HTTP $($Response.StatusCode), expected 200."
        return $null
    }

    try {
        return $Response.Content | ConvertFrom-Json
    }
    catch {
        Add-Failure "GET $Path did not return valid JSON: $($_.Exception.Message)"
        return $null
    }
}

function Require-Equal($Actual, $Expected, [string]$Name) {
    if ($Actual -cne $Expected) {
        Add-Failure "$Name must be '$Expected', but was '$Actual'."
    }
}

$health = Get-Response '/health'
if ($null -ne $health) {
    if ($health.StatusCode -ne 200) {
        Add-Failure "GET /health returned HTTP $($health.StatusCode), expected 200."
    }

    if ($health.Headers['X-Content-Type-Options'] -ne 'nosniff') {
        Add-Failure 'GET /health must send X-Content-Type-Options: nosniff.'
    }

    if ($health.Headers['Referrer-Policy'] -ne 'strict-origin-when-cross-origin') {
        Add-Failure 'GET /health must send Referrer-Policy: strict-origin-when-cross-origin.'
    }

    if ([string]::IsNullOrWhiteSpace($health.Headers['Content-Security-Policy'])) {
        Add-Failure 'GET /health must send Content-Security-Policy.'
    }

    if ($RequireStrictTransportSecurity -and [string]::IsNullOrWhiteSpace($health.Headers['Strict-Transport-Security'])) {
        Add-Failure 'GET /health must send Strict-Transport-Security in the production proxy.'
    }

    if ($RequireStrictTransportSecurity -and -not [string]::IsNullOrWhiteSpace($health.Headers['Server'])) {
        Add-Failure 'GET /health must not expose the upstream Server header.'
    }
}

$status = Read-JsonResponse (Get-Response '/api/status') '/api/status'
if ($null -ne $status) {
    if ($status.ok -ne $true -or $status.status -ne 'online') {
        Add-Failure '/api/status must report an online service.'
    }

    Require-Equal $status.endpoint '/mcp' '/api/status.endpoint'
    if ($status.auth.requireJwt -ne $true -or $status.auth.requireApiKey -ne $false) {
        Add-Failure '/api/status must report RequireJwt=true and RequireApiKey=false.'
    }

    Require-Equal $status.auth.issuer $expectedIssuer '/api/status.auth.issuer'
    Require-Equal $status.auth.audience $expectedAudience '/api/status.auth.audience'
    if ($status.auth.chatGptClientConfigured -ne $true -or $status.auth.chatGptRedirectConfigured -ne $true) {
        Add-Failure '/api/status must report a configured ChatGPT OAuth client and redirect URI.'
    }

    if ([int]$status.toolsCount -lt 1) {
        Add-Failure '/api/status.toolsCount must be positive.'
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedGitCommit) -and $status.gitCommit -ne $ExpectedGitCommit) {
        Add-Failure "/api/status.gitCommit must be '$ExpectedGitCommit', but was '$($status.gitCommit)'."
    }
}

$protectedResource = Read-JsonResponse (Get-Response '/.well-known/oauth-protected-resource/mcp') '/.well-known/oauth-protected-resource/mcp'
if ($null -ne $protectedResource) {
    Require-Equal $protectedResource.resource $expectedAudience 'oauth-protected-resource.resource'
    if (@($protectedResource.authorization_servers) -notcontains $expectedIssuer) {
        Add-Failure "oauth-protected-resource.authorization_servers must include '$expectedIssuer'."
    }
}

$openid = Read-JsonResponse (Get-Response '/.well-known/openid-configuration') '/.well-known/openid-configuration'
if ($null -ne $openid) {
    Require-Equal $openid.issuer $expectedIssuer 'openid-configuration.issuer'
    Require-Equal $openid.authorization_endpoint "${expectedIssuer}authorize" 'openid-configuration.authorization_endpoint'
    Require-Equal $openid.token_endpoint "${expectedIssuer}token" 'openid-configuration.token_endpoint'
    Require-Equal $openid.jwks_uri "${expectedIssuer}.well-known/jwks" 'openid-configuration.jwks_uri'
}

$jwks = Read-JsonResponse (Get-Response '/.well-known/jwks') '/.well-known/jwks'
if ($null -ne $jwks -and @($jwks.keys).Count -lt 1) {
    Add-Failure '/.well-known/jwks must expose at least one public key.'
}

$initializePayload = @{
    jsonrpc = '2.0'
    id = 'production-contract-check'
    method = 'initialize'
    params = @{
        protocolVersion = '2025-06-18'
        capabilities = @{}
        clientInfo = @{
            name = 'production-contract-check'
            version = '1.0'
        }
    }
} | ConvertTo-Json -Compress -Depth 5

try {
    $initialize = Invoke-WebRequest -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 20 -Method Post -Uri "$baseUri/mcp" -ContentType 'application/json' -Headers @{ Accept = 'application/json, text/event-stream' } -Body $initializePayload
    if ($initialize.StatusCode -ne 200) {
        Add-Failure "POST /mcp initialize returned HTTP $($initialize.StatusCode), expected 200."
    }

    if ($initialize.Headers['Content-Type'] -notmatch 'text/event-stream') {
        Add-Failure 'POST /mcp initialize must return an event stream.'
    }

    $mcpSessionId = @($initialize.Headers['Mcp-Session-Id'])[0]
    if ([string]::IsNullOrWhiteSpace($mcpSessionId)) {
        Add-Failure 'POST /mcp initialize must return Mcp-Session-Id.'
    }

    if ($initialize.Content -notmatch '"protocolVersion"') {
        Add-Failure 'POST /mcp initialize response is missing protocolVersion.'
    }

    if (-not [string]::IsNullOrWhiteSpace($mcpSessionId)) {
        $mcpHeaders = @{ Accept = 'application/json, text/event-stream'; 'Mcp-Session-Id' = $mcpSessionId }
        $notificationPayload = @{
            jsonrpc = '2.0'
            method = 'notifications/initialized'
            params = @{}
        } | ConvertTo-Json -Compress
        $notification = Invoke-WebRequest -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 20 -Method Post -Uri "$baseUri/mcp" -ContentType 'application/json' -Headers $mcpHeaders -Body $notificationPayload
        if ($notification.StatusCode -notin 200, 202) {
            Add-Failure "POST /mcp notifications/initialized returned HTTP $($notification.StatusCode), expected 200 or 202."
        }

        $toolsPayload = @{
            jsonrpc = '2.0'
            id = 'production-contract-tools-list'
            method = 'tools/list'
            params = @{}
        } | ConvertTo-Json -Compress
        $toolsResponse = Invoke-WebRequest -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 20 -Method Post -Uri "$baseUri/mcp" -ContentType 'application/json' -Headers $mcpHeaders -Body $toolsPayload
        if ($toolsResponse.StatusCode -ne 200) {
            Add-Failure "POST /mcp tools/list returned HTTP $($toolsResponse.StatusCode), expected 200."
        }

        $toolsMatch = [regex]::Match($toolsResponse.Content, 'data:\s*(?<json>\{.*\})\s*$', [System.Text.RegularExpressions.RegexOptions]::Singleline)
        if (-not $toolsMatch.Success) {
            Add-Failure 'POST /mcp tools/list must return an SSE JSON-RPC response.'
        }
        else {
            $toolsMessage = $toolsMatch.Groups['json'].Value | ConvertFrom-Json
            $tools = @($toolsMessage.result.tools)
            if ($tools.Count -lt 1) {
                Add-Failure 'POST /mcp tools/list must expose at least one tool.'
            }
            elseif ($null -ne $status -and $tools.Count -gt [int]$status.toolsCount) {
                # /api/status reports the registered, feature-enabled inventory.
                # tools/list is request-specific and may legitimately be smaller
                # after OAuth scopes, linked-connection policy, and Moodle
                # capability filtering are applied.
                Add-Failure "POST /mcp tools/list exposed $($tools.Count) tools, more than the /api/status inventory of $($status.toolsCount)."
            }

            if (@($tools | Where-Object { $null -eq $_.outputSchema }).Count -gt 0) {
                Add-Failure 'POST /mcp tools/list must expose outputSchema for every production tool.'
            }
        }
    }
}
catch {
    $statusCode = Get-HttpStatusCodeFromException $_.Exception
    Add-Failure "POST /mcp initialize failed$(if ($null -ne $statusCode) { " with HTTP $statusCode" }): $($_.Exception.Message)"
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    throw "Production endpoint verification failed with $($failures.Count) issue(s)."
}

Write-Host "Production endpoint verification passed: $baseUri"
