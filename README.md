<p>
  <img src="public/logo.png" alt="Moodle Connector" width="720">
</p>

# Moodle Connector MCP

Conector MCP não oficial para Moodle, criado para conectar o ChatGPT diretamente a vários ambientes Moodle. A ideia é permitir que professores, tutores, monitores e equipes acadêmicas consultem cursos, alunos, notas, pendências e informações operacionais sem precisar alternar entre várias telas e vários AVAs.

Este projeto ainda está em construção e aceita contribuições. Sugestões, melhorias, correções, novas tools, testes e revisões de segurança são bem-vindos.

O conector já possui escritas confirmadas e limitadas, como publicação em fórum, mensagens individuais e nota/feedback de tarefa. Elas dependem de permissão explícita, confirmação humana, auditoria, controle de escopo, rastreabilidade e bloqueios por capability/feature flag. Escrita geral de conteúdo, broadcast e agendamento não estão implementados.

> Este projeto não é oficial do Moodle HQ. Ele é uma integração independente baseada em MCP, ASP.NET Core, OAuth e APIs WebService do Moodle.

## Objetivo

O Moodle Connector MCP permite que usuários autorizados consultem dados do Moodle por tools MCP e, em fases futuras, preparem ações sensíveis como mensagens, feedbacks, notas e alterações acadêmicas sempre com prévia, auditoria e confirmação humana.

Diretriz central:

- tools somente leitura executam imediatamente;
- tools de escrita não executam mudança direta na primeira chamada;
- escritas seguem o fluxo `prepare_*` e `confirm_*`;
- dados acadêmicos e pessoais devem ser tratados com mínimo necessário.

## Tutorial

### 1. Acesse o portal

Acesse:

```text
https://novascript.com.br/
```

No portal do Moodle Connector, crie sua conta ou entre com uma conta já cadastrada.

<p>
  <img src="public/Captura%201.png" alt="Tela de cadastro do Moodle Connector" width="720">
</p>

Se você já tiver conta, use a tela de entrada.

<p>
  <img src="public/Captura%2013.png" alt="Tela de login do Moodle Connector" width="720">
</p>

### 2. Cadastre seus Moodles

Depois do cadastro, adicione um ou mais ambientes Moodle.

Para cada Moodle, informe:

- um alias fácil de lembrar, como `goias`, `nacional`, `ctm` ou `faculdade`;
- a URL base do Moodle;
- o usuário do Moodle;
- a senha do Moodle;
- se esse Moodle deve ser o padrão;
- se a permissão de escrita deve ficar habilitada.

O alias ajuda o ChatGPT a escolher o Moodle correto durante a conversa. Por exemplo:

```text
Liste meus cursos no Moodle goias.
```

```text
Gere um relatório de notas no Moodle nacional.
```

Você pode cadastrar vários Moodles e alternar entre eles pelo prompt.

<p>
  <img src="public/Captura%202.png" alt="Tela para adicionar um Moodle no Moodle Connector" width="720">
</p>

Ao finalizar, o portal mostra os Moodles cadastrados e a chave do conector quando ela estiver habilitada para o seu modo de uso.

<p>
  <img src="public/Captura%203.png" alt="Tela de Moodle configurado no Moodle Connector" width="720">
</p>

### 3. Conecte o ChatGPT ao Moodle Connector

No ChatGPT:

1. Abra **Configurações**.
2. Entre em **Aplicativos**.
3. Clique em **Configurações avançadas**.
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

6. Em conexão, use a URL:

```text
https://novascript.com.br/mcp
```

7. Em autenticação, escolha **OAuth**.
8. Quando solicitado, entre com sua conta do Moodle Connector.
9. Autorize o app.

<p>
  <img src="public/Captura%2011.png" alt="Configuração do app Moodle no ChatGPT" width="720">
</p>

<p>
  <img src="public/Captura%2012.png" alt="Autorização para adicionar Moodle ao ChatGPT" width="720">
</p>

Depois disso, o ChatGPT poderá usar o conector para consultar os Moodles cadastrados na sua conta.

<p>
  <img src="public/Captura%2014.png" alt="Moodle conectado ao ChatGPT" width="720">
</p>

### 4. Como usar no ChatGPT

Depois de conectar, você pode pedir coisas como:

```text
Liste todos os meus cursos em andamento.
```

```text
Consulte o curso 29972 no Moodle goias.
```

```text
Mostre as atividades pendentes de correção dessa turma.
```

```text
Gere um relatório de notas para este curso.
```

```text
Busque os cursos do Moodle nacional com o termo desenvolvimento de sistemas.
```

Quando houver mais de um Moodle cadastrado, informe o alias no pedido para evitar ambiguidade.

O conector também pode consultar o acervo pedagógico e manter preferências duráveis:

```text
Consulte as orientações pedagógicas sobre avaliação formativa antes de sugerir o feedback desta atividade.
```

```text
Lembre que prefiro feedbacks objetivos, com evidência e próximo passo. Não inclua dados de alunos nessa memória.
```

```text
Liste minhas memórias sobre correção e remova a que eu indicar pelo identificador.
```

Memórias não são lugar para senhas, tokens, chaves, segredos, notas ou dados pessoais
de estudantes. A consulta pedagógica usa somente os guias locais publicados com a
aplicação; ela não pesquisa a internet e deve apoiar, não substituir, a revisão humana.
O servidor MCP não vê a conversa por conta própria: memórias e orientações só são
consultadas ou registradas quando a IA ou o cliente chama as tools correspondentes e
envia o contexto necessário nos argumentos.

