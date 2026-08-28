# Runbook de Deploy

## Visão Geral

O deploy principal é feito por GitHub Actions em:

```text
.github/workflows/deploy-vps.yml
```

O alvo é uma VPS com Docker Compose, PostgreSQL, aplicação e Caddy opcional para HTTPS.

## Fluxo Do Workflow

1. Checkout do repositório.
2. Validação de secrets/variáveis.
3. Setup do .NET.
4. `dotnet restore`.
5. `dotnet test`.
6. Configuração SSH.
7. Preparação do host remoto.
8. Sync do repositório com `rsync`.
9. Escrita remota do `.env.production`.
10. `docker compose up -d --build --remove-orphans`.
11. Healthcheck HTTP/HTTPS.

## Variáveis Recomendadas

| Variável | Exemplo |
| --- | --- |
| `APP_DOMAIN` | `moodle-conector.seu-dominio.com` |
| `COMPOSE_PROFILES` | `https` |
| `CADDYFILE` | `./Caddyfile` |
| `MCP_REQUIRE_JWT` | `true` |
| `MCP_REQUIRE_API_KEY` | `false` |
| `FEATURES_MESSAGES_WRITE_ENABLED` | `true` |
| `FEATURES_ASSIGNMENT_GRADE_WRITE_ENABLED` | `true` |
| `FEATURES_ASSIGNMENT_FEEDBACK_WRITE_ENABLED` | `true` |
| `OAUTH_CLIENT_ID` | `moodle` |
| `OAUTH_SCOPE_NAME` | `moodle-mcp-audience` |
| `OAUTH_REQUIRE_HTTPS_METADATA` | `true` |
| `RATE_LIMIT_WINDOW_SECONDS` | `60` |
| `RATE_LIMIT_MCP_PERMIT_LIMIT` | `120` |

As tres flags de escrita acima sao repassadas pelo Compose como variaveis
`Features__*` da aplicacao. Defina uma delas como `false` nas variaveis do
ambiente GitHub para desabilitar especificamente essa capacidade em um deploy.

## Secrets Obrigatórios

- `VPS_HOST`
- `VPS_USER`
- `VPS_SSH_KEY`
- `POSTGRES_PASSWORD`
- `CONNECTOR_SECRETS_ENCRYPTION_KEY_BASE64`
- `ADMIN_API_KEY`
- `OAUTH_CHATGPT_REDIRECT_URI`

## Validações Do Workflow

O step `Validate VPS secrets` falha antes do sync quando:

- nenhum método de autenticação MCP está ativo;
- booleanos não usam `true` ou `false`;
- portas, tempos, retries, circuit breaker ou rate limits estão fora das faixas aceitas pela aplicação;
- `OAUTH_REQUIRE_HTTPS_METADATA=true` e issuer, audience ou callback explícitos não usam `https://`;
- `CONNECTOR_SECRETS_ENCRYPTION_KEY_BASE64` não decodifica para 32 bytes;
- algum secret ou valor gravado no `.env.production` contém quebra de linha.

## Comandos Úteis Na VPS

```bash
cd /opt/moodle-connector
docker compose --env-file .env.production ps
docker compose --env-file .env.production logs --tail=200 app
docker compose --env-file .env.production logs --tail=200 caddy
```

## Healthcheck E Metadata

```bash
curl http://127.0.0.1:<APP_PORT>/health
curl https://<APP_DOMAIN>/health
curl https://<APP_DOMAIN>/api/status
curl https://<APP_DOMAIN>/.well-known/oauth-protected-resource/mcp
curl https://<APP_DOMAIN>/.well-known/oauth-authorization-server
curl https://<APP_DOMAIN>/.well-known/openid-configuration
curl https://<APP_DOMAIN>/.well-known/jwks
```

## Primeiro Deploy Desta Versão

No startup fora de `Testing`, a aplicação aplica o script versionado `Database/Scripts/001_initial_schema.sql`. Ele cria as tabelas da aplicação e do OpenIddict de forma idempotente e registra a versão em `moodle_connector_schema_versions`.

Se a VPS ainda tiver volume Postgres criado por uma versão de desenvolvimento anterior, execute o cleanup destrutivo ou valide manualmente se o schema existente é compatível antes do deploy.

## Rollback

Rollback é manual e deve preservar volumes, exceto quando o operador decidir executar cleanup destrutivo.

Procedimento:

1. Identificar commit anterior estável.
2. Registrar o commit atual e o motivo do rollback no incidente/release.
3. Na VPS, capturar estado antes da reversão:

```bash
cd /opt/moodle-connector
docker compose --env-file .env.production ps
docker compose --env-file .env.production logs --tail=300 app > rollback-app-before.log
docker compose --env-file .env.production logs --tail=300 caddy > rollback-caddy-before.log
```

4. Fazer deploy do commit anterior pelo workflow ou manualmente.

Deploy manual:

```bash
cd /opt/moodle-connector
git fetch --all
git checkout <COMMIT_ESTAVEL>
docker compose --env-file .env.production up -d --build --remove-orphans
```

5. Validar saúde e metadata:

```bash
curl -f http://127.0.0.1:<APP_PORT>/health
curl -f https://<APP_DOMAIN>/health
curl -f https://<APP_DOMAIN>/api/status
curl -f https://<APP_DOMAIN>/.well-known/oauth-protected-resource/mcp
curl -f https://<APP_DOMAIN>/.well-known/oauth-authorization-server
curl -f https://<APP_DOMAIN>/.well-known/openid-configuration
curl -f https://<APP_DOMAIN>/.well-known/jwks
```

6. No ChatGPT App, atualizar/recarregar a configuração do app para forçar nova leitura de tool descriptors quando houve mudança de metadata MCP.
7. Se o rollback envolver schema, validar compatibilidade antes de reapontar tráfego. O baseline atual é idempotente, mas scripts futuros podem exigir plano de reversão próprio.

## Cleanup Destrutivo

Workflow:

```text
.github/workflows/cleanup-vps.yml
```

Requer confirmação manual pelo input `confirm_full_reset`.

Use apenas quando for necessário limpar containers, imagens, volumes, arquivos `.env` e diretório da aplicação.
