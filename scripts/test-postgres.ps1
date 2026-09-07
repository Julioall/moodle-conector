[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [int]$Port = 5433,
    [switch]$KeepDatabase
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repoRoot 'docker-compose.test.yml'
$composeProject = 'moodle-connector-test'
$database = 'moodle_connector_ci'
$username = 'moodle_connector'
$password = 'ci-postgres-password'
$connectionString = "Host=localhost;Port=$Port;Database=$database;Username=$username;Password=$password"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker nao foi encontrado no PATH.'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK nao foi encontrado no PATH.'
}

$env:MOODLE_CONNECTOR_POSTGRES_TEST_PORT = $Port.ToString()
$env:MOODLE_CONNECTOR_POSTGRES_TEST_DATABASE = $database
$env:MOODLE_CONNECTOR_POSTGRES_TEST_USERNAME = $username
$env:MOODLE_CONNECTOR_POSTGRES_TEST_PASSWORD = $password
$env:MOODLE_CONNECTOR_POSTGRES_TEST_CONNECTION = $connectionString

$composeArgs = @('-f', $composeFile, '-p', $composeProject)
$testExitCode = 1
$containerId = $null

try {
    & docker compose @composeArgs up -d
    if ($LASTEXITCODE -ne 0) {
        throw 'Nao foi possivel iniciar o PostgreSQL de testes.'
    }

    $deadline = (Get-Date).AddSeconds(90)
    do {
        $containerId = (& docker compose @composeArgs ps -q postgres).Trim()
        $health = if ($containerId) {
            (& docker inspect --format '{{.State.Health.Status}}' $containerId).Trim()
        } else {
            'starting'
        }

        if ($health -eq 'healthy') {
            break
        }

        if ($health -eq 'unhealthy' -or (Get-Date) -ge $deadline) {
            & docker compose @composeArgs logs postgres
            throw "O PostgreSQL de testes nao ficou saudavel. Estado: $health"
        }

        Start-Sleep -Seconds 2
    } while ($true)

    Write-Host "PostgreSQL de testes pronto em localhost:$Port."
    Write-Host "Executando a suite com MOODLE_CONNECTOR_POSTGRES_TEST_CONNECTION."

    & dotnet test (Join-Path $repoRoot 'MoodleConnector.slnx') --configuration $Configuration
    $testExitCode = $LASTEXITCODE
}
finally {
    if (-not $KeepDatabase) {
        & docker compose @composeArgs down --remove-orphans
    }

    Remove-Item Env:MOODLE_CONNECTOR_POSTGRES_TEST_PORT -ErrorAction SilentlyContinue
    Remove-Item Env:MOODLE_CONNECTOR_POSTGRES_TEST_DATABASE -ErrorAction SilentlyContinue
    Remove-Item Env:MOODLE_CONNECTOR_POSTGRES_TEST_USERNAME -ErrorAction SilentlyContinue
    Remove-Item Env:MOODLE_CONNECTOR_POSTGRES_TEST_PASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:MOODLE_CONNECTOR_POSTGRES_TEST_CONNECTION -ErrorAction SilentlyContinue
}

exit $testExitCode
