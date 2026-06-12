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
| `MoodleFunction` | Função Moodle relacionada, quando aplicável. |
| `RequestSanitizedJson` | Dados de entrada sanitizados. |
| `ResponseSummaryJson` | Resumo da resposta, sem segredos. |
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

## Pending actions

Tabelas relacionadas:

- `moodle_pending_actions`
- `moodle_confirmed_actions`
- `moodle_audit_logs`
- `moodle_user_links`

`moodle_confirmed_actions` existe no schema, mas a execução real pós-confirmação ainda não está implementada.

## Inicialização de schema

O projeto aplica o script versionado `src/MoodleConnector.Infrastructure/Database/Scripts/001_initial_schema.sql` no startup fora do ambiente de teste.

Esse baseline cria as tabelas da aplicação, pending actions, auditoria, vínculos Moodle, tabelas OpenIddict e registra a versão em `moodle_connector_schema_versions`.

## Diretrizes

- Registrar payload sanitizado, não payload bruto com segredo.
- Usar `CorrelationId` em fluxos multi-etapa.
- Não persistir token Moodle, JWT, API key ou senha.
- Em falhas de autorização, registrar motivo e área, sem credencial recebida.

## Sanitização Centralizada

Payloads de pending action e auditoria passam por `AuditPayloadSanitizer` antes de serem persistidos em campos de preview, request ou response summary.

A sanitização:

- redige campos com nomes sensíveis, como `password`, `token`, `secret`, `apiKey`, `authorization`, `cookie`, `jwt` e `connectionString`;
- remove parâmetros sensíveis de URLs Moodle, como `token`, `wstoken`, `sesskey`, `privatekey`, `accesskey` e `secret`;
- redige valores que aparentam ser headers `Bearer`/`Basic` ou JWT completo.
