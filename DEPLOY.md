# Deploy com Docker Compose

## Local

1. Copie o exemplo de ambiente:

```bash
cp .env.example .env.production
```

2. Ajuste os valores de `.env.production`.

3. Suba a aplicação:

```bash
docker compose --env-file .env.production up -d --build
```

4. Valide:

```bash
curl http://127.0.0.1:8787/health
curl http://127.0.0.1:8787/api/status
```

## VPS Com Domínio

Configure DNS apontando `APP_DOMAIN` para a VPS e habilite o profile HTTPS. O Caddy deste projeto publica somente o Moodle Connector e sua SPA integrada:

```env
APP_DOMAIN=moodle-conector.seu-dominio.com
COMPOSE_PROFILES=https
CADDYFILE=./Caddyfile
```

O Caddy publica o Moodle Connector e o portal integrado em HTTPS:

```bash
curl https://moodle-conector.seu-dominio.com/health
curl https://moodle-conector.seu-dominio.com/
```

O endpoint do ChatGPT App deve ser:

```text
https://moodle-conector.seu-dominio.com/mcp
```

## OAuth Para ChatGPT Apps

O authorization server roda embutido na aplicação via OpenIddict. A configuração recomendada é:

```env
McpServerSecurity__RequireJwt=true
McpServerSecurity__RequireApiKey=false

OAuth__Issuer=https://moodle-conector.seu-dominio.com
OAuth__Audience=https://moodle-conector.seu-dominio.com/mcp
OAuth__ChatGptClientId=moodle
OAuth__ChatGptRedirectUri=https://chatgpt.com/connector/oauth/<callback_id>
OAuth__ScopeName=moodle-mcp-audience
OAuth__RequireHttpsMetadata=true
OAuth__AccessTokenMinutes=60
OAuth__RefreshTokenDays=30
OAuth__KeyStoragePath=/app/data/oauth
OAuth__CertificateYears=5
```

Se `OAuth__Issuer` e `OAuth__Audience` forem omitidos, a aplicação deriva:

```text
issuer   = https://<APP_DOMAIN>
audience = https://<APP_DOMAIN>/mcp
```

Copie `OAuth__ChatGptRedirectUri` exatamente da tela de configuração do ChatGPT App.

## GitHub Actions

Configure o environment `moodle-connector` com os secrets:

- `VPS_HOST`
- `VPS_USER`
- `VPS_SSH_KEY`
- `POSTGRES_PASSWORD`
- `CONNECTOR_SECRETS_ENCRYPTION_KEY_BASE64`
- `ADMIN_API_KEY`
- `MEDIATR_LICENSE_KEY`
- `OAUTH_CHATGPT_REDIRECT_URI`

Secrets opcionais:

- `APP_DOMAIN`
- `OAUTH_ISSUER`
- `OAUTH_AUDIENCE`
- `MOODLE_API_BASE_URL`
- `MOODLE_API_SERVICE_TOKEN`
- `MOODLE_API_WRITE_SERVICE_TOKEN`
- `MOODLE_PROXY_BASE_URL`
- `MOODLE_PROXY_API_KEY`

Variables opcionais:

- `VPS_APP_DIR` - padrão `/opt/moodle-connector`
- `VPS_SSH_PORT` - padrão `22`
- `APP_PORT` - padrão `8787`
- `APP_DOMAIN` - domínio público usado pelo Caddy para HTTPS automático
- `COMPOSE_PROFILES` - padrão `https`
- `CADDYFILE` - padrão `./Caddyfile`
- `MCP_REQUIRE_JWT` - padrão `true`
- `MCP_REQUIRE_API_KEY` - padrão `false`
- `FEATURES_MESSAGES_WRITE_ENABLED`, `FEATURES_SCHEDULED_MESSAGES_ENABLED`, `FEATURES_ASSIGNMENT_FEEDBACK_WRITE_ENABLED`, `FEATURES_ASSIGNMENT_GRADE_WRITE_ENABLED`, `FEATURES_UNIVERSAL_MOODLE_WRITE_ENABLED` e `FEATURES_COURSE_CONTENT_WRITE_ENABLED` - padrão `true`; defina explicitamente a política de escrita aprovada para o ambiente
- `FEATURES_MCP_RESOURCE_SUBMISSION_DELIVERY_ENABLED` - padrão `true` no deploy; habilita a entrega de submissões por MCP Resources
- `FEATURES_LEGACY_SUBMISSION_EXTRACTION_ENABLED` - padrão `true`; mantém o fallback legado disponível
- `FEATURES_MCP_RESOURCE_ZIP_ENABLED`, `FEATURES_MCP_GRADING_DRAFT_ENABLED` e `FEATURES_MCP_GRADING_WRITE_ENABLED` - padrão `false`; habilite somente após validação da coorte correspondente
- `OAUTH_CLIENT_ID` - padrão `moodle`
- `OAUTH_SCOPE_NAME` - padrão `moodle-mcp-audience`
- `OAUTH_REQUIRE_HTTPS_METADATA` - padrão `true`
- `OAUTH_ACCESS_TOKEN_MINUTES` - padrão `60`
- `OAUTH_REFRESH_TOKEN_DAYS` - padrão `30`
- `OAUTH_KEY_STORAGE_PATH` - padrão `/app/data/oauth`
- `OAUTH_CERTIFICATE_YEARS` - padrão `5`
- `RATE_LIMIT_WINDOW_SECONDS` - padrão `60`
- `RATE_LIMIT_APP_AUTH_PERMIT_LIMIT` - padrão `12`
- `RATE_LIMIT_ADMIN_API_PERMIT_LIMIT` - padrão `30`
- `RATE_LIMIT_MCP_PERMIT_LIMIT` - padrão `120`
- `POSTGRES_DB` - padrão `moodle_connector`
- `POSTGRES_USER` - padrão `moodle_connector`
- `COMPOSE_PROJECT_NAME` - padrão `moodle-connector`
- `RESET_DATABASE_ON_DEPLOY` - padrão `false`; quando excepcionalmente `true` em desenvolvimento, o deploy remove somente o volume PostgreSQL remoto antes de subir a aplicação.

