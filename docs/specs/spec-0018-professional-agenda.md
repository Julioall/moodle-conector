# SPEC-0018: Agenda profissional, recorrência, ICS e integração com Tasks

## Status

Implementing.

## Objetivo

Evoluir a agenda local para uma base profissional de **Event**s únicos e recorrentes: consultáveis por janela, compatíveis com ICS e relacionados a Tasks sem fundir seus domínios. A primeira versão atende à tutoria, escolas, turmas, cursos e unidades curriculares, mas permanece genérica e extensível.

`Event` é o nome canônico do domínio, DTOs e novos contratos. O nome físico legado `app_calendar_events` e identificadores de código já publicados podem permanecer temporariamente apenas por compatibilidade; eles não devem definir o vocabulário de novas APIs, migrations ou telas.

## Referências visuais

As imagens abaixo são referências internas de hierarquia, densidade e estados de interface; não constituem contrato de API nem instruções executáveis.

| Referência | Uso adaptado ao Moodle Connector |
|---|---|
| [Agenda: lista e detalhe](assets/agenda-professional/agenda-list-detail-reference.png) | Lista operacional principal, filtros, ações e drawer de Event. |
| [Agenda: criar/editar](assets/agenda-professional/event-edit-modal-reference.png) | Modal com título, tempo, tags, referências, disponibilidade, recorrência e origem ICS. |
| [Tasks: lista e detalhe](assets/agenda-professional/task-list-detail-reference.png) | Padrão compartilhado de painel lateral; a Task exibe **Eventos relacionados**. |
| [Tasks: criar/editar](assets/agenda-professional/task-modal-reference.png) | Padrão de chips e seleção estruturada de contexto reutilizável no formulário de Event. |

Preservar shell, permissões e tokens já existentes no Claris; usar referências acadêmicas estruturadas em vez de campos rígidos; não assumir participantes, links de reunião ou ações de cancelamento que não existam no domínio atual.

## Contexto e evidência atual

- A tabela legada `app_calendar_events` guarda evento privado por `OwnerId`, com `Title`, `Description`, `StartAt`, `EndAt`, `Type`, UID/origem e auditoria.
- `planner_links` armazena referências de Task **ou** evento por uma restrição de um único pai. Não representa relação Task ↔ Event.
- `GET /api/agenda` retorna registros brutos e filtra apenas por início; não expande recorrência, exceções ou eventos que atravessam a janela.
- O frontend usa lista/calendário, drawer e modal simples; o contrato web ainda expõe o modelo legado e `type` fechado.
- A importação ICS é idempotente por UID/origem, mas não persiste timezone, localização, recorrência, datas de exceção, tags ou overrides.

Antes da implementação, a fase de diagnóstico deve registrar entidades e tabelas atuais, endpoints, services, DTOs, tools MCP, componentes frontend, campos preserváveis, migrations e riscos de breaking change da Agenda e de Tasks. Esse diagnóstico verificado acompanha o primeiro PR da spec.

## Decisão e arquitetura-alvo

### Limites de domínio

| Conceito | Responsabilidade | Não é |
|---|---|---|
| `Event` | Algo que ocorre em um período: horário, timezone, recorrência, disponibilidade, local e ICS. | Uma Task ou status de execução. |
| `Task` | Trabalho a executar: status, prioridade, responsáveis, subtarefas, comentários e progresso. | Um bloqueio automático de horário. |
| `TaskEventLink` | Relação opcional, explícita e muitos-para-muitos. | Referência acadêmica ou sincronização de ciclos de vida. |

Concluir uma Task não cancela Event; cancelar ou reagendar Event não conclui nem altera automaticamente o prazo da Task. Automações futuras devem ser explícitas.

### Modelo de dados

| Entidade | Campos/garantias principais |
|---|---|
| `Event` | `Id`, `OwnerId`, `Title`, `Description`, `StartAt`, `EndAt`, `TimeZoneId`, `Location`, `AvailabilityStatus`, `IsAllDay`, `Source`, `ExternalUid`, auditoria e versão otimista. |
| `EventRecurrence` | `EventId`, `RRule`, `UntilAt`, `Count`, auditoria; nenhuma ocorrência futura é materializada. |
| `EventRecurrenceDate` | `EventId`, `OccurrenceStartAt`, `Kind` (`include`/`exclude`) para RDATE/EXDATE. |
| `EventOccurrenceOverride` | `EventId`, `OriginalStartAt`, `IsCancelled` e substituições opcionais. |
| `EventReference` | `EventId`, `ReferenceType`, `ReferenceId`, `ReferenceName`, `ConnectionRef`, `Relation`. |
| `EventTag` | `EventId`, `Value`, `NormalizedValue`. |
| `TaskEventLink` | `TaskId`, `EventId`, `Relation` (`scheduled_for`, `related`, `generated_from`), `OccurrenceStartAt` opcional, `CreatedBy`, auditoria. |

