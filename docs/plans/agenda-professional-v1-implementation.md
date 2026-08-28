# Plano de implementação — Agenda profissional v1

## Referências e resultado

- Spec: [SPEC-0018](../specs/spec-0018-professional-agenda.md).
- Referência visual: lista/calendário com painel lateral de evento e modal de
  criação/edição fornecidos em 27/08/2026.
- Resultado: eventos únicos e recorrentes consultáveis por janela, ICS
  idempotente, tags/referências e UI profissional sem quebrar a agenda atual.

## Fase 0 — congelar contratos e casos de teste

1. Inventariar entidades/tabelas, DTOs, endpoints, services, tools MCP,
   componentes de Agenda e Tasks, migrations e consumidores legados; registrar
   campos preserváveis, evolução necessária e riscos de breaking change.
2. Criar fixtures ICS: Event simples, RRULE semanal com `UNTIL`, `EXDATE`,
   `RDATE`, timezone IANA, UID repetido e descrição com URL.
3. Registrar contratos DTO v1/v2 e testes de compatibilidade para o campo
   legado `type`.
4. Escolher e aprovar a biblioteca de parsing/expansão RFC 5545 e timezone;
   documentar os limites de expansão e o tratamento de horários ambíguos.

**Saída:** contratos testáveis antes de qualquer migration.

## Fase 1 — persistência aditiva e migração

1. Criar migration `040_professional_agenda.sql` com campos novos da entidade
   canônica `Event` na tabela física legada `app_calendar_events`, e as tabelas
   de recorrência, datas, overrides, tags, referências e `task_event_links`.
2. Criar índices para `OwnerId + StartAt`, `OwnerId + TimeZoneId`, UID/origem,
   `EventId + OriginalStartAt`, tag normalizada e referência tipada.
3. Migrar referências de Event hoje em `planner_links` para
   `event_references`; manter `planner_links` apenas como leitor legado e criar
   `task_event_links` para a integração explícita com Tasks.
4. Preencher valores seguros para eventos existentes e manter `Type` como
   compatibilidade de leitura.
5. Adicionar testes de schema e de migração com banco PostgreSQL local.

**Saída:** dados existentes continuam legíveis e a nova estrutura está pronta.

## Fase 2 — domínio, validação e expansão sob demanda

1. Introduzir `Event`/`Recurrence`/`Occurrence` como modelos e serviços
   de aplicação, isolando ORM e RFC 5545 dos endpoints.
2. Implementar validação central de datas, timezone, RRULE, tamanho de campos,
   tags e referências.
3. Implementar expansor `ListOccurrences(from, to)` com ordenação estável,
   orçamento máximo, EXDATE, RDATE e merge de overrides.
4. Implementar alteração/cancelamento de uma ocorrência sem alterar a série.
5. Cobrir semanal, `UNTIL`, EXDATE, RDATE, override, cancelamento, intervalo
   vazio e mudança de horário em testes unitários.

**Saída:** recorrência correta sem gravação de ocorrências futuras.

## Fase 2.5 — integração Task ↔ Event

1. Implementar `TaskEventIntegrationService` sem dependência circular entre os
   serviços de Task e Event.
2. Criar, remover e listar `TaskEventLink`, com suporte a série inteira ou à
   ocorrência identificada por `OccurrenceStartAt`.
3. Implementar os fluxos derivados com cópia seletiva de título, descrição,
   tags e referências; nunca sincronizar status, prioridade ou prazo.
4. Registrar `TaskActivity` nos vínculos e testar independência dos ciclos de
   vida, inclusive em subtarefas.

**Saída:** os domínios continuam distintos e podem ser relacionados nos dois sentidos.

## Fase 3 — HTTP, ICS e compatibilidade

1. Evoluir `GET /api/agenda` para retornar ocorrências no intervalo e filtros
   por tag/referência/tutor, mantendo query antiga funcional.
2. Adicionar leitura de série, mutações de série e de ocorrência, com erro
   explícito para escopo inválido.
