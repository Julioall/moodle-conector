<p>
  <img src="public/logo.png" alt="Moodle Connector" width="720">
</p>

[![CI](https://github.com/Julioall/moodle-conector/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Julioall/moodle-conector/actions/workflows/ci.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)

Conector MCP nÃ£o oficial para Moodle, criado para conectar o ChatGPT diretamente a vÃ¡rios ambientes Moodle. A ideia Ã© permitir que professores, tutores, monitores e equipes acadÃªmicas consultem cursos, alunos, notas, pendÃªncias e informaÃ§Ãµes operacionais sem precisar alternar entre vÃ¡rias telas e vÃ¡rios AVAs.

**Status: Release Candidate**

- OperaÃ§Ãµes de leitura: estÃ¡veis e cobertas por testes.
- Escritas controladas: disponÃ­veis atrÃ¡s de feature flags, confirmaÃ§Ã£o humana e auditoria.
- Arquitetura SKILL/Registry/Exposure: pronta para evoluÃ§Ã£o funcional.
- OtimizaÃ§Ã£o cognitiva da superfÃ­cie MCP: experimental e deferida enquanto nÃ£o houver quota de certificaÃ§Ã£o.

O projeto aceita contribuiÃ§Ãµes, melhorias, correÃ§Ãµes, novas tools, testes e revisÃµes de seguranÃ§a.

O conector jÃ¡ possui escritas confirmadas e limitadas, como publicaÃ§Ã£o em fÃ³rum, mensagens individuais e nota/feedback de tarefa. Esses fluxos usam `PendingAction`, confirmaÃ§Ã£o literal, escopo de escrita, conexÃ£o `CanWrite` e auditoria; a permissÃ£o Moodle contextual continua obrigatÃ³ria. As flags de escrita sÃ£o configurÃ¡veis por ambiente e devem ser revisadas conforme a polÃ­tica de menor privilÃ©gio. Escrita geral de conteÃºdo, broadcast e agendamento nÃ£o estÃ£o implementados.

> Este projeto nÃ£o Ã© oficial do Moodle HQ. Ele Ã© uma integraÃ§Ã£o independente baseada em MCP, ASP.NET Core, OAuth e APIs WebService do Moodle.

## Em 20 segundos

O Moodle Connector transforma o ChatGPT em uma interface operacional para Moodle:

**Professor:** â€œQuais atividades ainda preciso corrigir?â€

**Moodle Connector:** identifica seus cursos, consulta assignments, encontra submissÃµes pendentes e organiza o resultado por turma e prazo.

**Professor:** â€œAvise os alunos que ainda nÃ£o entregaram.â€

**Moodle Connector:** encontra a atividade, resolve os alunos pendentes, prepara as mensagens, solicita confirmaÃ§Ã£o explÃ­cita e sÃ³ entÃ£o envia.

Fluxos de acompanhamento, risco, mensagens, correÃ§Ã£o e relatÃ³rios seguem o mesmo princÃ­pio: contexto Moodle primeiro, aÃ§Ã£o sensÃ­vel somente apÃ³s revisÃ£o humana.

## Objetivo

O Moodle Connector MCP permite que usuÃ¡rios autorizados consultem dados do Moodle e usem fluxos existentes de prÃ©via/confirmaÃ§Ã£o para mensagens individuais, publicaÃ§Ã£o em fÃ³rum e nota/feedback individual. Outras alteraÃ§Ãµes acadÃªmicas permanecem planejadas e nÃ£o devem ser inferidas dessas capabilities.

Diretriz central:

- tools somente leitura executam imediatamente;
- tools de escrita nÃ£o executam mudanÃ§a direta na primeira chamada;
- escritas seguem o fluxo `prepare_*` e `confirm_*`;
- dados acadÃªmicos e pessoais devem ser tratados com mÃ­nimo necessÃ¡rio.

## Tutorial

### 1. Acesse o app

Acesse:

```text
https://novascript.com.br/
```

No app do Moodle Connector, crie sua conta ou entre com uma conta jÃ¡ cadastrada.

<p>
  <img src="public/Captura%201.png" alt="Tela de cadastro do Moodle Connector" width="720">
</p>

Se vocÃª jÃ¡ tiver conta, use a tela de entrada.

<p>
  <img src="public/Captura%2013.png" alt="Tela de login do Moodle Connector" width="720">
</p>

### 2. Cadastre seus Moodles

Depois do cadastro, adicione um ou mais ambientes Moodle.

Para cada Moodle, informe:

- um alias fÃ¡cil de lembrar, como `goias`, `nacional`, `ctm` ou `faculdade`;
- a URL base do Moodle;
- o usuÃ¡rio do Moodle;
- a senha do Moodle;
- se esse Moodle deve ser o padrÃ£o;
- se a permissÃ£o de escrita deve ficar habilitada.

O alias ajuda o ChatGPT a escolher o Moodle correto durante a conversa. Por exemplo:

```text
Liste meus cursos no Moodle goias.
```

```text
Gere um relatÃ³rio de notas no Moodle nacional.
```

VocÃª pode cadastrar vÃ¡rios Moodles e alternar entre eles pelo prompt.

<p>
  <img src="public/Captura%202.png" alt="Tela para adicionar um Moodle no Moodle Connector" width="720">
</p>

Ao finalizar, o app mostra os Moodles cadastrados e a chave do conector quando ela estiver habilitada para o seu modo de uso.

<p>
  <img src="public/Captura%203.png" alt="Tela de Moodle configurado no Moodle Connector" width="720">
</p>

### 3. Conecte o ChatGPT ao Moodle Connector

No ChatGPT:

1. Abra **ConfiguraÃ§Ãµes**.
2. Entre em **Aplicativos**.
3. Clique em **ConfiguraÃ§Ãµes avanÃ§adas**.
4. Escolha **Criar aplicativo** ou **Adicionar app personalizado**.
5. Informe o nome do app, por exemplo:

```text
Moodle
```

<p>
  <img src="public/Captura%205.png" alt="Menu de configuracoes do ChatGPT" width="720">
</p>

<p>
  <img src="public/Captura%207.png" alt="Tela de aplicativos habilitados no ChatGPT" width="720">
</p>

6. Em conexÃ£o, use a URL:

```text
https://novascript.com.br/mcp
```

7. Em autenticaÃ§Ã£o, escolha **OAuth**.
8. Quando solicitado, entre com sua conta do Moodle Connector.
9. Autorize o app.

<p>
  <img src="public/Captura%2011.png" alt="ConfiguraÃ§Ã£o do app Moodle no ChatGPT" width="720">
</p>

<p>
  <img src="public/Captura%2012.png" alt="AutorizaÃ§Ã£o para adicionar Moodle ao ChatGPT" width="720">
</p>

Depois disso, o ChatGPT poderÃ¡ usar o conector para consultar os Moodles cadastrados na sua conta.

<p>
  <img src="public/Captura%2014.png" alt="Moodle conectado ao ChatGPT" width="720">
</p>

### 4. Como usar no ChatGPT

Depois de conectar, vocÃª pode pedir coisas como:

```text
Liste todos os meus cursos em andamento.
```

```text
Consulte o curso 29972 no Moodle goias.
```

```text
Mostre as atividades pendentes de correÃ§Ã£o dessa turma.
```

```text
Gere um relatÃ³rio de notas para este curso.
```

```text
Busque os cursos do Moodle nacional com o termo desenvolvimento de sistemas.
```

Quando houver mais de um Moodle cadastrado, informe o alias no pedido para evitar ambiguidade.

O conector tambÃ©m pode consultar o acervo pedagÃ³gico e manter preferÃªncias durÃ¡veis:

```text
Consulte as orientaÃ§Ãµes pedagÃ³gicas sobre avaliaÃ§Ã£o formativa antes de sugerir o feedback desta atividade.
```

```text
Lembre que prefiro feedbacks objetivos, com evidÃªncia e prÃ³ximo passo. NÃ£o inclua dados de alunos nessa memÃ³ria.
```

```text
Liste minhas memÃ³rias sobre correÃ§Ã£o e remova a que eu indicar pelo identificador.
```

MemÃ³rias nÃ£o sÃ£o lugar para senhas, tokens, chaves, segredos, notas ou dados pessoais
de estudantes. A consulta pedagÃ³gica usa somente os guias locais publicados com a
aplicaÃ§Ã£o; ela nÃ£o pesquisa a internet e deve apoiar, nÃ£o substituir, a revisÃ£o humana.
O servidor MCP nÃ£o vÃª a conversa por conta prÃ³pria: memÃ³rias e orientaÃ§Ãµes sÃ³ sÃ£o
consultadas ou registradas quando a IA ou o cliente chama as tools correspondentes e
envia o contexto necessÃ¡rio nos argumentos.

### 5. Sobre escrita e aÃ§Ãµes sensÃ­veis

As escritas controladas jÃ¡ estÃ£o disponÃ­veis para fluxos especÃ­ficos e devem seguir um fluxo seguro:

1. a IA prepara uma prÃ©via da aÃ§Ã£o;
2. o professor ou usuÃ¡rio responsÃ¡vel revisa;
3. o sistema pede uma confirmaÃ§Ã£o explÃ­cita;
4. somente depois disso a aÃ§Ã£o pode ser executada.

Hoje existem fluxos reais de prÃ©via/confirmaÃ§Ã£o para publicaÃ§Ã£o em fÃ³rum, mensagens individuais e nota/feedback de tarefa. Mensagens exigem `PendingAction`, confirmaÃ§Ã£o, escopo `moodle.write`, conexÃ£o `CanWrite` e `MessagesWriteEnabled=true`. Nota individual exige tambÃ©m `moodle.write.assignments.grade` e `AssignmentGradeWriteEnabled=true`. No `appsettings.json` versionado atual essas flags estÃ£o habilitadas; operadores devem revisÃ¡-las e podem sobrescrevÃª-las por ambiente conforme sua polÃ­tica de menor privilÃ©gio.

## Arquitetura

```text
ChatGPT / Cliente MCP
        |
        | HTTP streamable MCP + OAuth 2.1/PKCE
        v
MoodleConnector.Presentation
        |
        | OpenIddict / JWT Bearer / app local
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

- `Presentation`: expoe `/mcp`, app, endpoints OAuth, healthcheck e tools MCP.
- `Application`: concentra queries, commands, contratos de tools e regras de aplicaÃ§Ã£o.
- `Domain`: contÃ©m entidades e classificaÃ§Ãµes de domÃ­nio, como risco e pending actions.
- `Infrastructure`: implementa banco, gateway Moodle, cache, storage OAuth e repositorios.

## Stack

- Compatibilidade orientada pelas funÃ§Ãµes Web Service habilitadas em cada conexÃ£o Moodle
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

Por padrÃ£o, a aplicaÃ§Ã£o fica disponÃ­vel em:

```text
http://127.0.0.1:8787
```

Para usar com ChatGPT Apps, exponha a aplicaÃ§Ã£o por domÃ­nio pÃºblico HTTPS na VPS. O ChatGPT nÃ£o completa OAuth contra `localhost` puro.

## Testes

```bash
dotnet test MoodleConnector.slnx
```

A suÃ­te cobre contratos MCP, autenticaÃ§Ã£o JWT/API key, OAuth local, cadastro/login do app, pending actions, resoluÃ§Ã£o de usuÃ¡rio Moodle e tools implementadas.

## Endpoints

- `POST /mcp`: endpoint MCP streamable HTTP.
- `GET /health`: healthcheck.
- `GET /api/status`: status da API e configuraÃ§Ã£o de autenticaÃ§Ã£o.
- `GET /.well-known/oauth-protected-resource/mcp`: metadata OAuth Protected Resource para descoberta pelo ChatGPT.
- `GET /.well-known/oauth-authorization-server`: metadata do authorization server local.
- `GET /.well-known/openid-configuration`: discovery OIDC publicado pelo OpenIddict.
- `GET /.well-known/jwks`: chaves publicas usadas para validar JWTs.
- `GET|POST /authorize`: inicio do authorization code flow.
- `POST /token`: troca de code/refresh token feita pelo OpenIddict.

Producao:

```text
https://<APP_DOMAIN>/mcp
```

No ambiente pÃºblico atual:

```text
https://novascript.com.br/mcp
```

## Autenticacao

O `/mcp` usa OAuth/JWT como padrÃ£o para ChatGPT Apps:

- JWT Bearer via `Authorization: Bearer <JWT>`.
- API key via `X-Mcp-Api-Key` apenas quando `McpServerSecurity__RequireApiKey=true`.

ConfiguraÃ§Ã£o recomendada:

```text
APP_DOMAIN=moodle-conector.seu-dominio.com
CLARIS_DOMAIN=claris.seu-dominio.com
PUBLIC_PROXY_NETWORK=novascript-proxy
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

Quando `OAuth__Issuer` e `OAuth__Audience` ficam vazios, a aplicaÃ§Ã£o deriva os valores de `APP_DOMAIN`.

O app local usa cookie HttpOnly/SameSite, senha mÃ­nima de 12 caracteres e rate limit nos endpoints de cadastro/login. O `/mcp` tambÃ©m aplica rate limit por usuÃ¡rio/conector. Em produÃ§Ã£o, `OAuth__RequireHttpsMetadata=true` forÃ§a issuer, audience e callback HTTPS.

## Variaveis De Ambiente

Use `.env.example` como base. Principais grupos:

```text
APP_PORT=8787
APP_DOMAIN=<APP_DOMAIN>
CLARIS_DOMAIN=<CLARIS_DOMAIN>
PUBLIC_PROXY_NETWORK=novascript-proxy
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
RateLimiting__AppAuthPermitLimit=12
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

1. `prepare_*`: valida a intenÃ§Ã£o, monta uma prÃ©via, grava uma aÃ§Ã£o pendente e retorna o texto exato de confirmaÃ§Ã£o.
2. `confirm_*`: recebe o `pendingActionId`, valida usuÃ¡rio, escopo, expiraÃ§Ã£o e texto de confirmaÃ§Ã£o, e sÃ³ entÃ£o executa a aÃ§Ã£o.

Contrato de aÃ§Ã£o pendente:

```json
{
  "status": "pending_confirmation",
  "pendingActionId": "00000000-0000-0000-0000-000000000000",
  "toolName": "prepare_demo_action",
  "riskLevel": "HumanConfirmedWrite",
  "preview": {},
  "confirmationText": "CONFIRMAR ...",
  "expiresAt": "2026-05-31T00:15:00Z"
}
```

O projeto mantÃ©m tools demo e tambÃ©m possui escritas reais confirmadas para fÃ³rum, mensagens individuais e nota/feedback. A conexÃ£o `CanWrite`, o escopo aplicÃ¡vel, a capability Moodle, a prÃ©via, a confirmaÃ§Ã£o e a auditoria continuam obrigatÃ³rios. As flags de mensagens e notas individuais iniciam desabilitadas e sÃ£o verificadas pelos handlers antes de preparar ou confirmar uma escrita.

## Tools Existentes

Tools universais de leitura:

| Tool | DescriÃ§Ã£o | Status |
| --- | --- | --- |
| `moodle_diagnose_connection` | Descobre o perfil tÃ©cnico da conexÃ£o, sem expor segredos. | Implementada |
| `moodle_list_functions` | Lista as funÃ§Ãµes Web Service habilitadas para o token. | Implementada |
| `moodle_check_function` | Verifica disponibilidade e classificaÃ§Ã£o de risco local. | Implementada |
| `moodle_describe_function` | Descreve disponibilidade e classificaÃ§Ã£o de risco local. | Implementada |
| `moodle_list_available_flows` | Mostra estratÃ©gias selecionadas e funÃ§Ãµes ausentes por fluxo. | Implementada |
| `moodle_execute_read` | Executa somente funÃ§Ãµes explicitamente classificadas como leitura segura. | Implementada |
| `moodle_prepare_write` | Cria prÃ©via de escrita controlada sem chamar o Moodle. | Implementada; depende de `UniversalMoodleWriteEnabled=true` |
| `moodle_confirm_write` | Executa uma prÃ©via confirmada uma Ãºnica vez. | Implementada; depende de `UniversalMoodleWriteEnabled=true` |

As chamadas universais usam `POST /webservice/rest/server.php`, serializam arrays e objetos no formato nativo do Moodle e nunca inserem o token na URL. Uma funÃ§Ã£o descoberta, mas ainda nÃ£o classificada no catÃ¡logo local, Ã© tratada como `Unknown` e recusada pela tool de execuÃ§Ã£o. `moodle_prepare_write` e `moodle_confirm_write` sÃ³ permitem funÃ§Ãµes explicitamente classificadas como escrita controlada, com `UniversalMoodleWriteEnabled=true`, `CanWrite`, confirmaÃ§Ã£o literal, auditoria e execuÃ§Ã£o Ãºnica; funÃ§Ãµes destrutivas continuam bloqueadas.

Leitura:

| Tool | DescriÃ§Ã£o | Status |
| --- | --- | --- |
| `search` | Busca cursos autorizados no formato padrÃ£o MCP connector/company knowledge. | Implementada |
| `fetch` | Retorna um curso autorizado no formato padrÃ£o MCP connector/company knowledge. | Implementada |
| `list_my_courses` | Lista cursos vinculados ao usuÃ¡rio autenticado com metadados bÃ¡sicos. | Implementada |
| `list_courses` | Alias em ingles de `list_my_courses`. | Implementada |
| `search_courses` | Busca cursos vinculados por termo, nome, categoria, `courseId`, `shortName` ou `idNumber`. | Implementada |
| `search_courses` | Alias em ingles de `search_courses`. | Implementada |
| `get_course` | Consulta metadados bÃ¡sicos de um curso vinculado. | Implementada |
| `get_course` | Alias em ingles de `get_course`. | Implementada |
| `get_pedagogical_guidelines` | Pesquisa os guias pedagÃ³gicos locais antes de tarefas educacionais. | Implementada |

Estado interno (nÃ£o altera o Moodle):

| Tool | DescriÃ§Ã£o | Status |
| --- | --- | --- |
| `manage_user_memory` | Salva, lista ou remove memÃ³rias privadas e durÃ¡veis do usuÃ¡rio. A remoÃ§Ã£o Ã© destrutiva para esse estado interno. | Implementada |

Demo de pending action:

| Tool | DescriÃ§Ã£o | Status |
| --- | --- | --- |
| `prepare_demo_action` | Cria aÃ§Ã£o pendente demonstrativa. NÃ£o executa escrita real no Moodle. | Implementada como demo |
| `confirm_demo_action` | Confirma aÃ§Ã£o pendente demonstrativa. NÃ£o executa escrita real no Moodle. | Implementada como demo |

## SeguranÃ§a

- Nao registrar senhas, JWTs, API keys, tokens Moodle, refresh tokens ou links privados com token em logs.
- Nao expor dados de estudantes sem necessidade operacional clara.
- Preferir respostas agregadas quando possivel.
- Escritas exigem confirmacao humana quando aplicavel.
- Tools de escrita devem validar usuÃ¡rio, escopo, vÃ­nculo Moodle, expiraÃ§Ã£o, idempotÃªncia e texto de confirmaÃ§Ã£o.
- Payloads de auditoria devem ser sanitizados antes de persistir.

## DocumentaÃ§Ã£o Detalhada

- Ãndice da documentaÃ§Ã£o: `docs/README.md`

- TODO operacional: `TODO.md`
- Roadmap funcional canÃ´nico, organizado pelas jornadas de tutor, monitor, corpo pedagÃ³gico e operaÃ§Ã£o: `docs/roadmap.md`
- Setup local: `docs/technical/local-setup.md`
- Setup Moodle Webservice: `docs/technical/moodle-webservice-setup.md`
- CatÃ¡logo MCP: `docs/technical/mcp-tools-catalog.md`
- Modelo de seguranca: `docs/technical/security-model.md`
- Checklist de release: `docs/security/release-checklist.md`
- Modelo de auditoria: `docs/technical/audit-model.md`
- OAuth ChatGPT Apps: `docs/architecture/chatgpt-app-oauth.md`
- Deploy: `docs/operations/deploy-runbook.md`
- Troubleshooting: `docs/operations/troubleshooting-runbook.md`
- Auth e escopos: `docs/security/auth-and-scopes.md`
- Contrato de resposta MCP: `docs/technical/tool-response-contract.md`

