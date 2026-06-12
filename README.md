<p>
  <img src="public/logo.png" alt="Moodle Connector" width="720">
</p>

Conector MCP em ASP.NET Core para integrar ChatGPT Apps, portal web e Moodle com autenticação OAuth local, leitura controlada e escrita protegida por confirmação humana.

## Objetivo

O Moodle Connector MCP permite que usuários autorizados consultem dados do Moodle por tools MCP e, em fases futuras, preparem ações sensíveis como mensagens, feedbacks e alterações acadêmicas sempre com prévia, auditoria e confirmação humana.

Diretriz central:

- tools somente leitura executam imediatamente;
- tools de escrita não executam mudança direta na primeira chamada;
- escritas seguem o fluxo `prepare_*` e `confirm_*`;
- dados acadêmicos e pessoais devem ser tratados com mínimo necessário.

## Arquitetura

```text
ChatGPT / Cliente MCP
        |
        | HTTP streamable MCP + OAuth 2.1/PKCE
        v
MoodleConnector.Presentation
        |
        | OpenIddict / JWT Bearer / portal local
        v
MoodleConnector.Application
        |
        | casos de uso, policies e pending actions
        v
MoodleConnector.Infrastructure
        |
        | PostgreSQL / Moodle REST / OpenIddict storage
        v
Moodle + PostgreSQL
```

Responsabilidades:

- `Presentation`: expõe `/mcp`, portal, endpoints OAuth, healthcheck e tools MCP.
- `Application`: concentra queries, commands, contratos de tools e regras de aplicação.
- `Domain`: contém entidades e classificações de domínio, como risco e pending actions.
- `Infrastructure`: implementa banco, gateway Moodle, cache, storage OAuth e repositórios.

## Stack

- ASP.NET Core / .NET 10
- Solution: `MoodleConnector.slnx`
- MCP: `ModelContextProtocol.AspNetCore 1.3.0`
- OpenIddict Server embutido para OAuth
- OAuth authorization code + PKCE `S256`
- JWT Bearer no `/mcp`
- API key opcional via `X-Mcp-Api-Key`
- PostgreSQL 16, EF Core 10 e Npgsql
- Docker Compose
- Caddy 2 para HTTPS/reverse proxy
- GitHub Actions para deploy em VPS

## Rodando Localmente

```bash
cp .env.example .env.production
docker compose --env-file .env.production up -d --build
curl http://127.0.0.1:8787/health
```

Por padrão, a aplicação fica disponível em:

```text
http://127.0.0.1:8787
```

Para usar com ChatGPT Apps, exponha a aplicação por domínio público HTTPS na VPS. O ChatGPT não completa OAuth contra `localhost` puro.

## Testes

```bash
dotnet test MoodleConnector.slnx
```

A suíte cobre contratos MCP, autenticação JWT/API key, OAuth local, cadastro/login do portal, pending actions, resolução de usuário Moodle e tools implementadas.

## Endpoints

- `POST /mcp`: endpoint MCP streamable HTTP.
- `GET /health`: healthcheck.
- `GET /api/status`: status da API e configuração de autenticação.
- `GET /.well-known/oauth-protected-resource/mcp`: metadata OAuth Protected Resource para descoberta pelo ChatGPT.
- `GET /.well-known/oauth-authorization-server`: metadata do authorization server local.
- `GET /.well-known/openid-configuration`: discovery OIDC publicado pelo OpenIddict.
- `GET /.well-known/jwks`: chaves públicas usadas para validar JWTs.
- `GET|POST /authorize`: início do authorization code flow.
- `POST /token`: troca de code/refresh token feita pelo OpenIddict.

Produção:

```text
https://<APP_DOMAIN>/mcp
```

## Autenticação

O `/mcp` usa OAuth/JWT como padrão para ChatGPT Apps:

- JWT Bearer via `Authorization: Bearer <JWT>`.
- API key via `X-Mcp-Api-Key` apenas quando `McpServerSecurity__RequireApiKey=true`.

Configuração recomendada:

```text
APP_DOMAIN=moodle-conector.seu-dominio.com
COMPOSE_PROFILES=https
CADDYFILE=./Caddyfile

McpServerSecurity__RequireJwt=true
McpServerSecurity__RequireApiKey=false

OAuth__Issuer=https://moodle-conector.seu-dominio.com
OAuth__Audience=https://moodle-conector.seu-dominio.com/mcp
OAuth__ChatGptClientId=chatgpt-mcp
OAuth__ChatGptRedirectUri=https://chatgpt.com/connector/oauth/<callback_id>
OAuth__ScopeName=moodle-mcp-audience
OAuth__RequireHttpsMetadata=true
OAuth__KeyStoragePath=/app/data/oauth
```

Quando `OAuth__Issuer` e `OAuth__Audience` ficam vazios, a aplicação deriva os valores de `APP_DOMAIN`.

O portal local usa cookie HttpOnly/SameSite, senha mínima de 12 caracteres e rate limit nos endpoints de cadastro/login. O `/mcp` também aplica rate limit por usuário/conector. Em produção, `OAuth__RequireHttpsMetadata=true` força issuer, audience e callback HTTPS.

## Variáveis De Ambiente

