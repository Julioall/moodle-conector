# Plano de implementação — caminho rápido da correção assistida

Referência: [SPEC-0024](../specs/spec-0024-assisted-grading-fast-path.md).

> Decisão vigente: o caminho operacional de correção assistida termina após gerar e salvar
> os rascunhos e exportar `export_grading_corrections_csv`. A UI, a prévia, a confirmação e o
> envio do lote permanecem apenas como código legado não registrado no MCP; as etapas abaixo
> que tratam de publicação não fazem parte deste caminho.

## Resultado esperado

Entregar uma correção assistida que consulte o Moodle apenas nas fronteiras necessárias e
use o PostgreSQL como fonte autoritativa da revisão. O primeiro marco deve eliminar chamadas
Moodle ao abrir ou atualizar a interface e reduzir a etapa de 20–36 segundos para próximo do
custo-base atual.

## Princípios de implementação

- Otimizar o fluxo específico; o executor universal é diagnóstico/capacidade complementar.
- Reusar `GradingContextSnapshot` em vez de reconstruir contexto.
- Preferir queries set-based e DTOs de leitura; não carregar agregados para renderização.
- Persistir a seleção e o job antes de baixar ou extrair arquivos.
- Paralelizar somente operações independentes e sempre com limite por conexão.
- Separar sucesso da gravação de sucesso do refresh visual.
- Não relaxar escala, confirmação, idempotência, reconciliação ou auditoria para ganhar tempo.

## Mapa de trabalho

| ID | Prioridade | Entrega | Dependência | Estado |
|---|---:|---|---|---|
| FAST-00 | P0 | Baseline e métricas internas por fase | — | Parcial — telemetria base e métricas da revisão implementadas; benchmark pendente |
| FAST-01 | P0 | `IGradingReviewReadStore` paginado | FAST-00 | Implementado |
| FAST-02 | P0 | UI de revisão sem Moodle | FAST-01 | Implementado |
| FAST-03 | P0 | Nomes e contexto vindos do banco/snapshot | FAST-01 | Implementado |
| FAST-04 | P0 | Resposta de save reconciliada localmente | FAST-02 | Implementado |
| FAST-05 | P1 | Pacote IA set-based | FAST-01 | Implementado |
| FAST-06 | P1 | Salvamento de revisões em lote | FAST-01 | Implementado |
| FAST-07 | P1 | Snapshot com coverage de avaliação | FAST-00 | Implementado |
| FAST-08 | P1 | Descoberta/listagem direta e fallback paralelo | FAST-07 | Implementado |
| FAST-09 | P1 | Downloads autenticados concorrentes no worker | SPEC-0022 | Implementado |
| FAST-10 | P2 | `moodle_download_file` controlada | FAST-09, SPEC-0014 | Implementado atrás de flag |
| FAST-11 | P2 | Certificação da escrita universal existente | SPEC-0011/0012 | Preservado; homologação manual pendente |
| FAST-12 | P0 | Carga, fault injection e rollout | FAST-01–11 aplicáveis | Pendente — requer homologação no ambiente Moodle |

## Fase 0 — baseline e instrumentação

### Mudanças

1. Criar um `GradingOperationTelemetry` com spans/fases para listagem, criação, ingestão,
   pacote IA, save, review, preview e confirmação.
2. Interceptar comandos EF Core para contar queries por operação/correlation ID.
3. Contabilizar função Moodle, duração, tentativas, retry delay e resultado.
4. Registrar snapshot/cache hit, idade, coverage, itens e bytes.
5. Criar fixture reproduzível de 1 e 50 itens, sem conteúdo acadêmico real.

### Gate

- Baseline automatizado reproduz o N+1 da revisão atual.
- Métricas não contêm nomes, texto, feedback, token ou URL autenticada.
- O custo da instrumentação fica abaixo de 5% no benchmark local.

## Fase 1 — read model local da revisão (P0)

### Contratos

Adicionar em Application:

```text
IGradingReviewReadStore.GetPageAsync(request, cancellationToken)
GradingReviewPageReadModel
GradingReviewItemReadModel
GetGradingReviewPageQuery
```