`ReferenceType` inicia com `school`, `class`, `course`, `curricular_unit`, `tutor`, `monitor`, `student`, `category` e `custom`; não é campo rígido do formulário. Tags são classificação livre e nunca substituem uma entidade Moodle que possa ser representada como referência.

`Relation` pode futuramente expressar vínculo hierárquico, como `applies_to_descendants`, para consultar cursos/UCs sob uma turma ou categoria sem duplicar fisicamente o Event. A propagação acadêmica completa não pertence à v1.

Uma relação de ocorrência usa `EventId + OccurrenceStartAt` (ou chave determinística equivalente), sem gravar uma linha física para cada ocorrência da série. `TaskEventLink` deve aceitar Tasks-filhas como aceita Tasks-raiz.

### Recorrência, ICS e reuso

- Persistir `RRULE`, `EXDATE`, `RDATE` e UID sem expansão global; expandir somente na janela solicitada, no timezone IANA da série, com limite configurável, paginação e ordenação determinística.
- Edição/cancelamento pontual cria override por `OriginalStartAt`; série e ocorrência exigem escopo explícito no cliente.
- Importar VEVENT idempotentemente por `(OwnerId, Source, ExternalUid)`, atualizando o mesmo Event na reimportação.
- `TaskReference` e `EventReference` são tabelas/contratos independentes, mas compartilham `AcademicReferenceResolver`, validação e componentes como `ReferenceChip`. A integração fica em `TaskEventIntegrationService` (ou equivalente), evitando dependência circular entre serviços de Task e Event.

### Consultas e experiência de uso

A API deve consultar Events de hoje, semana ou intervalo, por tutor, escola, turma, curso, UC, tag, recorrência e Task relacionada. A consulta retorna ocorrências sobrepostas à janela, inclusive quando o Event começa antes de `from` e termina depois, com ordenação e paginação estáveis.

Lista é a visão operacional principal, preservando também Calendário. O cabeçalho contém busca por Events e contextos, filtros, importar/exportar ICS, configurações, alternância Calendário/Lista e botão escuro “Novo Event”. A lista agrupa por data e abre o drawer ao selecionar uma linha.

O drawer exibe título, tags, horário/duração, recorrência, descrição, referências acadêmicas, localização/fonte ou link quando disponíveis, Tasks relacionadas, origem/auditoria e ações editar, duplicar e remover/cancelar conforme o escopo. Deve oferecer “Vincular Task” e “Criar Task a partir deste Event”.

O modal contém título, início/fim, dia inteiro, descrição, tags, referências, localização, disponibilidade (`free`, `busy`, `tentative`), recorrência e origem/UID ICS somente leitura. Na edição recorrente, o usuário escolhe ocorrência ou série. Participantes e motor de disponibilidade não são inferidos das imagens.

## Escopo

- CRUD de Event único e série recorrente, leitura por intervalo e filtros por tag, referência, tutor e Task relacionada.
- Importação/exportação ICS com UID, timezone, recorrência e exceções.
- Tags livres, referências acadêmicas estruturadas e vínculos bidirecionais Task ↔ Event, inclusive uma ocorrência recorrente.
- Criar Event a partir de Task e Task a partir de Event, com pré-preenchimento seletivo de título, descrição, tags/referências e criação do link.
- Lista/calendário, drawer e modal adaptados às referências visuais salvas.
- Endpoints, services, tools MCP, autorização, auditoria e testes.

## Fora de escopo

- Sincronização automática bidirecional com Google, Outlook ou Moodle.
- Motor de disponibilidade, participantes/convites, lembretes, notificações e métricas gerenciais.
- Propagação acadêmica física de um Event por turma/categoria.
- Criação automática de Task para todo Event, ou de Event para toda Task; também não há sincronização automática dos ciclos de vida.

## Contratos, compatibilidade e migração

### Migration aditiva

Uma migration nova deve estender `app_calendar_events` com os campos do Event e criar as tabelas de recorrência, datas, overrides, tags, referências e `task_event_links`. Ela deve:

1. Manter `Type` apenas como leitura/alias legado durante a transição.
2. Migrar `planner_links.CalendarEventId` para `event_references`, mantendo `planner_links` como leitor legado até remoção aprovada.
3. Preencher `TimeZoneId=America/Sao_Paulo` e `Source=manual` quando ausentes, preservando UID/origem e seu índice único parcial.
4. Criar unicidade para o vínculo Task/Event/ocorrência e índices de consulta.

### HTTP e MCP

Payloads novos usam `EventDto` e `EventOccurrenceDto`; campos atuais não devem ser renomeados destrutivamente na janela de compatibilidade.

