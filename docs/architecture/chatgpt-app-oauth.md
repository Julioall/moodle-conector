# OAuth para ChatGPT Apps

## Decisão

O conector usa um authorization server OAuth embutido no `MoodleConnector.Presentation`, implementado com OpenIddict Server.

Essa decisão remove a dependência de um servidor de identidade externo para o fluxo ChatGPT Apps e mantém o contrato esperado pela arquitetura MCP:

- authorization code flow com PKCE `S256`;
- metadata OAuth pública;
- tokens Bearer enviados pelo ChatGPT para `/mcp`;
- validação de issuer, audience, assinatura, expiração e scopes em toda chamada MCP;
- desafio OAuth em HTTP `WWW-Authenticate` e em `_meta["mcp/www_authenticate"]` nos resultados MCP;
- storage persistente de aplicações, authorizations e tokens no banco do conector;
- certificados RSA persistidos em volume local para assinar/criptografar tokens sem invalidar sessões a cada restart.

## Topologia

Como o projeto tem VPS e domínio, o mesmo host público expõe o MCP, o portal e o broker OAuth:

```text
ChatGPT
  -> GET https://<APP_DOMAIN>/.well-known/oauth-protected-resource/mcp
  -> GET https://<APP_DOMAIN>/.well-known/oauth-authorization-server
  -> GET https://<APP_DOMAIN>/.well-known/openid-configuration
  -> GET https://<APP_DOMAIN>/.well-known/jwks
  -> GET https://<APP_DOMAIN>/authorize
  -> POST https://<APP_DOMAIN>/token
  -> POST https://<APP_DOMAIN>/mcp
```

Configuração recomendada:

```env
APP_DOMAIN=moodle-conector.<dominio>
COMPOSE_PROFILES=https
CADDYFILE=./Caddyfile

McpServerSecurity__RequireJwt=true
McpServerSecurity__RequireApiKey=false

OAuth__Issuer=https://moodle-conector.<dominio>
OAuth__Audience=https://moodle-conector.<dominio>/mcp
OAuth__ChatGptClientId=moodle
OAuth__ChatGptRedirectUri=https://chatgpt.com/connector/oauth/<callback_id>
OAuth__ScopeName=moodle-mcp-audience
OAuth__RequireHttpsMetadata=true
OAuth__KeyStoragePath=/app/data/oauth
```

Quando `OAuth__Issuer` e `OAuth__Audience` ficam vazios, a aplicação deriva ambos de `APP_DOMAIN`.

## Contrato MCP/OAuth

`/.well-known/oauth-protected-resource/mcp` deve retornar:

- `resource = https://<APP_DOMAIN>/mcp`;
- `authorization_servers = ["https://<APP_DOMAIN>"]`;
- `scopes_supported` contendo `openid`, `profile`, `email`, `offline_access` e `moodle-mcp-audience`;
- `bearer_methods_supported = ["header"]`.

`/.well-known/oauth-authorization-server` deve retornar:

- `issuer = https://<APP_DOMAIN>`;
- `authorization_endpoint = https://<APP_DOMAIN>/authorize`;
- `token_endpoint = https://<APP_DOMAIN>/token`;
- `jwks_uri = https://<APP_DOMAIN>/.well-known/jwks`;
- suporte a `authorization_code`, `refresh_token`, `offline_access` e PKCE `S256`.

`/.well-known/openid-configuration` e `/.well-known/jwks` são publicados pelo OpenIddict para descoberta OIDC/JWT padrão. O `JwtBearer` usa essa cadeia em produção para resolver issuer e chaves de assinatura.

`tools/list` anuncia `_meta.securitySchemes` com OAuth 2.0 quando JWT está ativo.

Chamadas `tools/call` sem token válido retornam resultado MCP com `_meta["mcp/www_authenticate"]`, permitindo que a UI do ChatGPT inicie o vínculo OAuth.

## Fluxo

```text
1. ChatGPT descobre o protected resource do /mcp.
2. ChatGPT abre /authorize com client_id, redirect_uri, scope, state, code_challenge e resource.
3. O portal autentica o usuário local, se necessário.
4. OpenIddict emite authorization code.
5. ChatGPT troca code por access token em /token usando PKCE.
6. ChatGPT chama /mcp com Authorization: Bearer <token>.
7. O MCP valida token e resolve a conta/conexão Moodle do usuário.
```

## Escopo Inicial

Incluído:

- um cliente OAuth predefinido para ChatGPT;
- login local simples no portal;
- authorization code + refresh token;
- scopes MCP mínimos;
- tokens próprios do conector;
- persistência via EF Core/PostgreSQL;
- API key opcional para automações internas.
- rate limit básico nos endpoints de cadastro/login/admin e no `/mcp` por usuário/conector.

Fora do primeiro corte:

- federação corporativa;
- múltiplos tenants;
- Dynamic Client Registration obrigatório;
- login social;
- painel completo de IdP.

Esses itens podem ser adicionados depois sem alterar o contrato MCP se o broker continuar publicando metadata correta e emitindo tokens com audience/resource do `/mcp`.

## Validação

Checklist para VPS:

```bash
curl https://<APP_DOMAIN>/health
curl https://<APP_DOMAIN>/.well-known/oauth-protected-resource/mcp
curl https://<APP_DOMAIN>/.well-known/oauth-authorization-server
curl https://<APP_DOMAIN>/.well-known/openid-configuration
curl https://<APP_DOMAIN>/.well-known/jwks
```

No ChatGPT App, configure o endpoint MCP:

```text
https://<APP_DOMAIN>/mcp
```

Copie o redirect URI gerado pelo ChatGPT para:

```env
OAuth__ChatGptRedirectUri=<valor_exato_do_chatgpt>
```

## Referências oficiais

- Apps SDK Authentication: https://developers.openai.com/apps-sdk/build/auth
- Apps SDK MCP server concepts: https://developers.openai.com/apps-sdk/concepts/mcp-server
- OpenIddict Server: https://documentation.openiddict.com/guides/getting-started/creating-your-own-server-instance.html
