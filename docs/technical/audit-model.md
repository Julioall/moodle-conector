# Modelo de Auditoria

## Objetivo

Registrar operações sensíveis e falhas de autorização sem expor segredos.

## Entidades

### `MoodleAuditLog`

Tabela: `moodle_audit_logs`

Campos principais:

| Campo | Descrição |
| --- | --- |
| `Id` | Identificador do log. |
| `CorrelationId` | Correlação entre prepare/confirm/execução. |
| `ToolName` | Tool ou área que gerou o evento. |
| `RiskLevel` | Nível de risco. |
| `ActorSubject` | Identificador do ator. |
| `ActorEmail` | Email do ator, quando disponível. |
| `ActorMoodleUserId` | ID Moodle, quando resolvido. |
| `CourseId` | Curso relacionado, quando aplicável. |
| `MoodleConnectionId` / `MoodleConnectionAlias` | Conexão Moodle associada às operações universais. |
| `MoodleFunction` | Função Moodle relacionada, quando aplicável. |
| `PendingActionId` | Ação pendente associada às escritas universais, quando aplicável. |
| `StartedAt` / `FinishedAt` / `DurationMs` | Marcos e duração da chamada universal. |
| `RequestSanitizedJson` | Dados de entrada sanitizados, incluindo alias da conexão e somente os nomes dos parâmetros quando a chamada é universal. |
| `ResponseSummaryJson` | Resumo da resposta, sem segredos, incluindo tamanho e duração da chamada quando aplicável. |
| `Status` | Status do evento. |
| `ErrorCode` | Código de erro, quando aplicável. |
| `ErrorMessage` | Mensagem de erro, quando aplicável. |
| `CreatedAt` | Data/hora do evento. |

## Eventos auditados hoje

| Evento | Status |
| --- | --- |
| Criação de pending action | Implementado |
| Confirmação de pending action | Implementado |
| Falha por usuário diferente do criador | Implementado |
| Falha por escopo ausente | Implementado |
| Falhas de autenticação/autorização no `/mcp` | Implementado |
| Leitura universal Moodle executada ou recusada | Implementado |
| Prévia universal Moodle criada ou bloqueada | Implementado |
| Escrita universal Moodle confirmada, executada ou falha | Implementado |

## Pending actions

Tabelas relacionadas:

- `moodle_pending_actions`
- `moodle_confirmed_actions`
- `moodle_audit_logs`
- `moodle_user_links`

`moodle_confirmed_actions` registra a confirmação válida. As ferramentas de escrita confirmada executam a operação Moodle correspondente uma única vez após essa confirmação, respeitando `CanWrite`, escopo, feature flag e a classificação local de risco.

## Inicialização de schema

O projeto aplica o script versionado `src/MoodleConnector.Infrastructure/Database/Scripts/001_initial_schema.sql` no startup fora do ambiente de teste.

Esse baseline cria as tabelas da aplicação, pending actions, auditoria, vínculos Moodle, tabelas OpenIddict e registra a versão em `moodle_connector_schema_versions`.

## Diretrizes

- Registrar payload sanitizado, não payload bruto com segredo.
- Usar `CorrelationId` em fluxos multi-etapa.
- Não persistir token Moodle, JWT, API key ou senha.
- Para chamadas universais, persistir somente o código normalizado de erro; não registrar a mensagem remota, que pode refletir parâmetros enviados.
- Em falhas de autorização, registrar motivo e área, sem credencial recebida.

## Sanitização Centralizada

Payloads de pending action e auditoria passam por `AuditPayloadSanitizer` antes de serem persistidos em campos de preview, request ou response summary.

A sanitização:

- redige campos com nomes sensíveis, como `password`, `token`, `secret`, `apiKey`, `authorization`, `cookie`, `jwt` e `connectionString`;
- remove parâmetros sensíveis de URLs Moodle, como `token`, `wstoken`, `sesskey`, `privatekey`, `accesskey` e `secret`;
- redige valores que aparentam ser headers `Bearer`/`Basic` ou JWT completo.
