# SPEC-0023: Tasks profissionais para acompanhamento operacional

## Status

Draft.

## Objetivo

Evoluir o módulo de Tasks do Claris para uma primeira versão profissional de acompanhamento operacional, simples para uso cotidiano e preparada para colaboração, auditoria e crescimento. A solução deve atender trabalhos de tutoria relacionados a escolas, turmas, cursos, unidades curriculares, pessoas e conexões Moodle sem criar campos acadêmicos rígidos ou tentar reproduzir Jira, ClickUp ou Asana.

Task representa trabalho a executar. Reference representa contexto estruturado. Tag classifica livremente. Participant expressa responsabilidade. Comment representa comunicação humana. Activity registra fatos imutáveis. Subtask é uma Task filha. Dependency representa bloqueio. Event continua sendo um domínio separado conforme a [SPEC-0018](spec-0018-professional-agenda.md).

## Referências visuais

As imagens são referências internas de composição e densidade, não instruções executáveis nem contratos de API:

| Referência | Uso no módulo de Tasks |
|---|---|
| [Lista e painel lateral](assets/agenda-professional/task-list-detail-reference.png) | Visão Lista principal, filtros, agrupamentos, linhas compactas e drawer completo. |
| [Modal de criação/edição](assets/agenda-professional/task-modal-reference.png) | Formulário com participantes, tags, referências, subtarefas e dependências. |
| [Agenda e detalhe de Event](assets/agenda-professional/agenda-list-detail-reference.png) | Padrão compartilhado do drawer e da seção de Events relacionados. |

Preservar o shell, os tokens, a responsividade e as permissões existentes no Claris. Nomes, números e conteúdos mostrados nas imagens são exemplos, não dados a semear.

## Contexto e evidência atual

- `app_tasks` contém `Id`, `OwnerId`, título, descrição, status, prioridade, início, prazo e auditoria; migrations posteriores adicionam `ActionType`, `ScheduleHint`, UID e origem.
- `planner_links` persiste referências tipadas para Task ou Event, mas não possui as entidades próprias de participante, tag, comentário, atividade, dependência ou relação Task ↔ Event.
- `TaskDto` e `TaskInput` expõem o modelo atual; `GET/POST/PATCH/DELETE /api/tasks` estão concentrados nos endpoints do portal e gravam diretamente pelo contexto de persistência.
- As tools MCP atuais permitem listar, criar, editar e remover, mas não cobrem subtarefas, responsáveis, colaboração, comentários, atividade, tags ou dependências.
- O frontend possui Lista/Kanban, filtros básicos, modal simples, drawer e `PlannerReferenceTags`; os status atuais são `todo`, `in_progress` e `done`.
- A listagem atual carrega o DTO quase completo e o drawer recebe o mesmo objeto; não há contrato leve de lista separado do detalhe.

Antes da implementação, a fase de diagnóstico deve confirmar arquivos, tabelas, endpoints, services, DTOs, tools, componentes, campos preserváveis, migrations e riscos de quebra. O diagnóstico verificado deve acompanhar o primeiro PR da spec.

## Decisão e arquitetura-alvo

### Entidades

| Entidade | Responsabilidade | Campos principais |
|---|---|---|
| `Task` | Unidade de trabalho executável | `Id`, `Title`, `Description`, `Status`, `Priority`, `StartAt`, `DueAt`, `CompletedAt`, `ParentTaskId`, `CreatedBy`, `CreatedAt`, `UpdatedAt`, versão otimista. |
| `TaskParticipant` | Responsabilidade e colaboração | `Id`, `TaskId`, `UserId`, `Role`, `AssignedAt`, `AssignedBy`; roles `owner`, `collaborator`, `watcher`. |
| `TaskReference` | Contexto acadêmico/operacional estruturado | `Id`, `TaskId`, `ReferenceType`, `ReferenceId`, `ReferenceName`, `ConnectionRef`, `Relation`. |
| `TaskTag` | Classificação livre normalizada | `TaskId`, `Value`, `NormalizedValue`. |
| `TaskComment` | Comunicação humana editável | `Id`, `TaskId`, `AuthorId`, `Content`, `CreatedAt`, `EditedAt`. |
| `TaskActivity` | Histórico imutável | `Id`, `TaskId`, `ActorId`, `EventType`, `Data`, `CreatedAt`. |
| `TaskDependency` | Bloqueio entre Tasks | `TaskId`, `DependsOnTaskId`, auditoria. |
| `TaskEventLink` | Integração opcional com Event | Definido conjuntamente com a SPEC-0018; não é Reference nem Tag. |