| Operação | Contrato previsto |
|---|---|
| Listar ocorrências | `GET /api/agenda?from=&to=&tag=&referenceType=&referenceId=&tutorId=&taskId=` |
| Ler/criar/editar/remover Event | `GET`, `POST`, `PATCH`, `DELETE /api/agenda/{id}` |
| Alterar/cancelar ocorrência | `PATCH`, `DELETE`, `PUT /api/agenda/{id}/occurrences/{occurrenceKey}` |
| ICS | `POST /api/agenda/import`, `GET /api/agenda/export.ics` |
| Vínculos | `POST/DELETE/GET /api/tasks/{taskId}/events`; `GET /api/agenda/{eventId}/tasks` |
| Fluxos derivados | `POST /api/tasks/{taskId}/events`, `POST /api/agenda/{eventId}/tasks` com modo `create` ou `link` explícito |

Evoluir tools MCP para `tags`, `references`, `availabilityStatus`, recorrência e escopo (`single`, `series`, `occurrence`); `type` continua alias por uma versão. Além das tools de listar/criar/editar/remover Event, incluir operações equivalentes a `link_task_event`, `unlink_task_event`, `list_task_events`, `list_event_tasks`, `create_event_from_task` e `create_task_from_event`. Os nomes públicos finais seguem o catálogo canônico do projeto. Todas reutilizam application services e validam acesso aos dois recursos.

## Segurança, privacidade e observabilidade

- Exigir sessão, `agenda.manage`, CSRF e rate limit nas mutações; validar acesso à Task no vínculo.
- Validar título (1–240), descrição (até 4.000), local (até 500), até 20 tags de 1–64 caracteres, timezone IANA, `endAt > startAt`, RRULE e UID/origem.
- Limitar ICS a 5 MB, fazer unfold RFC 5545 e tratar descrição/links como texto não confiável.
- Impedir vínculo duplicado e registrar em `TaskActivity`: `event_linked`, `event_unlinked`, `event_created_from_task`, `event_rescheduled`, `event_cancelled`, sem espelhar todo o histórico do Event.
- Usar `updatedAt` ou versão de linha para concorrência otimista; medir expansão, importação, rejeição e conflitos.

## Plano de execução

O plano detalhado está em [`docs/plans/agenda-professional-v1-implementation.md`](../plans/agenda-professional-v1-implementation.md).

## Critérios de aceite

- [ ] Event único e série semanal retornam somente ocorrências da janela, preservando EXDATE, RDATE, override e timezone IANA.
- [ ] Reimportar o mesmo ICS atualiza o mesmo Event por UID/origem, sem duplicação observável.
- [ ] Tags e referências estruturadas são independentes, persistidas e filtráveis.
- [ ] Uma Task ou subtarefa pode ligar-se à série ou a uma ocorrência; Event pode ligar-se a mais de uma Task e vínculos duplicados são rejeitados.
- [ ] Cancelar/reagendar Event não muda o ciclo de vida da Task e concluir Task não cancela Event.
- [ ] Criar Event a partir de Task e Task a partir de Event cria o vínculo e a atividade, sem copiar status, prioridade ou prazo.
- [ ] Drawer, lista e modal seguem as referências salvas, preservando shell Claris e exibindo referências/tags estruturadas.
- [ ] Endpoints e tools MCP preservam permissão, CSRF, auditoria e campos legados no período de compatibilidade.
- [ ] Consultas de hoje, semana, intervalo, recorrentes e por tutor/escola/turma/curso/UC/tag/Task retornam resultados paginados e corretos.
- [ ] Task ligada à série e subtarefa ligada a uma ocorrência coexistem sem materializar ocorrências; um Event pode relacionar-se a várias Tasks.
- [ ] Lista/Calendário, drawer e modal apresentam todos os estados descritos, inclusive Tasks relacionadas e escolha de escopo recorrente.

## Validação e evidências

```powershell
dotnet test tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj
npm --prefix src/MoodleConnector.Web run typecheck
npm --prefix src/MoodleConnector.Web run lint
npm --prefix src/MoodleConnector.Web run test
npm --prefix src/MoodleConnector.Web run build
```

Além dos testes automatizados, importar duas vezes um ICS com RRULE, EXDATE, RDATE e timezone; validar uma ocorrência ligada a subtarefa e comparar visualmente lista, calendário, drawer e modal em viewport desktop.

## Rollout e rollback

Publicar primeiro migration aditiva e leitores compatíveis. Habilitar rotas, tools e frontend sob `ProfessionalAgendaEnabled`; testar, observar auditoria e habilitar progressivamente. Rollback desativa a flag sem destruir dados novos. A remoção de `Type`, dos identificadores legados e de `planner_links` para Event depende de spec posterior e telemetria de ausência de clientes antigos.
