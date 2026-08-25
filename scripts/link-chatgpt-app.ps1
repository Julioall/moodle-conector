[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^plugin_asdk_app_[A-Za-z0-9_-]+$')]
    [string]$AppId,

    [Parameter()]
    [string]$PluginPath = "plugins/moodle-connector"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$pluginRoot = (Resolve-Path -LiteralPath $PluginPath).Path
$manifestPath = Join-Path $pluginRoot ".codex-plugin/plugin.json"
$appManifestPath = Join-Path $pluginRoot ".app.json"

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Plugin manifest not found: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
if ($manifest -isnot [hashtable]) {
    throw "plugin.json must contain a JSON object."
}

if ($manifest.ContainsKey("mcpServers")) {
    throw "The Moodle Connector uses a registered remote app; remove mcpServers before linking it."
}

$appManifest = [ordered]@{
    apps = [ordered]@{
        moodle = [ordered]@{
            id = $AppId
            category = "Productivity"
        }
    }
}

$manifest["apps"] = "./.app.json"

if ($PSCmdlet.ShouldProcess($pluginRoot, "Link registered ChatGPT app $AppId")) {
    $appManifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $appManifestPath -Encoding utf8
    $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding utf8

    Write-Host "ChatGPT app linked to plugin: $pluginRoot"
    Write-Host "Run ./scripts/validate-plugin.ps1 before installing or refreshing the plugin."
}