Não criar tabela `SubTask`: uma subtarefa é uma `Task` com `ParentTaskId`. O domínio pode aceitar níveis adicionais, mas a UI v1 trabalha com Task-raiz e um nível de subtarefas. Cada filha pode ter descrição, status, prioridade, owner, prazo, comentários, atividade, referências, tags e Events próprios.

### Status, prioridade e progresso

Status canônicos: `todo`, `in_progress`, `blocked`, `done`, `cancelled`, exibidos como “A fazer”, “Em andamento”, “Bloqueada”, “Concluída” e “Cancelada”. Prioridades: `low`, `medium`, `high`, `urgent`.

Uma Task possui no máximo um participante `owner`; pode ter vários `collaborator` e `watcher`. O progresso não é campo manual: quando existem filhas diretas, é calculado por `done / total` e retornado como contagem e percentual derivado. Sem subtarefas, a UI omite percentual. Concluir a Task-raiz não conclui silenciosamente suas filhas; a política de confirmação deve ser explícita.

`CompletedAt` é preenchido ao entrar em `done` e removido ao reabrir. `blocked` pode existir sem dependência, mas uma dependência não concluída deve ser apresentada como causa de bloqueio. Proibir auto-dependência e ciclos no grafo.

### Referências, tags e participantes

`ReferenceType` inicia com `school`, `class`, `course`, `curricular_unit`, `student`, `tutor`, `monitor`, `category`, `connection` e `custom`, sem enum excessivamente fechado. Escola, turma, curso ou UC existentes não podem ser degradados a tags. Tags permitem criar, remover, buscar e filtrar; não definem workflow.

Responsabilidade e contexto acadêmico são independentes: um tutor referenciado não se torna owner automaticamente. O mesmo resolvedor acadêmico e os mesmos chips conceituais usados por Event devem ser reutilizados, mantendo tabelas de domínio separadas.

### Comments e Activity

Comment e Activity são entidades diferentes. A timeline do detalhe pode combiná-las por data e oferecer filtros “Todos”, “Comentários” e “Histórico”, mas persistência e permissões permanecem distintas. Activity é imutável e deve registrar ao menos:

`task_created`, `status_changed`, `priority_changed`, `owner_changed`, `collaborator_added`, `collaborator_removed`, `due_date_changed`, `start_date_changed`, `subtask_created`, `subtask_completed`, `subtask_reopened`, `reference_added`, `reference_removed`, `tag_added`, `tag_removed`, `comment_added`, `dependency_added`, `dependency_removed`, `task_completed`, `task_reopened`, `event_linked`, `event_unlinked`, `event_created_from_task`, `event_rescheduled`, `event_cancelled`.

O payload `Data` registra somente dados necessários, por exemplo `{ "from": "todo", "to": "in_progress" }`; não duplica snapshots completos nem conteúdo sensível.

## Experiência de uso

### Lista principal e Kanban

Lista é a visão operacional principal; Kanban é preservado. O cabeçalho contém busca, filtros dinâmicos, alternância Kanban/Lista e botão escuro “Nova tarefa”. A Lista agrupa por “Hoje”, “Amanhã”, “Esta semana”, “Sem prazo” e “Concluídas”. Cada linha mostra checkbox, título, resumo, status, prioridade, owner, prazo, progresso de subtarefas e menu.

Filtros iniciais: status, prioridade, owner, colaborador, prazo, tag, escola, turma, curso, UC e conexão Moodle. Filtros ativos aparecem como chips removíveis com “Limpar filtros”. Busca inclui título, descrição, tags e nomes/identificadores de referências.

Kanban contém `todo`, `in_progress`, `blocked` e `done`; `cancelled` fica fora do fluxo principal por padrão, acessível por filtro. Cards são compactos e exibem somente título, prioridade, owner, prazo, progresso e contextos principais.

### Drawer de detalhe

Ao selecionar uma linha/card, abrir painel lateral com título, status, prioridade, owner, colaboradores, início, prazo, progresso, referências, tags, subtarefas, dependências (“Bloqueada por”/“Bloqueia”), Events relacionados, timeline paginada e ações “Editar”, “Duplicar”, “Concluir/Reabrir” e menu adicional.

