<p>
  <img src="public/logo.png" alt="Moodle Connector" width="720">
</p>

[![CI](https://github.com/Julioall/moodle-conector/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Julioall/moodle-conector/actions/workflows/ci.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)

Conector MCP não oficial para Moodle, criado para conectar o ChatGPT diretamente a vários ambientes Moodle. A ideia é permitir que professores, tutores, monitores e equipes acadêmicas consultem cursos, alunos, notas, pendências e informações operacionais sem precisar alternar entre várias telas e vários AVAs.

**Status: Release Candidate**

- Operações de leitura: estáveis e cobertas por testes.
- Escritas controladas: disponíveis atrás de feature flags, confirmação humana e auditoria.
- Arquitetura SKILL/Registry/Exposure: pronta para evolução funcional.
- Otimização cognitiva da superfície MCP: experimental e deferida enquanto não houver quota de certificação.

O projeto aceita contribuições, melhorias, correções, novas tools, testes e revisões de segurança.

O conector já possui escritas confirmadas e limitadas, como publicação em fórum, mensagens individuais e nota/feedback de tarefa. Esses fluxos usam `PendingAction`, confirmação literal, escopo de escrita, conexão `CanWrite` e auditoria; a permissão Moodle contextual continua obrigatória. As flags de escrita são configuráveis por ambiente e devem ser revisadas conforme a política de menor privilégio. Escrita geral de conteúdo, broadcast e agendamento não estão implementados.

> Este projeto não é oficial do Moodle HQ. Ele é uma integração independente baseada em MCP, ASP.NET Core, OAuth e APIs WebService do Moodle.

## Em 20 segundos

O Moodle Connector transforma o ChatGPT em uma interface operacional para Moodle:

**Professor:** “Quais atividades ainda preciso corrigir?”

**Moodle Connector:** identifica seus cursos, consulta assignments, encontra submissões pendentes e organiza o resultado por turma e prazo.

**Professor:** “Avise os alunos que ainda não entregaram.”

**Moodle Connector:** encontra a atividade, resolve os alunos pendentes, prepara as mensagens, solicita confirmação explícita e só então envia.

Fluxos de acompanhamento, risco, mensagens, correção e relatórios seguem o mesmo princípio: contexto Moodle primeiro, ação sensível somente após revisão humana.

## Objetivo

O Moodle Connector MCP permite que usuários autorizados consultem dados do Moodle e usem fluxos existentes de prévia/confirmação para mensagens individuais, publicação em fórum e nota/feedback individual. Outras alterações acadêmicas permanecem planejadas e não devem ser inferidas dessas capabilities.

Diretriz central:

- tools somente leitura executam imediatamente;
- tools de escrita não executam mudança direta na primeira chamada;
- escritas seguem o fluxo `prepare_*` e `confirm_*`;
- dados acadêmicos e pessoais devem ser tratados com mínimo necessário.

## Tutorial

### 1. Acesse o app

Acesse:

```text
https://novascript.com.br/
```

No app do Moodle Connector, crie sua conta ou entre com uma conta já cadastrada.

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

- um alias fácil de lembrar, como `meu-moodle`, `escola-abc` ou `faculdade`;
- a URL base do Moodle;
- o usuário do Moodle;
- a senha do Moodle;
- se esse Moodle deve ser o padrão;
- se a permissão de escrita deve ficar habilitada.

O alias ajuda o ChatGPT a escolher o Moodle correto durante a conversa. Por exemplo:

```text
Liste meus cursos no Moodle meu-moodle.
```

```text
Gere um relatório de notas no Moodle escola-abc.
```

Você pode cadastrar vários Moodles e alternar entre eles pelo prompt.

<p>
  <img src="public/Captura%202.png" alt="Tela para adicionar um Moodle no Moodle Connector" width="720">
</p>

Ao finalizar, o app mostra os Moodles cadastrados e a chave do conector quando ela estiver habilitada para o seu modo de uso.

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
Consulte o curso 29972 no Moodle meu-moodle.
```

```text
Mostre as atividades pendentes de correção dessa turma.
```

```text
Gere um relatório de notas para este curso.
```

```text
Busque os cursos do Moodle escola-abc com o termo desenvolvimento de sistemas.
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

As escritas controladas já estão disponíveis para fluxos específicos e devem seguir um fluxo seguro:

1. a IA prepara uma prévia da ação;
2. o professor ou usuário responsável revisa;
3. o sistema pede uma confirmação explícita;
4. somente depois disso a ação pode ser executada.

Hoje existem fluxos reais de prévia/confirmação para publicação em fórum, mensagens individuais e nota/feedback de tarefa. Mensagens exigem `PendingAction`, confirmação, escopo `moodle.write`, conexão `CanWrite` e `MessagesWriteEnabled=true`. Nota individual exige também `moodle.write.assignments.grade` e `AssignmentGradeWriteEnabled=true`. No `appsettings.json` versionado atual essas flags estão habilitadas; operadores devem revisá-las e podem sobrescrevê-las por ambiente conforme sua política de menor privilégio.

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
- `Application`: concentra queries, commands, contratos de tools e regras de aplicação.
- `Domain`: contém entidades e classificações de domínio, como risco e pending actions.
- `Infrastructure`: implementa banco, gateway Moodle, cache, storage OAuth e repositorios.

## Stack

- Compatibilidade orientada pelas funções Web Service habilitadas em cada conexão Moodle
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

A suíte cobre contratos MCP, autenticação JWT/API key, OAuth local, cadastro/login do app, pending actions, resolução de usuário Moodle e tools implementadas.

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
OAuth__ChatGptClientId=moodle
OAuth__ChatGptRedirectUri=https://chatgpt.com/connector/oauth/<callback_id>
OAuth__ScopeName=moodle-mcp-audience
OAuth__RequireHttpsMetadata=true
OAuth__KeyStoragePath=/app/data/oauth
```

Quando `OAuth__Issuer` e `OAuth__Audience` ficam vazios, a aplicação deriva os valores de `APP_DOMAIN`.

O app local usa cookie HttpOnly/SameSite, senha mínima de 12 caracteres e rate limit nos endpoints de cadastro/login. O `/mcp` também aplica rate limit por usuário/conector. Em produção, `OAuth__RequireHttpsMetadata=true` força issuer, audience e callback HTTPS.

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

1. `prepare_*`: valida a intenção, monta uma prévia, grava uma ação pendente e retorna o texto exato de confirmação.
2. `confirm_*`: recebe o `pendingActionId`, valida usuário, escopo, expiração e texto de confirmação, e só então executa a ação.

Contrato de ação pendente:

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

O projeto mantém tools demo e também possui escritas reais confirmadas para fórum, mensagens individuais e nota/feedback. A conexão `CanWrite`, o escopo aplicável, a capability Moodle, a prévia, a confirmação e a auditoria continuam obrigatórios. As flags são verificadas pelos handlers antes de preparar ou confirmar uma escrita; o `appsettings.json` versionado as habilita, enquanto `.env.example` as desabilita para um rollout manual seguro. O workflow de deploy grava valores explícitos de variáveis `FEATURES_*` para que a política do ambiente não dependa desse padrão versionado.

## Tools Existentes

Tools universais de leitura:

| Tool | Descrição | Status |
| --- | --- | --- |
| `moodle_diagnose_connection` | Descobre o perfil técnico da conexão, sem expor segredos. | Implementada |
| `moodle_list_functions` | Lista as funções Web Service habilitadas para o token. | Implementada |
| `moodle_check_function` | Verifica disponibilidade e classificação de risco local. | Implementada |
| `moodle_describe_function` | Descreve disponibilidade e classificação de risco local. | Implementada |
| `moodle_list_available_flows` | Mostra estratégias selecionadas e funções ausentes por fluxo. | Implementada |
| `moodle_execute_read` | Executa somente funções explicitamente classificadas como leitura segura. | Implementada |
| `moodle_prepare_write` | Cria prévia de escrita controlada sem chamar o Moodle. | Implementada; depende de `UniversalMoodleWriteEnabled=true` |
| `moodle_confirm_write` | Executa uma prévia confirmada uma única vez. | Implementada; depende de `UniversalMoodleWriteEnabled=true` |

As chamadas universais usam `POST /webservice/rest/server.php`, serializam arrays e objetos no formato nativo do Moodle e nunca inserem o token na URL. Uma função descoberta, mas ainda não classificada no catálogo local, é tratada como `Unknown` e recusada pela tool de execução. `moodle_prepare_write` e `moodle_confirm_write` só permitem funções explicitamente classificadas como escrita controlada, com `UniversalMoodleWriteEnabled=true`, `CanWrite`, confirmação literal, auditoria e execução única; funções destrutivas continuam bloqueadas.

Leitura:

| Tool | Descrição | Status |
| --- | --- | --- |
| `search` | Busca cursos autorizados no formato padrão MCP connector/company knowledge. | Implementada |
| `fetch` | Retorna um curso autorizado no formato padrão MCP connector/company knowledge. | Implementada |
| `list_my_courses` | Lista cursos vinculados ao usuário autenticado com metadados básicos. | Implementada |
| `list_courses` | Alias em ingles de `list_my_courses`. | Implementada |
| `search_courses` | Busca cursos vinculados por termo, nome, categoria, `courseId`, `shortName` ou `idNumber`. | Implementada |
| `search_courses` | Alias em ingles de `search_courses`. | Implementada |
| `get_course` | Consulta metadados básicos de um curso vinculado. | Implementada |
| `get_course` | Alias em ingles de `get_course`. | Implementada |
| `get_pedagogical_guidelines` | Pesquisa os guias pedagógicos locais antes de tarefas educacionais. | Implementada |

Estado interno (não altera o Moodle):

| Tool | Descrição | Status |
| --- | --- | --- |
| `manage_user_memory` | Salva, lista ou remove memórias privadas e duráveis do usuário. A remoção é destrutiva para esse estado interno. | Implementada |

Demo de pending action:

| Tool | Descrição | Status |
| --- | --- | --- |
| `prepare_demo_action` | Cria ação pendente demonstrativa. Não executa escrita real no Moodle. | Implementada como demo |
| `confirm_demo_action` | Confirma ação pendente demonstrativa. Não executa escrita real no Moodle. | Implementada como demo |

## Segurança

- Nao registrar senhas, JWTs, API keys, tokens Moodle, refresh tokens ou links privados com token em logs.
- Nao expor dados de estudantes sem necessidade operacional clara.
- Preferir respostas agregadas quando possivel.
- Escritas exigem confirmacao humana quando aplicavel.
- Tools de escrita devem validar usuário, escopo, vínculo Moodle, expiração, idempotência e texto de confirmação.
- Payloads de auditoria devem ser sanitizados antes de persistir.

## Documentação Detalhada

- Índice da documentação: `docs/README.md`

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
- Contrato de resposta MCP: `docs/technical/tool-response-contract.md`