O request inclui `BatchJobId`, página, tamanho e identidade autorizada. O resultado contém
todos os dados já necessários por `GradingReviewAppData`, além de `ContextHash`,
`DraftVersionHash`, `GradingMode`, escala anulável, coverage e versão do read model.

### Implementação PostgreSQL

1. Query de lote/contadores/total.
2. Query paginada de itens com último `grading_context_snapshot` e última proposta.
3. Query set-based de evidências para os IDs retornados.
4. Usar `AsNoTracking`, projeção direta e índices por `BatchId`, `GradingItemId`, versão e
   ordem de página.
5. Adicionar colunas opcionais `CourseDisplayName`, `StudentDisplayName` e, se necessário,
   `ReadModelVersion` por migration aditiva.

Não criar um refresh síncrono do Moodle para preencher campos ausentes. IDs e avisos locais
são o fallback de lotes legados.

### Migração da interface

1. Fazer `review_batch_feedbacks` chamar somente `GetGradingReviewPageQuery`.
2. Fazer `get_batch_grading_ui_state` usar exatamente o mesmo query handler.
3. Remover `IMoodleCoursesGateway`, participantes e preparação de contexto do construtor de
   `MoodleGradingReviewAppTools`.
4. Após save/confirm, atualizar os itens com o DTO retornado pela própria gravação.
5. Executar refresh local em background; se falhar, mostrar “alterações salvas; não foi
   possível atualizar a tela”, mantendo o estado confirmado.

### Testes

- Teste de arquitetura falha se o handler referenciar gateway Moodle.
- PostgreSQL integration test prova no máximo cinco comandos em página de 50 itens.
- Teste de autorização cobre owner, admin e usuário não autorizado.
- Snapshot sem nome usa ID sem chamada externa.
- Save bem-sucedido seguido de refresh falho preserva sucesso e edição local.

### Gate P0

- Zero chamadas Moodle na abertura e no refresh.
- p95 da abertura ≤10 s no ambiente de referência.
- DTO antigo e novo equivalentes nos campos compartilhados.

## Fase 2 — package e save em lote

### Pacote de IA

1. Criar `IGradingAiPackageReadStore` ou reutilizar uma projeção interna do read store.
2. Carregar itens, último snapshot e evidências em até cinco comandos SQL.
3. Não consultar `IMoodleAssignmentSettingsGateway`; escala e enunciado vêm do snapshot.
4. Bloquear pacote sem extração legível/coverage suficiente e declarar o motivo por item.

### Salvamento

1. Validar todas as entradas, hashes e escalas antes da persistência.
2. Aplicar revisões válidas em uma unidade de trabalho.
3. Persistir auditoria em lote sem texto integral quando um resumo/hashes bastarem.
4. Atualizar contadores por agregação set-based.
5. Retornar itens atualizados e falhas por item; repetição do mesmo hash é idempotente.

### Gate

- Package de 50 itens e save de 50 itens sem N+1.
- Nota zero aceita em escala numérica; `feedback_only` exige `finalGrade=null`.
- Conflito de `DraftVersionHash` não sobrescreve revisão concorrente.

## Fase 3 — snapshot de avaliação e caminho direto

### Snapshot

1. Evoluir `AssignmentSubmissionsSnapshotItem` com `GradingMode`, `MaxGrade?` e coverage
   separado de submissões/configuração/notas.
2. Persistir `gradingStatus`, nota existente observável e timestamps por submissão.
3. Calcular `NeedsGrading` somente quando a cobertura requerida estiver completa.
4. Atualizar sincronização em chunks e registrar falha por atividade.

### Caminho ao vivo

1. Resolver curso e atividade uma única vez, aceitando `cmid` e instance ID.
2. Consultar `mod_assign_get_submissions` diretamente.
3. Depois da resolução, executar participantes, configurações e notas em paralelo somente
   quando o filtro exigir e o snapshot não puder responder.
4. Usar um deadline compartilhado, orçamento único de retry e cancelamento encadeado.
5. Retornar `partial_failure` com função/duração/audit ID quando qualquer cobertura faltar.

### Gate

- Listagem direta p95 ≤6 s sem retry.
- Caminho de snapshot não chama Moodle.
- Falha de configuração/notas não produz `NoPendingSubmissions`.
- CMID e instance ID retornam a mesma atividade canônica.