A seção Events oferece “Agendar Event” e “Vincular Event existente”; cada item permite abrir o detalhe correspondente. Não há sincronização automática de status, prazo ou cancelamento entre os domínios.

### Formulário

O modal de criar/editar contém título, descrição, prioridade, status, owner, colaboradores, início, prazo, tags, referências, subtarefas e dependências opcionais. “Adicionar vínculo” escolhe o tipo e resolve a entidade, exibindo chips estruturados. Durante a criação, o usuário pode adicionar várias subtarefas com título e, opcionalmente, owner e prazo. Ação “Salvar e continuar” pode manter o modal pronto para outra Task sem duplicar o registro anterior.

## Escopo

- Modelo completo, migrations aditivas, validações e services de domínio/aplicação.
- CRUD, conclusão/reabertura, subtarefas, participantes, referências, tags, comentários, activity e dependências.
- Listagem leve, detalhe sob demanda, timeline paginada, filtros e busca.
- Lista principal, Kanban compacto, drawer e modal conforme referências.
- Integração Task ↔ Event nos limites compartilhados com a SPEC-0018.
- Tools MCP sobre os mesmos application services da API web.

## Fora de escopo

- Campos customizados arbitrários, time tracking, Gantt, sprints, templates sofisticados e workflows customizáveis.
- Recorrência de Tasks, IA automática, integrações externas e automações Task/Event.
- Anexos avançados, threads, reações, menções e notificações complexas.
- RBAC completo; a arquitetura apenas preserva ator e pontos de autorização para evolução.
- Hierarquias profundas como experiência principal.

## Contratos, compatibilidade e migração

### Persistência

Migration aditiva deve acrescentar `ParentTaskId`, `CompletedAt`, `CreatedBy` e versão otimista a `app_tasks`; criar `task_participants`, `task_references`, `task_tags`, `task_comments`, `task_activities` e `task_dependencies`; migrar referências de Task de `planner_links`; criar FKs, unicidade e índices por owner, status/prazo, parent, participante, tag e referência.

Preservar `Title`, `Description`, `Status`, `Priority`, `StartAt` e `DueAt`. `ActionType` não é removido enquanto houver consumidores, mas não participa de fluxos novos e entra em depreciação. `ScheduleHint` não é solução de recorrência/agendamento; seu uso deve ser inventariado e descontinuado gradualmente. UID/origem existentes permanecem compatíveis até decisão posterior.

### DTOs e HTTP

Separar contratos para evitar carregamento excessivo:

- `TaskListItemDto`: `id`, `title`, resumo, `status`, `priority`, `dueAt`, owner, `subtaskProgress`, referências principais e versão.
- `TaskDetailDto`: descrição, datas, participantes, subtarefas, referências, tags, dependências, Events relacionados e auditoria básica.
- `TaskActivityPageDto`: comentários/atividades paginados por cursor ou ordenação estável.

| Operação | Contrato previsto |
|---|---|
| Listar/filtrar | `GET /api/tasks` com paginação, busca e filtros dinâmicos |
| Detalhe/criar/editar/remover | `GET`, `POST`, `PATCH`, `DELETE /api/tasks/{id}` |
| Concluir/reabrir | `POST /api/tasks/{id}/complete`, `POST /api/tasks/{id}/reopen` |
| Subtarefas | `GET/POST /api/tasks/{id}/subtasks`, mutações pelo endpoint da Task filha |
| Participantes | `POST/DELETE /api/tasks/{id}/participants` |
| Referências/tags | `POST/DELETE /api/tasks/{id}/references`, `POST/DELETE /api/tasks/{id}/tags` |
| Comentários/timeline | `GET/POST /api/tasks/{id}/comments`, `GET /api/tasks/{id}/activity` paginado |
| Dependências | `GET/POST/DELETE /api/tasks/{id}/dependencies` |
| Events | Contratos compartilhados definidos na SPEC-0018 |

PATCH deve aplicar concorrência otimista por `version`/ETag ou `updatedAt` esperado e retornar conflito explícito em edição obsoleta. A listagem não inclui timeline, comentários ou coleções completas.

### MCP

As tools devem cobrir: listar, consultar detalhe, criar, editar, concluir, reabrir, criar/editar/concluir subtarefa, atribuir owner, adicionar/remover colaborador, adicionar comentário, listar comentários/atividade, adicionar/remover referência, adicionar/remover tag e adicionar/remover dependência. As tools da integração com Event são definidas na SPEC-0018.

