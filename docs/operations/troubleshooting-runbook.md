# Runbook de Troubleshooting

## Coleta Inicial

Na VPS:

```bash
cd /opt/moodle-connector
docker compose --env-file .env.production ps
docker compose --env-file .env.production logs --tail=200 app
docker compose --env-file .env.production logs --tail=200 caddy
```

## `/health` Falha

Verificar:

- container `moodle-connector-app` está saudável;
- PostgreSQL está saudável;
- variáveis obrigatórias existem no `.env.production`;
- porta `APP_PORT` está livre.

Comandos:

```bash
curl -v http://127.0.0.1:<APP_PORT>/health
docker compose --env-file .env.production logs --tail=200 app
```

## HTTPS Falha

Verificar:

- DNS de `<APP_DOMAIN>` aponta para a VPS;
- profile `https` está ativo;
- `CADDYFILE=./Caddyfile`;
- Caddy está em execução;
- portas 80 e 443 estão abertas.

Comandos:

```bash
curl -v https://<APP_DOMAIN>/health
docker compose --env-file .env.production logs --tail=200 caddy
```

## Portal exibe erros 404 em `/assets/index-*.js` ou `/assets/index-*.css`

Os arquivos do portal são versionados pelo Vite a cada build. Esse erro indica que
o navegador ainda possui o HTML de uma publicação anterior, que referencia hashes
de arquivos já removidos na publicação atual.

Atualize a página ignorando o cache (`Ctrl+F5`) ou limpe os dados do site. Em
seguida, valide que o HTML e os arquivos que ele referencia retornam `200`:

```bash
curl -I https://<APP_DOMAIN>/
curl -I https://<APP_DOMAIN>/assets/<hash-atual>.js
curl -I https://<APP_DOMAIN>/assets/<hash-atual>.css
```

Após publicar a versão que inclui a política de cache do portal, `index.html`
retorna `Cache-Control: no-cache, no-store, must-revalidate`, enquanto os recursos
fingerprinted em `/assets` podem permanecer em cache por longo prazo com segurança.

## OAuth ChatGPT Falha

Verificar:

- `/.well-known/oauth-protected-resource/mcp` retorna `authorization_servers`;
- `authorization_servers[0]` usa `https://<APP_DOMAIN>`;
- `/.well-known/oauth-authorization-server` retorna `/authorize` e `/token`;
- `/.well-known/openid-configuration` retorna o mesmo issuer público;
- `/.well-known/jwks` retorna pelo menos uma chave pública;
- `OAuth__ChatGptRedirectUri` é exatamente o callback exibido pelo ChatGPT;
- `OAuth__Audience` é `https://<APP_DOMAIN>/mcp` ou foi omitido para derivar de `APP_DOMAIN`;
- `OAuth__RequireHttpsMetadata=true` em produção.

Comandos:

```bash
curl https://<APP_DOMAIN>/.well-known/oauth-protected-resource/mcp
curl https://<APP_DOMAIN>/.well-known/oauth-authorization-server
curl https://<APP_DOMAIN>/.well-known/openid-configuration
curl https://<APP_DOMAIN>/.well-known/jwks
curl https://<APP_DOMAIN>/api/status
```

Se o ChatGPT informar falha ao obter configuração OAuth, valide que o domínio público está acessível a partir da internet e que o certificado HTTPS já foi emitido pelo Caddy.

## `401 missing_api_key`

Causa provável:

- `McpServerSecurity:RequireApiKey=true`;
- requisição sem header `X-Mcp-Api-Key`.

Correção:

```bash
curl https://<APP_DOMAIN>/mcp \
  -H "X-Mcp-Api-Key: <API_KEY>" \
  -H "Content-Type: application/json"
```

## `401 missing_or_invalid_jwt`

Causas prováveis:

- JWT ausente;
- issuer incorreto;
- audience incorreta;
- token expirado;
- assinatura inválida;
- token emitido antes de configurar o callback/cliente atual.

Verificar:

```env
OAuth__Issuer=https://<APP_DOMAIN>
OAuth__Audience=https://<APP_DOMAIN>/mcp
OAuth__ChatGptRedirectUri=<CALLBACK_EXATO_DO_CHATGPT>
```

## `403 moodle_connection_not_linked`

Causa:

- JWT válido, mas usuário local não possui conexão Moodle vinculada.

Correção:

- usuário deve acessar o portal e conectar uma conta Moodle;
- ou administrador deve registrar credenciais pelo endpoint administrativo.

## Erro Ao Resolver Usuário Moodle

Causas prováveis:

- token Moodle inválido;
- serviço Moodle incorreto;
- usuário sem permissão em `core_webservice_get_site_info`;
- URL base Moodle incorreta.

Verificar:

```env
MoodleApi__LoginService=moodle_mobile_app
MoodleApi__BaseUrl=<MOODLE_BASE_URL>
```

## Instabilidade Ou Lentidão Do Moodle

As chamadas Moodle usam timeout, retry para falhas transitórias HTTP e circuit breaker configuráveis por gateway.

Configurações úteis:

```env
MoodleApi__HttpTimeoutSeconds=30
MoodleApi__HttpRetryCount=2
MoodleApi__CircuitBreakerHandledEventsAllowedBeforeBreaking=5
MoodleApi__CircuitBreakerDurationSeconds=30
MoodleProxy__HttpTimeoutSeconds=30
MoodleProxy__HttpRetryCount=2
MoodleProxy__CircuitBreakerHandledEventsAllowedBeforeBreaking=5
MoodleProxy__CircuitBreakerDurationSeconds=30
```

Se o Moodle estiver intermitente, reduza volume de chamadas, valide a saúde do Moodle/proxy e evite aumentar retries sem revisar impacto de carga no servidor.

## `429 rate_limited`

Causas prováveis:

- muitas chamadas MCP na mesma janela para o mesmo usuário/conector;
- automação ou cliente repetindo `tools/call` em loop;
- limites baixos demais para o cenário de teste.

Verificar:

```env
RateLimiting__WindowSeconds=60
RateLimiting__McpPermitLimit=120
RateLimiting__PortalAuthPermitLimit=12
RateLimiting__AdminApiPermitLimit=30
```

Antes de aumentar limites em produção, valide se o Moodle está saudável e se o cliente não está repetindo chamadas desnecessárias.

## Falhas De Autorização Auditadas

Eventos relevantes são gravados em `moodle_audit_logs` com:

- `Status = authorization_failed`;
- `ErrorCode` com o motivo;
- `CorrelationId`.

Não consulte ou compartilhe tokens/senhas durante troubleshooting.
