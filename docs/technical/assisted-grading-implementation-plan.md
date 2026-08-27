# Plano de implementação: correção assistida segura e escalável

## Propósito

Este plano transforma os achados da revisão ponta a ponta em entregas incrementais. Ele
preserva a arquitetura de revisão humana, confirmação, lançamento e reconciliação Moodle já
existente e concentra mudanças no trecho `artifacts → contexto → IA → revisão → job`.

As specs são a fonte dos contratos: [SPEC-0019](../specs/spec-0019-assisted-grading-batch-integrity.md),
[SPEC-0020](../specs/spec-0020-assisted-grading-academic-safety.md),
[SPEC-0021](../specs/spec-0021-canonical-grading-context.md) e
[SPEC-0022](../specs/spec-0022-durable-assisted-grading-jobs.md).

## Princípios de execução

- Corrigir P0 antes de ampliar autonomia ou capacidade de lote.
- Não lançar nota sem escala verificável, cobertura declarada e revisão humana.
- Fazer o contexto ser um artefato versionado; não reconstruí-lo em cada etapa.
- Persistir trabalho antes de enfileirar e usar PostgreSQL para recuperação multi-réplica.
- Manter `CallWriteAsync`, confirmação literal, pending actions, `ExecutionUnknown`,
  reconciliação e auditoria da SPEC-0011 intactos.
- Não criar histórico analítico de snapshots nem dashboard nesta iniciativa.

## Mapa de achados

| ID | Achado | Prioridade | Spec | Fase |
|---|---|---:|---|---:|
| AGR-01 | Lote limitado a 100 deixa itens pendentes | P0 | 0019 | 1 |
| AGR-02 | Cancelamento sem autorização do proprietário | P0 | 0019 | 1 |
| AGR-03 | `MaxGrade=100` quando escala é desconhecida | P0 | 0020 | 1 |
| AGR-04 | Nota sem validação consistente de faixa/escala | P0 | 0020 | 1 |
| AGR-05 | Três reconstruções divergentes de contexto | P1 | 0021 | 2 |
| AGR-06 | Critérios gerados desaparecem | P1 | 0021 | 2 |
| AGR-07 | Proposta IA sem evidência estruturada/confiança real | P1 | 0020 | 3 |
| AGR-08 | Rubrica formal não distinguida de materiais | P1 | 0020/0021 | 2 |
| AGR-09 | `teacherInstructions` não chega ao worker | P1 | 0021 | 2 |
| AGR-10 | Truncamento/chunks/coverage são perdidos | P1 | 0021 | 2/3 |
| AGR-11 | Estados de extração incompletos ou inconsistentes | P1 | 0020 | 1/3 |
| AGR-12 | Critérios heurísticos alteram pontuação | P1 | 0020 | 3 |
| AGR-13 | Prompt injection via submissão/material | P1 | 0020 | 3 |
| AGR-14 | Nome do aluno não está no pacote IA | P1 | 0020 | 3 |
| AGR-15 | Ingestão pesada no request | P1 | 0022 | 4 |
| AGR-16 | Fila em memória e `Pending` órfão | P1 | 0022 | 4 |
| AGR-17 | Ausência de lease multi-réplica | P1 | 0022 | 4 |
| AGR-18 | Retenção configurada sem cleanup verificável | P1 | 0022 | 4 |
| AGR-19 | Contadores `Pending` e `CanLaunch` inconsistentes | P2 | 0019 | 1 |
| AGR-20 | Lote não chega claramente a `Completed` | P2 | 0019 | 1 |
| AGR-21 | `Priority` sem efeito observável | P2 | 0019/0022 | 4 |
| AGR-22 | Edição usa status em vez de hash | P2 | 0019 | 1 |
| AGR-23 | `cmid` e `assignmentId` podem ser confundidos | P1 | 0021 | 2 |

## Fases e entregas

### Fase 0 — baseline e congelamento de contratos

**Dependências:** nenhuma.

**Entregas:** inventário de handlers/repositório/gateways; matriz de estados e extração;
contrato de compatibilidade; fixtures de lote 1, 100 e 400 itens; decisão de retenção e
rubrica; baseline de testes e métricas.

**Gate:** nenhum contrato atual de lançamento Moodle é alterado; todos os cenários P0 têm
teste reproduzível antes da implementação.

### Fase 1 — integridade imediata do lote e escala segura

**Specs:** 0019 + parte P0 da 0020.

**Entregas:** `LoadAllBatchItemsAsync`; autorização de cancelamento; contadores/lifecycle;
CAS por `ExpectedDraftVersionHash`; `GradingScale`; remoção do fallback 100; validação de
faixa e estados de extração canônicos.

