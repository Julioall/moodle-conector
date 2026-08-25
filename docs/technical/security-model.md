# Modelo de Segurança

## Autenticação No `/mcp`

O endpoint MCP é `/mcp`.

Métodos suportados:

| Método | Status | Configuração |
| --- | --- | --- |
| JWT Bearer OAuth | Implementado | `McpServerSecurity:RequireJwt` |
| API key | Implementado | `McpServerSecurity:RequireApiKey` |

O padrão recomendado para ChatGPT Apps é:

```env
McpServerSecurity__RequireJwt=true
McpServerSecurity__RequireApiKey=false
```

Quando JWT e API key estão habilitados, qualquer um dos dois autentica a requisição.

## OAuth/JWT

O broker OAuth local roda no `MoodleConnector.Presentation` com OpenIddict Server.

Configurações:

```env
OAuth__Issuer=https://<APP_DOMAIN>
OAuth__Audience=https://<APP_DOMAIN>/mcp
OAuth__ChatGptClientId=moodle
OAuth__ChatGptRedirectUri=https://chatgpt.com/connector/oauth/<callback_id>
OAuth__ScopeName=moodle-mcp-audience
OAuth__RequireHttpsMetadata=true
OAuth__KeyStoragePath=/app/data/oauth
```

O middleware valida:

- emissor;
- audience;
- assinatura;
- expiração;
- issuer signing key;
- vínculo local do usuário antes de executar tools que precisam do Moodle.

Os certificados RSA usados para assinatura e criptografia são persistidos em `OAuth__KeyStoragePath`. Em Docker, esse caminho fica em volume (`app-data`) para evitar invalidação de tokens em restart/rebuild.

O authorization server publica `/.well-known/openid-configuration` e `/.well-known/jwks`, além das metadata MCP/OAuth exigidas pelo ChatGPT Apps. Os scopes emitidos para o fluxo MCP incluem `openid`, `profile`, `email`, `offline_access` e o scope de audience configurado em `OAuth:ScopeName`.

## Portal Local

O portal usa autenticação por cookie HttpOnly com `SameSite=Lax`. Em produção, o cookie é marcado como `Secure`.
As rotas privadas de `/api` exigem a política `portal.session`, que aceita exclusivamente esse
cookie: um Bearer JWT emitido para `/mcp` não funciona como sessão do portal. As exceções públicas
estão documentadas em [fronteiras de autenticação](../architecture/authentication-boundaries.md).

Controles aplicados:

- senha local mínima de 12 caracteres;
- validação de formato e tamanho de e-mail;
- rate limit em cadastro, login, conexão Moodle, endpoint administrativo e `/mcp`;
- senhas Moodle preservadas exatamente como informadas, sem `Trim()` no segredo.

## Proxy E Headers De Transporte

O container ASP.NET publica sua porta somente em loopback do host. O Caddy é a única entrada
pública e encaminha protocolo, host e endereço do cliente; por isso o app pode reconhecer HTTPS
e acrescentar `Strict-Transport-Security` como defesa em profundidade. O Caddy também remove o
header `Server` do upstream. A certificação pós-deploy exige HSTS e rejeita exposição de Kestrel.

## Rate Limiting

O `/mcp` aplica rate limit por usuário/conector depois da resolução de credenciais. A chave de partição usa `connector_client_id` quando disponível, `sub`/`NameIdentifier` quando aplicável, hash da API key como fallback autenticado, ou IP para chamadas de descoberta sem credencial.

Configurações:

- `RateLimiting:WindowSeconds`;
- `RateLimiting:AppAuthPermitLimit`;
- `RateLimiting:AdminApiPermitLimit`;
- `RateLimiting:McpPermitLimit`.

## API Keys

Header da API key:

```text
X-Mcp-Api-Key: <API_KEY>
```

As API keys são geradas no cadastro/rotação do conector e armazenadas como hash em `connector_clients.ApiKeyHash`.

Quando uma API key é válida:

