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

Configure DNS apontando `APP_DOMAIN` e `CLARIS_DOMAIN` para a VPS e habilite o profile HTTPS. O Caddy deste projeto e o unico proxy publico da VPS e encaminha o host Claris ao container do outro projeto pela rede Docker externa compartilhada:

```env
APP_DOMAIN=moodle-conector.seu-dominio.com
CLARIS_DOMAIN=claris.seu-dominio.com
PUBLIC_PROXY_NETWORK=novascript-proxy
COMPOSE_PROFILES=https
CADDYFILE=./Caddyfile
```

O Caddy publica o Moodle Connector e o frontend Claris em HTTPS:

```bash
curl https://moodle-conector.seu-dominio.com/health
curl https://claris.seu-dominio.com/health
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
OAuth__ChatGptClientId=chatgpt-mcp
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
- `CLARIS_DOMAIN` - subdomínio público encaminhado para o frontend Claris; padrão `claris.novascript.com.br`
- `PUBLIC_PROXY_NETWORK` - rede Docker externa compartilhada com o Claris; padrão `novascript-proxy`
- `COMPOSE_PROFILES` - padrão `https`
- `CADDYFILE` - padrão `./Caddyfile`
- `MCP_REQUIRE_JWT` - padrão `true`
- `MCP_REQUIRE_API_KEY` - padrão `false`
- `OAUTH_CLIENT_ID` - padrão `chatgpt-mcp`
- `OAUTH_SCOPE_NAME` - padrão `moodle-mcp-audience`
- `OAUTH_REQUIRE_HTTPS_METADATA` - padrão `true`
- `OAUTH_ACCESS_TOKEN_MINUTES` - padrão `60`
- `OAUTH_REFRESH_TOKEN_DAYS` - padrão `30`
- `OAUTH_KEY_STORAGE_PATH` - padrão `/app/data/oauth`
- `OAUTH_CERTIFICATE_YEARS` - padrão `5`
- `RATE_LIMIT_WINDOW_SECONDS` - padrão `60`
- `RATE_LIMIT_PORTAL_AUTH_PERMIT_LIMIT` - padrão `12`
- `RATE_LIMIT_ADMIN_API_PERMIT_LIMIT` - padrão `30`
- `RATE_LIMIT_MCP_PERMIT_LIMIT` - padrão `120`
- `POSTGRES_DB` - padrão `moodle_connector`
- `POSTGRES_USER` - padrão `moodle_connector`
- `COMPOSE_PROJECT_NAME` - padrão `moodle-connector`

O workflow sincroniza o código para a VPS, escreve `.env.production` remoto e executa:

```bash
docker compose --env-file .env.production up -d --build --remove-orphans
```

Ele cria a rede externa `PUBLIC_PROXY_NETWORK` quando ela ainda não existir. Faça o deploy deste projeto antes do Claris para o Caddy carregar o host adicional; em seguida, publique o frontend Claris, que entra na mesma rede sem expor portas públicas.

Antes do deploy, o workflow valida:

- métodos de autenticação MCP (`MCP_REQUIRE_JWT`/`MCP_REQUIRE_API_KEY`);
- variáveis numéricas de porta, OAuth, rate limit e resiliência Moodle;
- URLs OAuth com `https://` quando `OAUTH_REQUIRE_HTTPS_METADATA=true`;
- `CONNECTOR_SECRETS_ENCRYPTION_KEY_BASE64` decodificando para exatamente 32 bytes;
- ausência de quebras de linha em secrets e variáveis que são gravadas no `.env.production`.

Para migrar de uma stack de desenvolvimento anterior, rode o cleanup destrutivo uma vez ou valide manualmente se o schema existente é compatível antes do primeiro deploy desta versão. A aplicação aplica o baseline versionado `Database/Scripts/001_initial_schema.sql`, que cria as tabelas da aplicação e do OpenIddict de forma idempotente.

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