**Gate de saída:** lote 400 completo, cross-user cancellation negado, escala desconhecida
sem nota numérica, notas fora da faixa rejeitadas, suíte existente preservada e novos testes
P0 aprovados.

### Fase 2 — contexto canônico e ingestão de evidência

**Spec:** 0021.

**Entregas:** `BatchConfiguration`; `GradingContextSnapshot` imutável; collectors
independentes; `teacherInstructions`; critérios persistidos; metadados de origem; rubrica
separada; chunks/truncamento/coverage; `ContextHash` em worker, UI, IA, preview e auditoria.

**Gate de saída:** os seis consumidores usam o mesmo hash; flags de rubrica/material têm
efeito independente; contexto legado exige nova preparação; nenhuma nota é gerada com
truncamento não declarado.

**Incremento entregue:** o contrato imutável `GradingContextSnapshot` foi criado com
identificadores Moodle tipados, proveniência, cobertura, estado de extração e hash
determinístico. A persistência e a migração dos consumidores continuam pendentes e serão
ativadas somente após os testes de equivalência da fase. O worker e o orquestrador local já
registram no item a identidade (`ContextVersion`, `ContextHash`, `ContextStatus`) do contexto
usado, sem duplicar o texto bruto da submissão.

### Fase 3 — proposta IA auditável e resistente a conteúdo hostil

**Spec:** 0020.

**Entregas:** `AiGradingProposal` versionado; resultados por critério; referências de
evidência/gaps; confiança recalculada; proveniência de critérios; proteção contra prompt
injection; nome autorizado ou remoção da saudação nominal; adaptador para respostas legadas.

**Gate de saída:** propostas sem escala/cobertura bloqueiam nota; critérios gerados não
redistribuem pontos; prompt injection não altera instruções; UI e auditoria exibem
incerteza/evidência; contrato legado vira revisão obrigatória.

### Fase 4 — job durável e processamento em escala

**Spec:** 0022.

**Entregas:** criação leve; fila PostgreSQL; claims/leases/checkpoints; retomada de
`Pending`/lease expirado; duas réplicas; prioridade/fairness; limites de concorrência;
cleanup de retenção; métricas/alertas.

**Gate de saída:** queda entre save e enqueue é recuperada; não há processamento duplicado
concorrente; 400 itens retomam por checkpoint; cleanup preserva auditoria; PostgreSQL CI
aprovado.

### Fase 5 — rollout e certificação

**Dependências:** Fases 1–4 e SPEC-0017.

**Entregas:** dual-read/dual-write encerrado; flags documentadas; migrações aplicadas;
MCP/portal/UI atualizados; runbook de incidentes; evidências de homologação e revisão
humana; monitoramento de backlog, bloqueios e propostas sem escala.

**Gate de release:** build/testes, PostgreSQL, validadores, portal, MCP Inspector,
fluxo de revisão e confirmação com conta de teste; nenhuma escrita real é feita durante
benchmark sem aprovação explícita.

## Estratégia de testes

| Camada | Cobertura mínima |
|---|---|
| Domínio | escala, faixa, estados, contadores, lifecycle e hash concorrente |
| Application | autorização, carregamento paginado, contexto único, proposta IA e flags |
| Infraestrutura | PostgreSQL JSONB/constraints, claim/lease, checkpoint e retenção |
| MCP | schemas versionados, estados `grade_unavailable`, cobertura, compatibilidade |
| Portal | review, `CanLaunch`, bloqueios, evidência, conflito de versão |
| Segurança | cross-user, prompt injection, logs sem PII/token, confirmação preservada |
| Operação | restart, duas réplicas, backlog, cleanup, alertas e rollback |

## Ordem de migração de dados

1. Adicionar colunas/tabelas novas sem remover as antigas.
2. Preencher `BatchConfiguration`, escala e snapshots para novos lotes.
3. Executar backfill somente de metadados verificáveis; marcar o restante como legado.
4. Ativar dual-read e comparar hashes/contadores.
5. Tornar novos contratos obrigatórios após os gates.
6. Remover campos/rotas legadas somente em release posterior com telemetria.

## Rollback global

Cada fase possui feature flag e migração aditiva. Em falha, pausar novos lotes/claims,
preservar dados e voltar à leitura compatível. Nunca fazer rollback que reenvie POST Moodle,
converta escala desconhecida em 100, descarte auditoria ou libere lote de outro usuário.

## Pendências para aprovação

- [ ] Aprovar fórmula de confiança e limiar de `ReviewRequired`.
- [ ] Aprovar fonte/capability de rubrica Moodle.
- [ ] Aprovar retenção legal de texto, anexos e evidência mínima.
- [ ] Decidir se `Priority` será implementada ou deprecada.
- [ ] Definir storage de arquivos grandes e limite de chunks por modelo.