## Fase 4 — download e extração concorrentes

### Worker específico

1. Reusar `IMoodleSubmissionFileGateway`; não criar segundo cliente HTTP.
2. Introduzir `MaxConcurrentDownloadsPerConnection` e
   `MaxConcurrentDownloadsPerBatch`, default 4.
3. Processar artifacts com fila limitada e checkpoint por item.
4. Aplicar timeout total por arquivo, limite de bytes, MIME allowlist e cancelamento.
5. Persistir transição por artifact e continuar itens independentes após falha isolada.

### Primitiva genérica opcional

Adicionar `moodle_download_file` somente após os testes de SSRF e sanitização. A tool usa a
mesma conexão/gateway, devolve blob/resource MCP e metadados sanitizados. Ela fica atrás de
`UniversalMoodleFileDownloadEnabled` e não é necessária para o caminho específico.

### Gate

- DOCX/PDF reais são baixados e extraídos sem token em logs/DB.
- Redirect cross-host, arquivo acima do limite e esquema inválido são rejeitados.
- Concorrência reduz tempo de lote sem exceder limites por conexão.
- Falha de arquivo bloqueia avaliação baseada naquele arquivo.

## Fase 5 — preview e escrita enxutos

1. Preparar preview usando dados locais e último snapshot/version hash.
2. Fazer eventual preflight remoto em lote antes da pending action, com timestamp e validade.
3. Na confirmação, não consultar curso, participantes, enunciado ou escala.
4. Preservar write sem retry, idempotency key e `ExecutionUnknown`.
5. Certificar `moodle_prepare_write`/`moodle_confirm_write` já existentes com allowlist e
   schemas por função; manter `UniversalMoodleWriteEnabled=false` por padrão.
6. Avaliar `mod_assign_save_grades` em sandbox; habilitar chunks somente se erro parcial e
   reconciliação forem determinísticos.

### Gate

- Confirmação executa no máximo a escrita planejada e persistências locais.
- Timeout não cria segundo envio automático.
- Feedback-only nunca é auditado ou exibido como nota zero.
- Escrita universal indisponível informa flag/capability/scope faltante de forma explícita.

## Fase 6 — certificação e rollout

### Cenários obrigatórios

- 1, 16 e 50 itens;
- cache frio e quente;
- snapshot completo, incompleto e stale;
- atividade numérica, escala Moodle, feedback-only e modo desconhecido;
- DOCX, PDF, arquivo inválido, grande e redirect hostil;
- falha transitória em cada função Moodle;
- save concluído + refresh falho;
- timeout antes e depois do envio de escrita;
- duas réplicas e restart do worker.

### Sequência de flags

1. Instrumentação sem mudança de resposta.
2. Read model em shadow mode.
3. Read model para equipe interna.
4. Package/save em lote.
5. Snapshot e caminho direto.
6. Downloads concorrentes.
7. Primitives genéricas, separadamente e somente após certificação.

### Critério de encerramento

- Fluxo de referência entre 25–40 s, sem IA.
- Review p95 ≤10 s e zero chamadas Moodle.
- Listagem direta p95 ≤6 s sem retry.
- Nenhum gate acadêmico/de escrita regredido.
- Dashboard mostra query count, funções Moodle, retry, cache/snapshot hit e fases.

## Ordem sugerida dos PRs

| PR | Conteúdo | Risco |
|---|---|---:|
| 1 | Telemetria e testes de baseline | Baixo |
| 2 | Read store + migration/índices | Médio |
| 3 | Migração das tools/UI para leitura local | Médio |
| 4 | Package e save set-based | Médio |
| 5 | Snapshot de avaliação + caminho direto | Alto |
| 6 | Concorrência de download no worker | Médio |
| 7 | Preview/confirm sem reconstrução | Alto |
| 8 | `moodle_download_file` controlada | Alto |
| 9 | Certificação universal write e batch write experimental | Alto |

Cada PR deve ser reversível por flag, incluir os critérios da SPEC-0024 atendidos e anexar
evidência de query count/latência quando alterar um hot path.