- o principal recebe `connector_client_id`;
- se `CanWrite=true`, recebe o scope legado `moodle.write`.

O endpoint de perfil `/api/account/me` não retorna o valor bruto da API key; apenas informa se a conta possui API key cadastrada.

## Vínculo Moodle

JWT válido sem conexão Moodle local vinculada recebe:

```text
403 Forbidden
error: moodle_connection_not_linked
```

O vínculo é feito pela conta local e pelo `ConnectorClientId`.

## Resolução Do Usuário Moodle

Ordem de resolução:

1. claims `moodle_user_id`, `moodle_userid`, `moodle_user` ou `userid`;
2. conexão Moodle atual;
3. chamada `core_webservice_get_site_info`.

## Escopos Planejados

| Escopo | Uso |
| --- | --- |
| `moodle.read.courses` | Leituras de cursos. |
| `moodle.write.messages` | Envio de mensagens/comunicados. |
| `moodle.write.assignments.feedback` | Feedback de atividades. |
| `moodle.write.assignments.grade` | Lançamento/ajuste de nota. |
| `moodle.write.course_content` | Conteúdo de curso. |
| `moodle.read.forums` | Leitura de fóruns e participação observável. |
| `moodle.write.forums` | Publicação confirmada em fórum. |

`moodle.admin` não é registrado pelo broker OAuth. Ações administrativas dependem de platform permission e política server-side.

## Proteção De Segredos

Credenciais Moodle e API key retornável ao usuário são protegidas por `AesGcmConnectorSecretProtector`.

Em ambiente que não seja `Development` ou `Testing`, o host falha no startup se a connection string
Postgres, a chave `ConnectorSecrets:EncryptionKeyBase64`, a chave `AdminApi:ApiKey` ou
`MEDIATR_LICENSE_KEY` estiverem ausentes ou contiverem placeholders conhecidos. A chave de
criptografia também deve ser Base64 válido de exatamente 32 bytes. Isso impede que um `.env.example`
seja promovido acidentalmente e torna explícita a licença exigida pelo MediatR 13 em produção.

Não devem ser logados:

- JWT completo;
- API key;
- senha Moodle;
- token Moodle;
- refresh token OAuth;
- connection string com senha.

## Token Moodle De Leitura

`MoodleApi:ServiceToken` pode existir para leituras operacionais, mas seu uso não é automático por padrão.

- `MoodleApi:AllowServiceTokenForReadOnlyQueries=false` (padrão): consultas usam token da conexão Moodle vinculada ao usuário autenticado.
- `MoodleApi:AllowServiceTokenForReadOnlyQueries=true`: consultas read-only podem usar `ServiceToken` global.

Esse controle evita fallback silencioso para token global quando a política da instituição exige rastreabilidade por usuário.

## Resiliência Das Chamadas Moodle

Os gateways `MoodleApi` e `MoodleProxy` aplicam timeout, retry de falhas transitórias HTTP e circuit breaker com configuração independente.

- retries devem permanecer baixos para não amplificar instabilidade do Moodle;
- `CircuitBreakerHandledEventsAllowedBeforeBreaking=0` desabilita o circuit breaker quando necessário para diagnóstico;
- timeouts e circuit breaker não substituem validação de escopo, vínculo de usuário ou confirmação humana em tools de escrita.

## Estado Atual Das Escritas

Escritas reais controladas estão implementadas para fórum, mensagens, nota/feedback, conteúdo e
o executor universal classificado. A configuração versionada atual habilita as flags de escrita,
mas a promoção à VPS grava cada uma explicitamente a partir de variáveis `FEATURES_*`; o exemplo
de ambiente mantém todas desabilitadas para rollout manual seguro.

Toda escrita exige `CanWrite`, permissão/escopo aplicável, capability Moodle, prévia, confirmação
literal, expiração, execução única/idempotente e auditoria. O fluxo de pending actions é obrigatório
para qualquer escrita nova.
