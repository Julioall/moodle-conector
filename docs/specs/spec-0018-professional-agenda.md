# SPEC-0018: Agenda profissional, recorrência e ICS

## Status

Draft.

## Objetivo

Evoluir a agenda local do Moodle Connector para suportar compromissos únicos e
recorrentes, importação ICS idempotente, tags livres e referências acadêmicas
estruturadas, sem materializar antecipadamente as ocorrências de uma série.

As telas de referência são `ChatGPT Image 27 de ago. de 2026, 10_29_05.png`
(Agenda com lista e painel lateral) e `ChatGPT Image 27 de ago. de 2026,
10_29_17.png` (formulário modal). Elas definem a hierarquia visual da primeira
versão; não definem uma API nem substituem os contratos desta spec.

## Contexto e evidência atual

- `app_calendar_events` armazena eventos privados por `OwnerId`, com título,
  descrição, início/fim, `Type`, UID/origem externos e auditoria básica.
- A migration `033_planner_links_and_external_ids.sql` já garante unicidade
  parcial por `(OwnerId, ExternalSource, ExternalUid)` e mantém referências em
  `planner_links`.
- `GET /api/agenda` filtra somente `StartAt`; por isso não retorna ocorrências
  recorrentes, eventos que começaram antes do intervalo ou exceções.
- A importação atual de `.ics` é idempotente por UID/origem, mas não persiste
  timezone, localização, recorrência, EXDATE, RDATE, tags ou overrides.
- O contrato e a tool MCP atuais exigem `type` em um vocabulário fechado. Isso
  conflita com um evento de agenda genérico.

## Decisão e arquitetura-alvo

### Modelo de dados

`AgendaEvent` é o registro canônico de uma série ou de um evento único. Não
existirá uma linha persistida para cada ocorrência futura.

| Entidade | Responsabilidade | Campos principais |
|---|---|---|
| `AgendaEvent` | Evento ou série raiz | `Id`, `OwnerId`, `Title`, `Description`, `StartAt`, `EndAt`, `TimeZoneId`, `Location`, `AvailabilityStatus`, `IsAllDay`, `ExternalUid`, `Source`, auditoria |
| `AgendaEventRecurrence` | Regra da série | `EventId`, `RRule`, `UntilAt`, `Count`, auditoria |
| `AgendaEventRecurrenceDate` | Inclusões e exclusões explícitas | `EventId`, `OccurrenceStartAt`, `Kind` (`include`/`exclude`) |
| `AgendaEventOccurrenceOverride` | Alteração ou cancelamento de uma ocorrência | `EventId`, `OriginalStartAt`, `IsCancelled`, campos substitutos opcionais, auditoria |
| `AgendaEventReference` | Vínculo estruturado de um evento | `EventId`, `ReferenceType`, `ReferenceId`, `ReferenceName`, `ConnectionRef`, `Relation` |
| `AgendaEventTag` | Classificação livre normalizada | `EventId`, `Value`, `NormalizedValue` |

`ReferenceType` inicia com `school`, `class`, `course`, `curricular_unit`,
`tutor`, `category` e `custom`. `Relation` é opcional e permite explicar
futuros vínculos hierárquicos sem duplicar o evento (por exemplo,
`applies_to_descendants`). Tags não carregam identidade acadêmica.

O `TimeZoneId` usa identificador IANA. A expansão da regra acontece no timezone
da série e só então é convertida para `DateTimeOffset`; isto preserva horário
local em transições de horário de verão. O parser/expansor deve usar uma
biblioteca RFC 5545 e de timezone com comportamento determinístico, aprovada
na fase de implementação.

### Recorrência e exceções

- `RRULE`, `EXDATE` e `RDATE` são persistidos sem expansão global.
- `GET` expande apenas no intervalo solicitado, com limite configurável de
  ocorrências e paginação determinística.
- Uma alteração pontual cria ou atualiza um override endereçado por
  `OriginalStartAt`; um cancelamento pontual cria override com
  `IsCancelled=true`.
- Uma alteração da série atualiza o evento raiz e sua recorrência. Esta primeira
  versão não altera retrospectivamente overrides existentes sem escolha
  explícita do usuário.

### Migração

Uma migration PostgreSQL nova deve:

1. adicionar os campos novos a `app_calendar_events`, mantendo `Type` somente
   como legado de leitura durante a transição;
2. criar tabelas de recorrência, datas de recorrência, overrides, tags e
   referências de agenda, com FKs e índices;
3. copiar para `agenda_event_references` os vínculos existentes de
   `planner_links.CalendarEventId`;
4. preencher `TimeZoneId` dos eventos existentes com `America/Sao_Paulo`,
   marcar `Source=manual` quando inexistente e traduzir `Type` em tag opcional
   de legado, sem perder dados;
5. preservar `ExternalUid`/`ExternalSource` e o índice único parcial atual.

`planner_links` continua atendendo Tarefas. A Agenda passa a consultar sua
tabela própria para tornar o contrato independente e evolutivo.

## Escopo

- CRUD de evento único e série recorrente.
- Consulta por intervalo, tag, tutor e referência estruturada.
- Importação e reimportação ICS idempotente por `(OwnerId, Source, UID)`.
- Exportação ICS que preserva UID, timezone, recorrência, EXDATE, RDATE e
overrides suportados.
- Painel lateral para detalhe e modal para criar/editar, conforme referências
visuais fornecidas.
- Atualização das tools MCP e seus metadados/contratos.
- Migração segura dos eventos e vínculos existentes.