### 5. Sobre escrita e ações sensíveis

As ferramentas de escrita ainda estão em desenvolvimento e devem seguir um fluxo seguro:

1. a IA prepara uma prévia da ação;
2. o professor ou usuário responsável revisa;
3. o sistema pede uma confirmação explícita;
4. somente depois disso a ação pode ser executada.

Hoje existem fluxos reais de prévia/confirmação para publicação em fórum, mensagens individuais e nota/feedback de tarefa. `MessagesWriteEnabled` ainda não bloqueia efetivamente todo o fluxo de mensagens, e a flag de nota precisa adotar default seguro; por isso essas escritas devem permanecer desabilitadas na operação até que os bloqueadores P0 do roadmap sejam concluídos.

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

- `Presentation`: expoe `/mcp`, portal, endpoints OAuth, healthcheck e tools MCP.
- `Application`: concentra queries, commands, contratos de tools e regras de aplicação.
- `Domain`: contém entidades e classificações de domínio, como risco e pending actions.
- `Infrastructure`: implementa banco, gateway Moodle, cache, storage OAuth e repositorios.

## Stack

- Moodle 5.0.1 (versão alvo validada)
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
- `GET /.well-known/jwks`: chaves publicas usadas para validar JWTs.
- `GET|POST /authorize`: inicio do authorization code flow.
- `POST /token`: troca de code/refresh token feita pelo OpenIddict.

Producao:

```text
https://<APP_DOMAIN>/mcp
```

No ambiente público atual:

```text
https://novascript.com.br/mcp
```

## Autenticacao

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

## Variaveis De Ambiente

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

O projeto mantém tools demo e também possui escritas reais confirmadas para fórum, mensagens individuais e nota/feedback. A conexão `CanWrite`, o escopo aplicável, a capability Moodle, a prévia, a confirmação e a auditoria continuam obrigatórios. Os defaults e gates ainda não são uniformemente seguros; consulte o backlog P0 em `docs/roadmap.md` antes de habilitar escrita.

## Tools Existentes

Leitura:

| Tool | Descrição | Status |
| --- | --- | --- |
| `search` | Busca cursos autorizados no formato padrão MCP connector/company knowledge. | Implementada |
| `fetch` | Retorna um curso autorizado no formato padrão MCP connector/company knowledge. | Implementada |
| `listar_meus_cursos` | Lista cursos vinculados ao usuário autenticado com metadados básicos. | Implementada |
| `list_courses` | Alias em ingles de `listar_meus_cursos`. | Implementada |
| `buscar_cursos` | Busca cursos vinculados por termo, nome, categoria, `courseId`, `shortName` ou `idNumber`. | Implementada |
| `search_courses` | Alias em ingles de `buscar_cursos`. | Implementada |
| `consultar_curso` | Consulta metadados básicos de um curso vinculado. | Implementada |
| `get_course` | Alias em ingles de `consultar_curso`. | Implementada |
| `consultar_orientacoes_pedagogicas` | Pesquisa os guias pedagógicos locais antes de tarefas educacionais. | Implementada |

Estado interno (não altera o Moodle):

| Tool | Descrição | Status |
| --- | --- | --- |
| `gerenciar_memoria_usuario` | Salva, lista ou remove memórias privadas e duráveis do usuário. A remoção é destrutiva para esse estado interno. | Implementada |

Demo de pending action:

| Tool | Descrição | Status |
| --- | --- | --- |
| `preparar_acao_demo` | Cria ação pendente demonstrativa. Não executa escrita real no Moodle. | Implementada como demo |
| `confirmar_acao_demo` | Confirma ação pendente demonstrativa. Não executa escrita real no Moodle. | Implementada como demo |

## Segurança

- Nao registrar senhas, JWTs, API keys, tokens Moodle, refresh tokens ou links privados com token em logs.
- Nao expor dados de estudantes sem necessidade operacional clara.
- Preferir respostas agregadas quando possivel.
- Escritas exigem confirmacao humana quando aplicavel.
- Tools de escrita devem validar usuário, escopo, vínculo Moodle, expiração, idempotência e texto de confirmação.
- Payloads de auditoria devem ser sanitizados antes de persistir.

## Documentação Detalhada

- TODO operacional: `TODO.md`
- Roadmap funcional canônico, organizado pelas jornadas de tutor, monitor, corpo pedagógico e operação: `docs/roadmap.md`
- Setup local: `docs/technical/local-setup.md`
- Setup Moodle Webservice: `docs/technical/moodle-webservice-setup.md`
- Catálogo MCP: `docs/technical/mcp-tools-catalog.md`
- Modelo de seguranca: `docs/technical/security-model.md`
- Checklist de release: `docs/security/release-checklist.md`
- Modelo de auditoria: `docs/technical/audit-model.md`
- OAuth ChatGPT Apps: `docs/architecture/chatgpt-app-oauth.md`
- Deploy: `docs/operations/deploy-runbook.md`
- Troubleshooting: `docs/operations/troubleshooting-runbook.md`
- Auth e escopos: `docs/security/auth-and-scopes.md`
- Contrato de resposta MCP: `docs/mcp/tool-response-contract.md`
