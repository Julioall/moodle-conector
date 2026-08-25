[CmdletBinding()]
param(
    [string]$Image = 'moodle-connector-release-validation',
    [int]$HostPort = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-FreeLoopbackPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Remove-TemporaryContainer {
    param([string]$Name)

    if (-not [string]::IsNullOrWhiteSpace($Name)) {
        & docker rm --force $Name *> $null
    }
}

& docker image inspect $Image *> $null
if ($LASTEXITCODE -ne 0) {
    throw "A imagem '$Image' não existe localmente. Crie-a antes de executar esta certificação."
}

if ($HostPort -eq 0) {
    $HostPort = Get-FreeLoopbackPort
}

if ($HostPort -lt 1 -or $HostPort -gt 65535) {
    throw 'HostPort deve estar entre 1 e 65535.'
}

$suffix = [Guid]::NewGuid().ToString('N').Substring(0, 10)
$networkName = "moodle-cert-net-$suffix"
$databaseName = "moodle-cert-db-$suffix"
$applicationName = "moodle-cert-app-$suffix"
$connectionString = "Host=$databaseName;Port=5432;Database=moodle_connector;Username=moodle_connector;Password=validation-password"
$networkCreated = $false
$databaseCreated = $false
$applicationCreated = $false

try {
    & docker network create $networkName *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'Não foi possível criar a rede temporária de certificação.'
    }
    $networkCreated = $true

    & docker run --detach --rm --name $databaseName --network $networkName `
        --env POSTGRES_DB=moodle_connector `
        --env POSTGRES_USER=moodle_connector `
        --env POSTGRES_PASSWORD=validation-password `
        postgres:16-alpine *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'Não foi possível iniciar o PostgreSQL temporário.'
    }
    $databaseCreated = $true

    $databaseReady = $false
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        & docker exec $databaseName pg_isready -U moodle_connector -d moodle_connector *> $null
        if ($LASTEXITCODE -eq 0) {
            $databaseReady = $true
            break
        }

        Start-Sleep -Seconds 1
    }

    if (-not $databaseReady) {
        throw 'O PostgreSQL temporário não ficou pronto para a certificação.'
    }

    & docker run --detach --rm --name $applicationName --network $networkName `
        --publish "127.0.0.1:${HostPort}:8080" `
        --env ASPNETCORE_ENVIRONMENT=Production `
        --env "Postgres__ConnectionString=$connectionString" `
        --env 'ConnectorSecrets__EncryptionKeyBase64=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=' `
        --env 'AdminApi__ApiKey=production-validation-admin-key' `
        --env 'MEDIATR_LICENSE_KEY=production-validation-license' `
        --env 'OAuth__Issuer=https://validation.example' `
        --env 'OAuth__Audience=https://validation.example/mcp' `
        --env 'OAuth__ChatGptRedirectUri=https://chatgpt.com/connector/oauth/validation' `
        $Image *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'Não foi possível iniciar a imagem de produção temporária.'
    }
    $applicationCreated = $true

    $healthResponse = $null
    for ($attempt = 1; $attempt -le 45; $attempt++) {
        try {
            $healthResponse = Invoke-WebRequest -UseBasicParsing `
                -Uri "http://127.0.0.1:$HostPort/health" `
                -Headers @{ 'X-Forwarded-Proto' = 'https' } `
                -TimeoutSec 3
            if ($healthResponse.StatusCode -eq 200) {
                break
            }
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }

    if ($null -eq $healthResponse -or $healthResponse.StatusCode -ne 200) {
        $logs = & docker logs $applicationName 2>&1 | Select-Object -Last 40 | Out-String
        throw "A imagem não respondeu ao healthcheck. Logs finais: $logs"
    }

    if ($healthResponse.Headers['Strict-Transport-Security'] -ne 'max-age=31536000') {
        throw 'A resposta HTTPS encaminhada não incluiu Strict-Transport-Security.'
    }

    $statusResponse = Invoke-WebRequest -UseBasicParsing `
        -Uri "http://127.0.0.1:$HostPort/api/status" `
        -Headers @{ 'X-Forwarded-Proto' = 'https' } `
        -TimeoutSec 5
    if ($statusResponse.StatusCode -ne 200) {
        throw 'O endpoint /api/status não respondeu com HTTP 200.'
    }

    if ($statusResponse.Headers['Strict-Transport-Security'] -ne 'max-age=31536000') {
        throw 'O endpoint /api/status não incluiu Strict-Transport-Security.'
    }

    Write-Output "Production container contract passed for '$Image'."
}
finally {
    if ($applicationCreated) {
        Remove-TemporaryContainer -Name $applicationName
    }

    if ($databaseCreated) {
        Remove-TemporaryContainer -Name $databaseName
    }

    if ($networkCreated) {
        & docker network rm $networkName *> $null
    }
}
