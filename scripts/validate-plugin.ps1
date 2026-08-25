[CmdletBinding()]
param(
    [Parameter()]
    [string]$PluginPath = "plugins/moodle-connector"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Fail([string]$Message) {
    throw "Plugin validation failed: $Message"
}

function Require-NonEmptyString($Value, [string]$Field) {
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value)) {
        Fail "$Field must be a non-empty string."
    }
}

$pluginRoot = (Resolve-Path -LiteralPath $PluginPath).Path
$pluginName = Split-Path -Leaf $pluginRoot
$manifestPath = Join-Path $pluginRoot ".codex-plugin/plugin.json"

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    Fail "missing .codex-plugin/plugin.json."
}

try {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
} catch {
    Fail "plugin.json must contain valid JSON: $($_.Exception.Message)"
}

if ($null -eq $manifest) {
    Fail "plugin.json must contain a JSON object."
}

$allowedKeys = @("id", "name", "version", "description", "skills", "apps", "mcpServers", "interface", "author", "homepage", "repository", "license", "keywords")
foreach ($key in $manifest.Keys) {
    if ($key -notin $allowedKeys) {
        Fail "plugin.json contains unsupported field '$key'."
    }
}

Require-NonEmptyString $manifest.name "name"
if ($manifest.name -ne $pluginName) {
    Fail "name '$($manifest.name)' must match plugin directory '$pluginName'."
}

Require-NonEmptyString $manifest.version "version"
if ($manifest.version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    Fail "version must use semantic versioning."
}

Require-NonEmptyString $manifest.description "description"
if ($manifest.author -isnot [hashtable]) {
    Fail "author must be an object."
}
Require-NonEmptyString $manifest.author.name "author.name"

if ($manifest.skills -ne "./skills/") {
    Fail "skills must point to ./skills/."
}

$skillsRoot = Join-Path $pluginRoot "skills"
if (-not (Test-Path -LiteralPath $skillsRoot -PathType Container)) {
    Fail "skills directory is missing."
}

if ($manifest.ContainsKey("apps")) {
    if ($manifest.apps -ne "./.app.json") {
        Fail "apps must point to ./.app.json."
    }

    $appManifestPath = Join-Path $pluginRoot ".app.json"
    if (-not (Test-Path -LiteralPath $appManifestPath -PathType Leaf)) {
        Fail ".app.json is required when apps is declared."
    }

    try {
        $appManifest = Get-Content -LiteralPath $appManifestPath -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
    } catch {
        Fail ".app.json must contain valid JSON: $($_.Exception.Message)"
    }

    if ($appManifest.Keys.Count -ne 1 -or -not $appManifest.ContainsKey("apps") -or $appManifest.apps -isnot [hashtable]) {
        Fail ".app.json must contain only an apps object."
    }

    foreach ($app in $appManifest.apps.GetEnumerator()) {
        if ($app.Value -isnot [hashtable]) {
            Fail ".app.json entry '$($app.Key)' must be an object."
        }
        Require-NonEmptyString $app.Value.id ".app.json entry '$($app.Key)'.id"
    }
}

if ($manifest.ContainsKey("mcpServers")) {
    Fail "mcpServers is reserved for a server shipped with the plugin; the Moodle server is remote and must use .app.json."
}

foreach ($skillDirectory in Get-ChildItem -LiteralPath $skillsRoot -Directory) {
    $skillPath = Join-Path $skillDirectory.FullName "SKILL.md"
    if (-not (Test-Path -LiteralPath $skillPath -PathType Leaf)) {
        Fail "skill '$($skillDirectory.Name)' is missing SKILL.md."
    }

    $contents = Get-Content -LiteralPath $skillPath -Raw -Encoding utf8
    if ($contents -match '\[TODO:') {
        Fail "skill '$($skillDirectory.Name)' contains a TODO placeholder."
    }

    if ($contents -notmatch '(?s)\A---\r?\n(?<frontmatter>.*?)\r?\n---') {
        Fail "skill '$($skillDirectory.Name)' must begin with closed YAML frontmatter."
    }

    $frontmatter = $Matches.frontmatter
    if ($frontmatter -notmatch '(?m)^name:\s*\S+') {
        Fail "skill '$($skillDirectory.Name)' frontmatter must declare name."
    }
    if ($frontmatter -notmatch '(?m)^description:\s*\S+') {
        Fail "skill '$($skillDirectory.Name)' frontmatter must declare description."
    }
}

$manifestText = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8
if ($manifestText -match '\[TODO:') {
    Fail "plugin.json contains a TODO placeholder."
}

Write-Host "Plugin validation passed: $pluginRoot"
