# Setup Local

Este guia descreve como executar o MoodleConnector localmente com a stack atual.

## Pré-requisitos

- .NET SDK 10
- Docker e Docker Compose
- Acesso a um Moodle com webservices habilitados, se for testar chamadas reais

## Estrutura Da Solução

```text
MoodleConnector.slnx
src/
  MoodleConnector.Domain
  MoodleConnector.Application
  MoodleConnector.Infrastructure
  MoodleConnector.Presentation
tests/
  MoodleConnector.Application.Tests
```

## Restaurar E Testar

```bash
dotnet restore MoodleConnector.slnx
dotnet test MoodleConnector.slnx
```

## Executar Com Docker Compose

1. Crie o arquivo de ambiente:

```bash
cp .env.example .env.production
```

2. Ajuste os valores sensíveis:

```env
POSTGRES_PASSWORD=<POSTGRES_PASSWORD>
ConnectorSecrets__EncryptionKeyBase64=<32_BYTE_BASE64_KEY>
AdminApi__ApiKey=<ADMIN_API_KEY>
```

3. Para desenvolvimento local sem ChatGPT, use API key ou stub de dados conforme necessário:

```env
McpServerSecurity__RequireJwt=false
McpServerSecurity__RequireApiKey=true
MoodleApi__UseStubData=true
```

4. Suba a stack:

```bash
docker compose --env-file .env.production up -d --build
```

5. Valide:

```bash
curl http://127.0.0.1:8787/health
curl http://127.0.0.1:8787/api/status
```

## Profiles Do Docker Compose

| Profile | Uso |
| --- | --- |
| `https` | Sobe Caddy como reverse proxy HTTPS. |

Sem profiles, o Compose sobe apenas aplicação e PostgreSQL.

## OAuth Local

O broker OAuth roda dentro da própria aplicação. Para testes locais de endpoint e metadata:

```env
APP_DOMAIN=
OAuth__Issuer=http://localhost:8787
OAuth__Audience=http://localhost:8787/mcp
OAuth__RequireHttpsMetadata=false
OAuth__ChatGptClientId=chatgpt-mcp
OAuth__ScopeName=moodle-mcp-audience
OAuth__KeyStoragePath=App_Data/oauth
```

Para ChatGPT Apps, use VPS/domínio com HTTPS:

```env
APP_DOMAIN=moodle-conector.<dominio>
COMPOSE_PROFILES=https
CADDYFILE=./Caddyfile
McpServerSecurity__RequireJwt=true
McpServerSecurity__RequireApiKey=false
OAuth__ChatGptRedirectUri=https://chatgpt.com/connector/oauth/<callback_id>
```

O endpoint público será:

```text
https://<APP_DOMAIN>/mcp
```

## Configurações Principais

