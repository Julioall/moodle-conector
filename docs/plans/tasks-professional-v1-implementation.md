# Plano de implementação — Tasks profissionais v1

## Referências e resultado

- Spec: [SPEC-0023](../specs/spec-0023-professional-tasks.md).
- Referências visuais: [lista/detalhe](../specs/assets/agenda-professional/task-list-detail-reference.png) e [modal](../specs/assets/agenda-professional/task-modal-reference.png).
- Resultado: Tasks colaborativas, auditáveis e relacionadas ao contexto Moodle, com lista operacional, Kanban compacto e integração explícita com Event.

## Fase 0 — diagnóstico e contratos

1. Inventariar `app_tasks`, `planner_links`, entidades, DTOs, endpoints, tools MCP, componentes, testes e consumidores de `ActionType`/`ScheduleHint`.
2. Registrar matriz “preservar/evoluir/deprecar” e riscos de breaking change.
3. Congelar contratos de lista leve, detalhe, timeline e erro de concorrência.
4. Criar testes de caracterização da API, MCP e UI atuais antes da migration.

## Fase 1 — persistência e núcleo de Task

1. Criar migration aditiva para `ParentTaskId`, `CompletedAt`, `CreatedBy` e versão otimista.
2. Criar `task_participants`, `task_references` e `task_tags`, com FKs, unicidade e índices.
3. Migrar referências de `planner_links` com estratégia de compatibilidade definida no diagnóstico.
4. Implementar services e validações para status, prioridade, owner único, referências e tags.
5. Preservar leitura legada sem usar `ActionType` ou `ScheduleHint` em fluxos novos.

## Fase 2 — subtarefas e progresso

1. Implementar Task filha via `ParentTaskId`, sem tabela SubTask.
2. Permitir criar/editar/concluir/reabrir filha com owner e prazo próprios.
3. Calcular progresso de filhas diretas por `done/total`, sem campo percentual.
4. Definir confirmação explícita ao concluir Task-raiz com filhas abertas.

## Fase 3 — colaboração, comentários e atividade

1. Implementar owner, collaborators e watchers com ator autenticado.
2. Criar `task_comments` e `task_activities` separados; Activity append-only.
3. Centralizar emissão dos eventos de auditoria em services, incluindo mudanças de estado, participantes, referências, tags e subtarefas.
4. Implementar timeline paginada, combinada por leitura e filtrável por comentários/histórico.

## Fase 4 — dependências

1. Criar `task_dependencies` com unicidade e índices nos dois sentidos.
2. Rejeitar auto-dependência, duplicação e ciclos.
3. Expor “Bloqueada por” e “Bloqueia”, preservando a possibilidade de status `blocked` manual.

## Fase 5 — HTTP e consultas

1. Implementar `TaskListItemDto`, `TaskDetailDto` e timeline paginada.
2. Adicionar detalhe, complete/reopen, subtarefas, participantes, referências, tags, comentários e dependências.
3. Implementar busca e filtros por status, prioridade, owner, colaborador, prazo, tag, escola, turma, curso, UC e conexão.
4. Aplicar concorrência otimista e retornar conflito claro em atualização obsoleta.
5. Verificar planos e índices com volume representativo; não carregar coleções completas na lista.

## Fase 6 — integração Task ↔ Event

1. Reutilizar `TaskEventIntegrationService` da SPEC-0018.
2. Permitir vínculo com série ou ocorrência, inclusive para subtarefa.
3. Criar Event a partir de Task e Task a partir de Event com cópia seletiva.
4. Registrar Activity relevante sem sincronizar status, prazo ou cancelamento.

## Fase 7 — frontend

1. Tornar Lista a visão padrão, com agrupamentos Hoje, Amanhã, Esta semana, Sem prazo e Concluídas.
2. Implementar filtros dinâmicos e chips ativos; separar query de lista e detalhe.
3. Evoluir drawer com participantes, progresso, contexto, tags, subtarefas, dependências, Events e timeline.
4. Evoluir modal com participantes, vínculos, tags, subtarefas inline e dependências.
5. Compactar Kanban, adicionar `blocked` e manter `cancelled` disponível por filtro.
6. Comparar visualmente desktop e responsivo com os arquivos de referência.

## Fase 8 — MCP

1. Evoluir list/create/update para os novos contratos e adicionar detail, complete/reopen e subtarefas.
2. Adicionar tools de owner/colaborador, comentários/activity, referências, tags e dependências.
3. Integrar as tools Task ↔ Event previstas na SPEC-0018.
4. Testar autorização, ator, idempotência, auditoria, metadados e exposição.

## Fase 9 — certificação e rollout

1. Executar testes .NET, web, contratos, schema PostgreSQL, MCP e smoke autenticado.
2. Validar os casos de aceite da spec com Tasks-raiz, subtarefas, múltiplos usuários e contextos Moodle.
3. Aplicar migration com `ProfessionalTasksEnabled=false`, habilitar por ambiente e observar conflitos, erros e latência.
4. Preservar rollback por flag e dados novos até decisão explícita de remoção dos contratos legados.

## Sequência de PRs sugerida

1. `tasks: characterize current contracts and add persistence`
2. `tasks: add participants references tags and subtasks`
3. `tasks: add comments activity and dependencies`
4. `tasks: add list detail filters and optimistic concurrency`
5. `tasks: integrate events and evolve mcp tools`
6. `tasks: implement professional list drawer and form`
7. `tasks: certify rollout and legacy compatibility`

## Riscos e controles

| Risco | Controle |
|---|---|
| Perda de referências existentes | Migration aditiva, contagem/reconciliação e leitor compatível. |
| Dois owners | Índice/constraint e transação no service. |
| Ciclo de dependências | Detecção antes da escrita e testes de grafo. |
| Lista lenta | DTO leve, paginação e índices por filtros. |
| Sobrescrita concorrente | Versão otimista e erro de conflito. |
| Activity incompleta | Emissão central nos services e testes por operação. |
| Confusão Task/Event | Link explícito e ciclos de vida independentes. |