## Fora de escopo

- Sincronização automática bidirecional com Google, Outlook ou Moodle.
- Motor de disponibilidade entre várias pessoas, convite de participantes,
notificações, lembretes e métricas gerenciais.
- Propagação automática de um vínculo de turma/categoria para todos os cursos
descendentes.
- Materialização de ocorrências futuras ou integração com escrita no calendário
Moodle.

## Contratos, compatibilidade e migração

### HTTP

Os endpoints existentes permanecem durante a migração, aceitando `type` como
campo legado opcional. A resposta nova adiciona, sem renomear campos atuais:

```text
AgendaEventDto
  id, title, description, startAt, endAt, timeZoneId, location,
  availabilityStatus, isAllDay, recurrence, tags, references,
  source, externalUid, createdAt, updatedAt

AgendaOccurrenceDto
  occurrenceKey, eventId, originalStartAt, startAt, endAt,
  isCancelled, isOverride, event
```

| Operação | Contrato previsto |
|---|---|
| Listar ocorrências | `GET /api/agenda?from=&to=&tag=&referenceType=&referenceId=&tutorId=` |
| Ler evento/série | `GET /api/agenda/{id}` |
| Criar | `POST /api/agenda` |
| Editar série/evento único | `PATCH /api/agenda/{id}` |
| Editar ocorrência | `PATCH /api/agenda/{id}/occurrences/{occurrenceKey}` |
| Cancelar/restaurar ocorrência | `DELETE` / `PUT /api/agenda/{id}/occurrences/{occurrenceKey}` |
| Importar/exportar | `POST /api/agenda/import`, `GET /api/agenda/export.ics` |

As mutações continuam sob sessão, `agenda.manage`, CSRF e rate limit. Remover
uma série inteira continua sendo destrutivo; remover uma ocorrência só cria uma
exceção e deve deixar essa distinção explícita ao cliente.

### MCP

Substituir o parâmetro rígido `type` por `tags`, `availabilityStatus` e
`references`, mantendo `type` como alias de compatibilidade por uma versão.
Adicionar operações para criar/atualizar séries, consultar ocorrências e
alterar/cancelar uma ocorrência. Todas as escritas devem declarar claramente o
escopo `single`, `series` ou `occurrence` e preservar `agenda.manage`.

## Segurança, privacidade e observabilidade

- Validar título (1–240), descrição (até 4.000), localização (até 500), tags
  (1–64, máximo 20), timezone IANA e `endAt > startAt` quando houver fim.
- Rejeitar RRULE malformada, regras sem limite seguro quando ultrapassarem o
  orçamento de expansão, e overrides sem ocorrência correspondente.
- Sanitizar campos ICS, fazer unfold RFC 5545 com limite de arquivo de 5 MB e
  nunca executar links, anexos ou URLs presentes na descrição.
- Tratar UID como identificador externo não confiável, normalizá-lo e nunca
  expô-lo em logs de alto nível.
- Registrar auditoria de importação, atualização de série e override com
  `OwnerId`, origem, escopo e correlação; métricas incluem ocorrências
  expandidas, importados/atualizados/ignorados e rejeições por validação.

## Plano de execução

O plano detalhado está em
[`docs/plans/agenda-professional-v1-implementation.md`](../plans/agenda-professional-v1-implementation.md).

## Critérios de aceite

- [ ] Evento único e recorrente semanal são criados e retornam apenas suas
  ocorrências no intervalo requisitado.
- [ ] EXDATE, RDATE, override e cancelamento pontual têm comportamento estável
  em timezone IANA e não geram linhas futuras persistidas.
- [ ] Reimportar o mesmo ICS atualiza o mesmo evento por UID/origem, sem criar
  duplicatas.
- [ ] Tags livres e referências estruturadas são persistidas, retornadas e
  filtráveis de forma independente.
- [ ] Os eventos e vínculos atuais são migrados sem perda observável.
- [ ] O painel lateral e o modal seguem os estados e a hierarquia das imagens
  de referência, mantendo o shell Claris existente.
- [ ] As tools MCP e endpoints preservam permissão, CSRF, auditoria e contratos
  legados no período de compatibilidade.

## Validação e evidências

```powershell
dotnet test tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj
npm --prefix src/MoodleConnector.Web run typecheck
npm --prefix src/MoodleConnector.Web run lint
npm --prefix src/MoodleConnector.Web run test
npm --prefix src/MoodleConnector.Web run build
```

Além dos testes automatizados, executar importação/reimportação de um ICS com
RRULE, EXDATE e timezone, e validar visualmente lista, calendário, drawer e
modal no portal autenticado.

## Rollout e rollback

Publicar primeiro a migration aditiva e o leitor compatível. Ativar as rotas e
tools novas sob feature flag `ProfessionalAgendaEnabled`; migrar o frontend em
seguida. O rollback desativa a flag e mantém as colunas/tabelas novas sem
destruição de dados. A remoção de `Type` e do leitor legado só poderá ocorrer
em uma spec posterior, após telemetria de ausência de clientes antigos.