3. Reescrever importação/exportação ICS sobre o novo serviço: UID, DTSTART,
   DTEND, timezone, RRULE, EXDATE, RDATE, localização, descrição e tags.
4. Garantir reimportação segura por UID/origem, incluindo atualização de
   recorrência e referências.
5. Adicionar endpoints de listar/criar/remover vínculos e os fluxos de criar
   Event a partir de Task e Task a partir de Event, delegando ao serviço de
   integração da fase 2.5.
6. Aplicar sessão, `agenda.manage`, CSRF, rate limit, auditoria e contratos de
   erro em todos os endpoints novos.

**Saída:** API v2 segura e compatível com clientes v1.

## Fase 4 — tools MCP

1. Atualizar `list_agenda_events`, `create_agenda_event`,
   `update_agenda_event` e `remove_agenda_event` para o DTO novo.
2. Adicionar tools de criar/editar série, listar ocorrências e cancelar ou
   alterar ocorrência específica.
3. Adicionar operações de vincular/desvincular, listar nos dois sentidos,
   `create_event_from_task` e `create_task_from_event`, reutilizando o serviço
   de integração e aceitando série ou ocorrência.
4. Manter alias compatível para `type` na janela de migração e atualizar
   catálogo, metadados, exemplos e testes de exposição.
5. Testar autorização, escopo de escrita, idempotência e respostas sem dados
   sensíveis.

**Saída:** ChatGPT/MCP e portal usam a mesma capacidade de agenda.

## Fase 5 — frontend e fidelidade visual

1. Atualizar gateway e tipos TypeScript para `EventDto` e
   `EventOccurrenceDto`, mantendo aliases de transição onde necessário.
2. Implementar busca e filtros de hoje/semana/intervalo, tutor, escola, turma,
   curso, UC, tag, recorrência e Task; lista/calendário consomem ocorrências.
3. Manter painel lateral com horário/duração, recorrência, disponibilidade,
   localização/fonte, tags, referências, Tasks relacionadas, origem/auditoria
   e ações; incluir vincular/criar Task e usar modal para criar/editar.
4. No modal, organizar título, início/fim/dia inteiro, descrição, tags,
   referências, localização, disponibilidade, recorrência e origem/UID de
   importação somente leitura.
5. Para edição recorrente, pedir escopo explícito: esta ocorrência ou toda a
   série. Não oferecer exclusão destrutiva sem confirmação.
6. Executar comparação visual com as imagens de referência em viewport desktop
   equivalente, preservando shell/tokens Claris existentes.

**Saída:** experiência consistente, responsiva e operacional.

## Fase 6 — certificação e rollout

1. Executar testes .NET, web, contrato, PostgreSQL e smoke local.
2. Testar importação duas vezes do mesmo ICS e confirmar a mesma contagem de
   Events; testar uma série com EXDATE e override no calendário, consultas por
   todos os filtros e vínculos de Task com série e ocorrência.
3. Publicar com `ProfessionalAgendaEnabled=false`, aplicar migration, habilitar
   para ambiente de teste e observar auditoria/erros.
4. Habilitar progressivamente; manter leitor e payload legado até a próxima
   decisão de remoção.

## Sequência de PRs sugerida

1. `agenda: add additive persistence and migration tests`
2. `agenda: add recurrence domain and occurrence expansion`
3. `agenda: evolve APIs and idempotent ICS import`
4. `agenda: evolve MCP contracts and tools`
5. `agenda: implement professional calendar UI`
6. `agenda: certify rollout and retire legacy reads` (somente quando aprovado)

## Riscos e controles

| Risco | Controle |
|---|---|
| Expansão excessiva de RRULE | Janela obrigatória, limite de ocorrências e paginação. |
| Horário incorreto em DST | Timezone IANA e testes de transição. |
| Duplicação no ICS | Índice parcial UID/origem e teste de reimportação. |
| Quebra de clientes atuais | Campos e endpoints legados mantidos durante rollout. |
| Confusão entre evento e ocorrência | DTOs separados e confirmação de escopo em toda mutação. |
| Vínculo acadêmico duplicado | Referência estruturada normalizada; tags não substituem referência. |