Use `.env.example` como base. Principais grupos:

```text
APP_PORT=8787
APP_DOMAIN=<APP_DOMAIN>
COMPOSE_PROFILES=https
CADDYFILE=./Caddyfile

POSTGRES_DB=moodle_connector
POSTGRES_USER=moodle_connector
POSTGRES_PASSWORD=<POSTGRES_PASSWORD>
Postgres__ConnectionString=Host=postgres;Port=5432;Database=moodle_connector;Username=moodle_connector;Password=<POSTGRES_PASSWORD>

ConnectorSecrets__EncryptionKeyBase64=<32_BYTE_BASE64_KEY>
AdminApi__ApiKey=<ADMIN_API_KEY>

McpServerSecurity__RequireJwt=true
McpServerSecurity__RequireApiKey=false

OAuth__ChatGptRedirectUri=<CHATGPT_CALLBACK_URI_EXATA>
OAuth__KeyStoragePath=/app/data/oauth

RateLimiting__WindowSeconds=60
RateLimiting__PortalAuthPermitLimit=12
RateLimiting__AdminApiPermitLimit=30
RateLimiting__McpPermitLimit=120

MoodleApi__HttpTimeoutSeconds=30
MoodleApi__HttpRetryCount=2
MoodleApi__CircuitBreakerHandledEventsAllowedBeforeBreaking=5
MoodleApi__CircuitBreakerDurationSeconds=30

MoodleProxy__HttpTimeoutSeconds=30
MoodleProxy__HttpRetryCount=2
MoodleProxy__CircuitBreakerHandledEventsAllowedBeforeBreaking=5
MoodleProxy__CircuitBreakerDurationSeconds=30
```

## Fluxo `prepare_*` E `confirm_*`

Tools de escrita devem seguir duas etapas:

1. `prepare_*`: valida a intenção, monta uma prévia, grava uma ação pendente e retorna o texto exato de confirmação.
2. `confirm_*`: recebe o `pendingActionId`, valida usuário, escopo, expiração e texto de confirmação, e só então executa a ação.

Contrato de ação pendente:

```json
{
  "status": "pending_confirmation",
  "pendingActionId": "00000000-0000-0000-0000-000000000000",
  "toolName": "preparar_acao_demo",
  "riskLevel": "HumanConfirmedWrite",
  "preview": {},
  "confirmationText": "CONFIRMAR ...",
  "expiresAt": "2026-05-31T00:15:00Z"
}
```

No estado atual, o projeto possui tools demo para validar esse fluxo. Escritas reais no Moodle permanecem desabilitadas por padrão.

## Tools Existentes

Leitura:

| Tool | Descrição | Status |
| --- | --- | --- |
| `search` | Busca cursos autorizados no formato padrão MCP connector/company knowledge. | Implementada |
| `fetch` | Retorna um curso autorizado no formato padrão MCP connector/company knowledge. | Implementada |
| `listar_meus_cursos` | Lista cursos vinculados ao usuário autenticado com metadados básicos. | Implementada |
| `list_courses` | Alias em inglês de `listar_meus_cursos`. | Implementada |
| `buscar_cursos` | Busca cursos vinculados por termo, nome, categoria, `courseId`, `shortName` ou `idNumber`. | Implementada |
| `search_courses` | Alias em inglês de `buscar_cursos`. | Implementada |
| `consultar_curso` | Consulta metadados básicos de um curso vinculado. | Implementada |
| `get_course` | Alias em inglês de `consultar_curso`. | Implementada |

Demo de pending action:

| Tool | Descrição | Status |
| --- | --- | --- |
| `preparar_acao_demo` | Cria ação pendente demonstrativa. Não executa escrita real no Moodle. | Implementada como demo |
| `confirmar_acao_demo` | Confirma ação pendente demonstrativa. Não executa escrita real no Moodle. | Implementada como demo |

## Segurança

- Não registrar senhas, JWTs, API keys, tokens Moodle, refresh tokens ou links privados com token em logs.
- Não expor dados de estudantes sem necessidade operacional clara.
- Preferir respostas agregadas quando possível.
- Escritas exigem confirmação humana quando aplicável.
- Tools de escrita devem validar usuário, escopo, vínculo Moodle, expiração, idempotência e texto de confirmação.
- Payloads de auditoria devem ser sanitizados antes de persistir.

## Documentação Detalhada

- TODO operacional: `TODO.md`
- Roadmap funcional: `docs/roadmap.md`
- Setup local: `docs/technical/local-setup.md`
- Setup Moodle Webservice: `docs/technical/moodle-webservice-setup.md`
- Catálogo MCP: `docs/technical/mcp-tools-catalog.md`
- Modelo de segurança: `docs/technical/security-model.md`
- Checklist de release: `docs/security/release-checklist.md`
- Modelo de auditoria: `docs/technical/audit-model.md`
- OAuth ChatGPT Apps: `docs/architecture/chatgpt-app-oauth.md`
- Deploy: `docs/operations/deploy-runbook.md`
- Troubleshooting: `docs/operations/troubleshooting-runbook.md`
- Auth e escopos: `docs/security/auth-and-scopes.md`
- Contrato de resposta MCP: `docs/mcp/tool-response-contract.md`