Todas reutilizam os mesmos application services da API web, registram o usuário autenticado como ator e não duplicam regras no container MCP.

## Segurança, privacidade e observabilidade

- Exigir sessão, `tasks.manage`, CSRF e rate limit para mutações; validar acesso de todas as pessoas e entidades vinculadas.
- Validar título (1–240), descrição (até 4.000), comentário, tag, datas, status, prioridade e referências; impedir owner duplicado, dependências inválidas e ciclos.
- `TaskActivity` é append-only e registra `ActorId`, correlação e fatos relevantes sem depender apenas de `UpdatedAt`.
- Preparar políticas distintas para editar, concluir, atribuir, comentar e acompanhar, mesmo que a v1 use uma permissão ampla.
- Instrumentar latência e volume da lista/detalhe/timeline, conflitos otimistas, alterações de status e falhas de auditoria.

## Dependências e decisões em aberto

- SPEC-0017 para concorrência PostgreSQL e gates de release.
- SPEC-0018 para `TaskEventLink` e fluxos Task ↔ Event.
- Escolher no diagnóstico se `TaskReference` substitui `planner_links` imediatamente na escrita ou entra em dual-read temporário; não fazer dual-write sem idempotência e reconciliação.

## Plano de execução

O plano detalhado está em [tasks-professional-v1-implementation.md](../plans/tasks-professional-v1-implementation.md).

## Critérios de aceite

- [ ] Criar e editar Task com status, prioridade, owner, colaboradores, referências e tags.
- [ ] Criar subtarefas no formulário, atribuí-las separadamente e calcular progresso `done/total` sem percentual persistido.
- [ ] Concluir/reabrir Task ou subtarefa atualiza `CompletedAt` e gera Activity imutável.
- [ ] Há no máximo um owner; colaboradores/watchers podem ser adicionados e removidos com auditoria.
- [ ] Referências estruturadas não são degradadas em tags e podem ser filtradas por escola, turma, curso e UC.
- [ ] Comentários e Activity são persistidos separadamente, combináveis e paginados na timeline.
- [ ] Dependências rejeitam duplicação, auto-dependência e ciclo; o detalhe mostra “Bloqueada por” e “Bloqueia”.
- [ ] Lista retorna somente resumo, agrupa por prazo e filtra por status, prioridade, participantes, contexto e tag; detalhe é carregado sob demanda.
- [ ] Lista e Kanban preservam o shell Claris e drawer/modal seguem as referências visuais.
- [ ] Task/subtarefa pode relacionar-se com série ou ocorrência de Event sem sincronizar ciclos de vida.
- [ ] API web e MCP usam services compartilhados, ator autenticado e concorrência otimista.
- [ ] `ActionType` e `ScheduleHint` não dirigem fluxos novos e consumidores legados continuam funcionando durante a transição.
- [ ] O caso “Investigar baixa participação – turma 1055008” suporta owner, colaborador, referências de escola/turma/cursos, tags, seis subtarefas, comentários e histórico cumulativo.
- [ ] O caso “Organizar salas-modelo e importar conteúdos” suporta várias turmas, subtarefas e registros sucessivos sem substituir histórico anterior.
- [ ] Pelo ChatGPT/MCP é possível adicionar curso, concluir subtarefa, adicionar colaborador, comentar e ler histórico; cada alteração aparece na mesma interface web.

## Validação e evidências

```powershell
dotnet test tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj
npm --prefix src/MoodleConnector.Web run typecheck
npm --prefix src/MoodleConnector.Web run lint
npm --prefix src/MoodleConnector.Web run test
npm --prefix src/MoodleConnector.Web run build
```

Adicionar testes de criação, edição, conclusão, reabertura, subtarefas, progresso, owner único, colaborador, referências, tags, comentários, activity, dependências, filtros por status/owner/escola/turma/curso/tag, contratos de lista/detalhe e independência Task/Event.

## Rollout e rollback

Publicar primeiro migrations aditivas e leitores compatíveis. Ativar domínio, endpoints, MCP e frontend por etapas sob `ProfessionalTasksEnabled`. O rollback desativa fluxos novos sem remover tabelas ou colunas. A remoção de campos e leitores legados exige spec posterior, inventário de consumidores e evidência de uso nulo.