| Seção | Chave | Descrição |
| --- | --- | --- |
| `McpServerSecurity` | `RequireJwt` | Exige JWT Bearer no `/mcp`. |
| `McpServerSecurity` | `RequireApiKey` | Exige/aceita API key no `/mcp`. |
| `McpServerSecurity` | `ApiKeyHeader` | Header da API key. Padrão: `X-Mcp-Api-Key`. |
| `OAuth` | `Issuer` | Emissor do broker OAuth local. Se vazio, deriva de `APP_DOMAIN`. |
| `OAuth` | `Audience` | Audience esperada no JWT. Se vazio, deriva de `APP_DOMAIN` + `/mcp`. |
| `OAuth` | `ChatGptRedirectUri` | Callback exato exibido na configuração do ChatGPT App. |
| `OAuth` | `KeyStoragePath` | Diretório dos certificados OAuth persistidos. |
| `RateLimiting` | `WindowSeconds` | Janela fixa dos limitadores. Padrão: `60`. |
| `RateLimiting` | `PortalAuthPermitLimit` | Chamadas permitidas por janela nos endpoints de cadastro/login/conexão Moodle. Padrão: `12`. |
| `RateLimiting` | `AdminApiPermitLimit` | Chamadas permitidas por janela nos endpoints administrativos. Padrão: `30`. |
| `RateLimiting` | `McpPermitLimit` | Chamadas MCP permitidas por janela por usuário/conector. Padrão: `120`. |
| `MoodleApi` | `AllowServiceTokenForReadOnlyQueries` | Quando `true`, permite token global de leitura (`ServiceToken`) em consultas read-only. |
| `Postgres` | `ConnectionString` | Conexão EF Core com PostgreSQL. |
| `ConnectorSecrets` | `EncryptionKeyBase64` | Chave para proteger credenciais Moodle. |
| `MoodleApi` | `LoginService` | Serviço Moodle usado para obter token. Padrão: `moodle_mobile_app`. |
| `MoodleApi` | `HttpTimeoutSeconds` | Timeout das chamadas REST diretas ao Moodle. Padrão: `30`. |
| `MoodleApi` | `HttpRetryCount` | Tentativas adicionais para falhas transitórias HTTP ao chamar Moodle direto. `0` desabilita retry. Padrão: `2`. |
| `MoodleApi` | `CircuitBreakerHandledEventsAllowedBeforeBreaking` | Falhas transitórias antes de abrir o circuit breaker das chamadas diretas. `0` desabilita. Padrão: `5`. |
| `MoodleApi` | `CircuitBreakerDurationSeconds` | Tempo de abertura do circuit breaker das chamadas diretas. Padrão: `30`. |
| `MoodleProxy` | `HttpTimeoutSeconds` | Timeout das chamadas ao proxy Moodle. Padrão: `30`. |
| `MoodleProxy` | `HttpRetryCount` | Tentativas adicionais para falhas transitórias HTTP ao chamar o proxy. `0` desabilita retry. Padrão: `2`. |
| `MoodleProxy` | `CircuitBreakerHandledEventsAllowedBeforeBreaking` | Falhas transitórias antes de abrir o circuit breaker do proxy. `0` desabilita. Padrão: `5`. |
| `MoodleProxy` | `CircuitBreakerDurationSeconds` | Tempo de abertura do circuit breaker do proxy. Padrão: `30`. |
| `Features` | `DemoToolsEnabled` | Expõe tools demo de pending action. |

## Observações

- `UseStubData=false` é o padrão em `appsettings.json`; o fluxo real precisa de um Moodle acessível.
- O schema inicial é aplicado pelo script versionado `src/MoodleConnector.Infrastructure/Database/Scripts/001_initial_schema.sql`.
- O portal local usa cookie HttpOnly e senha mínima de 12 caracteres; em produção o cookie passa a ser `Secure` quando `OAuth__RequireHttpsMetadata=true`.
- Não grave tokens, senhas, API keys ou secrets reais em arquivos versionados.

## LiveShadow tests (executando contra um Moodle real)

Alguns testes de integração da categoria `LiveShadow` executam chamadas reais contra um ambiente Moodle e precisam de credenciais válidas. Para evitar embutir credenciais no código, as credenciais são lidas de variáveis de ambiente.

Nomes suportados:

- `LIVE_USERNAME` — nome de usuário genérico (fallback).
- `LIVE_PASSWORD` — senha genérica (fallback).
- `LIVE_{ALIAS}_USERNAME` — nome de usuário específico para um alias de conexão (ex.: `LIVE_FIEG_USERNAME`).
- `LIVE_{ALIAS}_PASSWORD` — senha específica para um alias de conexão (ex.: `LIVE_FIEG_PASSWORD`).

Comportamento:

- Os testes tentam primeiro `LIVE_{ALIAS}_USERNAME` / `LIVE_{ALIAS}_PASSWORD` (onde `{ALIAS}` é o alias da conexão em maiúsculas). Se não existirem, usam `LIVE_USERNAME` / `LIVE_PASSWORD`.
- Se nenhuma variável estiver definida, o teste `LiveShadow` é pulado e escreve uma mensagem no output explicando quais variáveis faltaram.

Exemplo (PowerShell):

```powershell
$env:LIVE_FIEG_USERNAME = "04112637225"
$env:LIVE_FIEG_PASSWORD = "442ficxk"
dotnet test tests\MoodleConnector.Application.Tests\MoodleConnector.Application.Tests.csproj --filter FullyQualifiedName~AssignmentsLiveShadowTests -v normal
```

Observação: Não coloque credenciais reais em repositórios ou em shells compartilhados. Use variáveis de ambiente temporárias em sessões de CI/CD ou runners seguros.