O workflow sincroniza o código para a VPS, escreve `.env.production` remoto e executa:

```bash
docker compose --env-file .env.production up -d --build --remove-orphans
```

O Compose de produção é autocontido: Caddy, aplicação e PostgreSQL ficam na rede padrão do projeto.
A porta interna da aplicação é publicada somente em `127.0.0.1:${APP_PORT}` para healthchecks e
diagnóstico local; o Caddy é a única entrada pública em 80/443.

Antes do deploy, o workflow valida:

- métodos de autenticação MCP (`MCP_REQUIRE_JWT`/`MCP_REQUIRE_API_KEY`);
- variáveis numéricas de porta, OAuth, rate limit e resiliência Moodle;
- URLs OAuth com `https://` quando `OAUTH_REQUIRE_HTTPS_METADATA=true`;
- `CONNECTOR_SECRETS_ENCRYPTION_KEY_BASE64` decodificando para exatamente 32 bytes;
- presença de `MEDIATR_LICENSE_KEY`;
- ausência de quebras de linha em secrets e variáveis que são gravadas no `.env.production`.

Quando habilitado somente para desenvolvimento, `RESET_DATABASE_ON_DEPLOY=true` limpa o volume
do PostgreSQL no deploy. Isso apaga usuários, conexões, memórias, auditorias, tarefas e demais
registros do banco. O CI de PR não toca na VPS. Em produção, mantenha o valor padrão `false` e
valide backup e rollback antes de qualquer alteração de schema.

Como o banco de desenvolvimento é recriado a cada deploy, não criamos migrations evolutivas neste momento. O schema continua sendo aplicado pelos scripts versionados em `src/MoodleConnector.Infrastructure/Database/Scripts/`, de forma idempotente. Quando houver dados que precisem ser preservados, a política deverá mudar primeiro e só então migrations/alterações compatíveis deverão ser introduzidas.

## Cleanup Da VPS

Workflow: `.github/workflows/cleanup-vps.yml`

O cleanup é manual e destrutivo. Para executar pelo GitHub Actions, informe `RESET_VPS` no input `confirm_full_reset`. Ele reutiliza o environment `moodle-connector` e os mesmos secrets de acesso SSH:

- `VPS_HOST`
- `VPS_USER`
- `VPS_SSH_KEY`

Por padrão, o cleanup deixa a VPS pronta para uma implementação limpa:

- derruba a stack com `docker compose down --volumes --remove-orphans --rmi local`;
- para e remove containers Docker quando `remove_all_docker_data=true`;
- remove volumes do projeto, incluindo dados de Postgres e Caddy;
- remove arquivos `.env` e todo o conteúdo de `VPS_APP_DIR`;
- remove arquivos conhecidos de ambiente do projeto em `/etc`;
- limpa cache de build, imagens, redes e volumes Docker.

Esse processo apaga dados persistidos em volumes Docker. Use antes de um deploy limpo quando o estado anterior da VPS pode atrapalhar a subida do projeto.

## Rollback

O procedimento operacional de rollback está em `docs/operations/deploy-runbook.md#rollback`. Revise também `docs/security/release-checklist.md` antes de publicar uma nova release.
