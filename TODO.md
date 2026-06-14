# TODO.md — Correção Assistida SENAI para AVA/Moodle via GPT Apps

**Status:** proposta técnica + plano de implementação ajustado ao repositório `moodle-conector`
**Data-base:** 2026-06-12
**Objetivo:** transformar o GPT APP/Conector do AVA em uma ferramenta de **consulta, análise e escrita assistida** para apoiar professores/tutores na correção de atividades, mantendo decisão humana, rastreabilidade e segurança institucional.

**Stack-alvo deste plano:** ASP.NET Core/C#, MCP server já exposto em `/mcp`, PostgreSQL, OpenIddict/OAuth, camadas `Domain`, `Application`, `Infrastructure` e `Presentation`, tools MCP implementadas em C# com atributos do `ModelContextProtocol.Server`.

---

## 0. Decisão de arquitetura

- [x] Adotar o modelo **Correção Assistida**, não “correção automática”.
- [x] Implementar dentro do projeto atual em C#/.NET, sem criar um segundo servidor TypeScript paralelo.
- [x] Separar o fluxo em:
  1. consulta e preparação do pacote de correção;
  2. extração e interpretação dos arquivos;
  3. sugestão de feedback e nota;
  4. revisão/conferência do professor;
  5. criação de prévia de lançamento;
  6. confirmação explícita;
  7. envio ao Moodle;
  8. auditoria.
- [x] Evitar uma única tool `corrigir_e_enviar`, pois ela mistura leitura, análise, decisão pedagógica e escrita no AVA.
- [x] Para pedidos com 300–400 atividades, usar **processamento assíncrono por lote**, fila e workers. Não tentar resolver 400 entregas como 400 chamadas diretas no chat.

---

## 1. Análise de viabilidade com base no sistema existente

### 1.1 Situação atual informada

O conector atual expõe tools MCP em C# com duplicidade parcial entre nomes em inglês e português. Elas cobrem principalmente:

- cursos;
- estrutura da sala;
- atividades e tarefas;
- entregas;
- estudantes, participantes e grupos;
- conteúdos e materiais;
- quizzes, SCORMs e URLs;
- ações demonstrativas.

Situação confirmada no repositório:

- [x] `/mcp` já está publicado como MCP streamable HTTP.
- [x] OAuth/OpenIddict já autentica o GPT App com `chatgpt-mcp`.
- [x] As consultas de cursos, atividades e entregas já funcionam.
- [x] As tools de entregas atuais são **somente leitura**.
- [x] Existe infraestrutura para ação pendente, confirmação literal e auditoria:
  - `PendingMoodleAction`;
  - `PendingActionService`;
  - `ActionConfirmationService`;
  - `moodle_pending_actions`;
  - `moodle_audit_logs`.
- [x] Existe tool real para lançar nota/feedback no Moodle após prévia e confirmação literal.
- [x] Criar gateway chamando `mod_assign_save_grade` para escrita individual controlada.

### 1.2 Pontos fortes já existentes

- [x] O conector já consulta cursos, atividades, estudantes, prazos e submissões.
- [x] Já existem funções próximas do fluxo necessário:
  - listar cursos;
  - consultar curso;
  - listar conteúdos;
  - listar tarefas;
  - consultar tarefa;
  - listar entregas;
  - consultar entrega de aluno;
  - listar entregas aguardando correção;
  - listar pendentes e atrasadas.
- [x] O modelo operacional já está próximo de um assistente de tutoria e monitoria.
- [x] Há separação clara entre consulta e demonstração, o que facilita acrescentar uma camada de escrita controlada.

### 1.3 Lacunas críticas

- [ ] Verificar se existe endpoint real para **baixar anexos da submissão**.
- [ ] Verificar se existe endpoint real para **ler arquivos vinculados a módulos e entregas**.
- [ ] Verificar em ambiente Moodle real se `mod_assign_get_submission_status`, `mod_assign_get_grades`, `mod_assign_save_grade` e `mod_assign_save_grades` estão habilitadas no serviço externo.
- [ ] Criar serviço de **extração de conteúdo** para PDF, DOCX, PPTX, XLSX, TXT, HTML, ODT e imagens.
- [ ] Criar estrutura de **pacote de correção** com enunciado, critérios, rubrica, anexos, materiais e submissão.
- [x] Criar motor de **análise por critério** com evidências, lacunas e nota sugerida.
- [x] Criar tool real de **lançamento de nota e feedback** no Moodle.
- [x] Reusar a confirmação humana obrigatória já existente antes de qualquer escrita no Moodle.
- [x] Expandir auditoria existente para registrar commit por item, nota final, feedback final e retorno/falha do Moodle.
- [x] Expandir auditoria existente para registrar criação de lote, versão do rascunho e revisão humana.

### 1.4 Viabilidade técnica

**Viável, com restrições.**

A proposta é viável se forem atendidas estas condições:

- [ ] O Moodle expõe funções de web service para consulta de submissões e notas.
- [ ] O serviço externo consegue baixar arquivos via endpoint seguro.
- [ ] A instância Moodle permite adicionar funções como `mod_assign_save_grade` ou `mod_assign_save_grades` ao serviço autorizado.
- [ ] Os tokens possuem escopos separados para leitura e escrita.
- [ ] Há fila de processamento para lotes grandes.
- [ ] Há armazenamento temporário para pacotes, rascunhos, logs e auditoria.
- [ ] Há política de retenção e descarte dos arquivos dos estudantes.

### 1.5 Viabilidade pedagógica

**Viável como correção assistida; não recomendável como correção autônoma.**

Regras pedagógicas obrigatórias:

- [ ] A IA deve sugerir, não decidir.
- [ ] Toda nota sugerida deve estar vinculada a critérios e evidências.
- [ ] Se não houver rubrica, gabarito, critérios ou tabela de conversão, a tool gera parecer preliminar, mas não nota final confiável.
- [ ] O feedback deve indicar:
  - pontos fortes;
  - lacunas;
  - orientação de melhoria;
  - próximos estudos ou materiais;
  - tom acolhedor e formativo.
- [ ] Toda publicação no AVA depende de revisão e confirmação do professor/tutor.

### 1.6 Viabilidade para 300–400 atividades por pedido

**Viável apenas com processamento assíncrono.**

Não recomendado:

- [ ] Não processar 400 entregas diretamente na janela do chat.
- [ ] Não retornar 400 feedbacks completos de uma só vez.
- [ ] Não fazer uma chamada de tool por estudante comandada pelo modelo.
- [x] Não lançar notas em lote sem prévia revisável e confirmação explícita.

Recomendado:

- [ ] Criar um `batchJobId`.
- [ ] Processar em fila.
- [ ] Consultar status por lote.
- [ ] Retornar sumários paginados.
- [ ] Abrir revisão individual sob demanda.
- [ ] Permitir confirmação por subconjuntos revisados.
- [x] Usar idempotência para evitar duplicidade de lançamento no fluxo MVP de confirmação.
- [x] Registrar auditoria por estudante, atividade e tentativa no commit Moodle.

---

## 2. Arquitetura-alvo

### 2.0 Classificação ChatGPT Apps SDK

- [x] Arquétipo principal do MVP: `tool-only`.
- [x] O app já tem MCP server remoto em `/mcp` e não precisa de UI obrigatória para entregar consulta, análise, rascunho e confirmação.
- [x] `search` e `fetch` já existem para cursos e seguem o padrão connector-like/company knowledge.
- [ ] Antes de evoluir company knowledge/deep research, decidir se `search`/`fetch` continuarão limitados a cursos ou se também devem cobrir materiais, atividades e documentos do AVA.
- [ ] Se a revisão por chat ficar ruim, evoluir para UI opcional no padrão desacoplado: tools de dados sem widget e uma tool de renderização que anexa o painel.

### 2.1 Componentes

```text
ChatGPT / GPT App
        |
        | MCP tools + UI opcional
        v
MCP Server SENAI / Middleware de Correção
        |
        | serviços internos
        v
Fila de processamento + Workers
        |
        | integração
        v
Moodle Web Services / pluginfile.php
        |
        v
AVA Moodle
```

### 2.2 Componentes internos do middleware

- [x] `MoodleConnector.Presentation`: expõe tools MCP ao ChatGPT em `/mcp`.
- [x] `MoodleConnector.Application`: concentra casos de uso, comandos, queries, orquestração de correção e regras de negócio.
- [x] `MoodleConnector.Infrastructure`: encapsula PostgreSQL, Moodle REST, download de arquivos, tokens e persistência.
- [x] `MoodleConnector.Domain`: entidades e value objects de lote, item, evidência, artefato, auditoria e status.
- [x] `GradingTools`: nova família de tools MCP em `src/MoodleConnector.Presentation/Tools/Grading`.
- [x] `IMoodleAssignmentGradingGateway`: gateway inicial para `mod_assign_save_grade`.
- [ ] Expandir `IMoodleAssignmentGradingGateway` para `mod_assign_save_grades` depois de testar falhas parciais e comportamento transacional.
- [x] `IMoodleSubmissionFileGateway`: gateway para anexos via Moodle/pluginfile.
- [x] `IDocumentExtractionService`: extração de texto e metadados.
- [x] `IGradingAnalysisService`: análise por critério e geração de rascunho.
- [x] `IGradingBatchOrchestrator`: criação e controle de lotes.
- [x] `IGradingReviewRepository`: persistência de rascunhos e decisões do professor.
- [x] Reusar `IPendingActionService` e `IActionConfirmationService` para prévia/confirmação.
- [x] Reusar `IMoodleAuditLogRepository` e criar eventos específicos de correção para commit Moodle.
- [ ] `app-ui`: painel opcional de revisão dentro do ChatGPT, depois do MVP.

### 2.3 Arquitetura conforme GPT Apps / OpenAI Apps SDK

- [ ] Implementar um **MCP server** público/privado para o GPT App.
- [ ] Expor o endpoint do servidor em `/mcp`.
- [x] Usar o MCP server ASP.NET Core já existente, com tools declaradas em C# via `[McpServerTool]`.
- [ ] Declarar/validar tools com:
  - `title`;
  - `description`;
  - `inputSchema`;
  - `outputSchema`;
  - `securitySchemes`;
  - `_meta.securitySchemes`;
  - `_meta.ui.resourceUri`, quando houver UI;
  - `annotations`.
- [ ] Usar `readOnlyHint: true` em tools de consulta.
- [ ] Usar `readOnlyHint: false` em tools que criam job, rascunho, ação pendente ou escrita no Moodle.
- [ ] Usar `openWorldHint: false` para escrita no Moodle institucional, pois a ação fica em sistema privado/fechado e não publica na internet aberta.
- [x] Usar `destructiveHint: true` em `confirmar_lancamento_lote_moodle`, salvo se o handler provar que nunca sobrescreve nota/feedback existente e que o efeito é reversível.
- [ ] Usar `idempotentHint: true` somente quando houver chave de idempotência, versão de rascunho, `pendingActionId` ou semântica comprovadamente retry-safe.
- [ ] No MVP, manter o OAuth atual com `moodle-mcp-audience` e autorização server-side por claims internas.
- [ ] Para escrita, exigir:
  - conexão Moodle com `CanWrite = true`;
  - claim `moodle.write` já usada pelo projeto;
  - feature flag de escrita habilitada;
  - confirmação literal.
- [ ] Evolução posterior: separar scopes OAuth por domínio, como `moodle.files.read`, `grading.draft.write`, `moodle.grade.write` e `grading.audit.read`, com cuidado para atualizar seed OpenIddict, metadata e testes.
- [ ] Implementar validação server-side, sem confiar apenas no modelo.
- [ ] Exigir confirmação humana para escrita no Moodle.
- [ ] Para UI, renderizar painel em iframe sandbox com CSP restritiva, usando `_meta.ui.resourceUri` na render tool e `_meta.ui.csp` no recurso.

---

## 3. Redesenho das tools

### 3.1 Estratégia

Adicionar um conjunto menor de tools canônicas para correção assistida, sem remover imediatamente as tools existentes. As tools atuais de cursos, atividades, participantes, conteúdos e entregas continuam como base de consulta e compatibilidade. Depois do MVP, reduzir exposição/duplicidade pode ser feito com segurança, mantendo aliases em português/inglês quando necessário.

Implementação no repo:

- [x] Criar `src/MoodleConnector.Presentation/Tools/Grading/MoodleGradingTools.cs`.
- [x] Registrar `MoodleGradingTools` em `src/MoodleConnector.Presentation/Program.cs` com `.WithTools<MoodleGradingTools>()`.
- [x] Criar comandos/queries em `src/MoodleConnector.Application/Grading`.
- [x] Criar entidades de domínio em `src/MoodleConnector.Domain/Grading`.
- [x] Criar gateways Moodle em `src/MoodleConnector.Infrastructure`.
- [x] Registrar novos serviços em `src/MoodleConnector.Infrastructure/DependencyInjection.cs` e/ou `src/MoodleConnector.Application/DependencyInjection.cs`.
- [x] Atualizar `tests/MoodleConnector.Application.Tests/Tools/McpToolMetadataTests.cs` para incluir `MoodleGradingTools` e diferenciar tools de leitura, preparo de escrita e confirmação.
- [ ] Manter `search` e `fetch` existentes; as tools de correção complementam o padrão, não substituem.

### 3.2 Tools canônicas propostas

#### Tool 1 — `listar_entregas_corrigiveis`

**Tipo:** leitura
**Objetivo:** listar entregas de uma ou mais atividades que podem entrar em lote de correção.

- [x] Entrada:
  - `courseId`
  - `assignmentIds`
  - `status`
  - `onlyAwaitingGrading`
  - `includeLate`
  - `page`
  - `perPage`
- [x] Saída:
  - lista paginada de entregas;
  - contadores;
  - alertas;
  - permissões disponíveis.

#### Tool 2 — `criar_lote_correcao_assistida`

**Tipo:** leitura + criação de job interno
**Objetivo:** criar lote assíncrono de análise para 1 a 400 entregas.

- [x] Entrada:
  - `courseId`
  - `assignmentIds`
  - `submissionIds` opcional
  - `maxItems`
  - `includeRubric`
  - `includeSubmissionFiles`
  - `includeCourseMaterials`
  - `teacherInstructions`
  - `priority`
- [x] Saída:
  - `batchJobId`
  - quantidade total;
  - quantidade aceita;
  - quantidade bloqueada;
  - previsão operacional sem prometer horário de entrega;
  - warnings.

#### Tool 3 — `consultar_status_lote_correcao`

**Tipo:** leitura
**Objetivo:** consultar andamento do lote.

- [x] Entrada:
  - `batchJobId`
- [x] Saída:
  - status geral;
  - contadores por estado;
  - erros por categoria;
  - próximos itens prontos para revisão;
  - métricas de processamento.

#### Tool 3.1 — `cancelar_lote_correcao_assistida`

**Tipo:** escrita interna, não Moodle
**Objetivo:** cancelar um lote interno ainda não finalizado.

- [x] Entrada:
  - `batchJobId`
- [x] Saída:
  - `batchJobId`;
  - status final;
  - mensagem operacional.

#### Tool 4 — `consultar_item_correcao_assistida`

**Tipo:** leitura
**Objetivo:** abrir análise de uma entrega específica.

- [x] Entrada:
  - `batchJobId`
  - `gradingItemId`
- [ ] Saída:
  - dados mínimos do estudante;
  - enunciado;
  - critérios;
  - evidências;
  - lacunas;
  - nota/conceito sugerido;
  - feedback sugerido;
  - observações internas;
  - nível de confiança;
  - bloqueios.

#### Tool 5 — `atualizar_rascunho_correcao`

**Tipo:** escrita interna, não Moodle
**Objetivo:** salvar ajustes do professor no rascunho.

- [x] Entrada:
  - `gradingItemId`
  - `finalGrade`
  - `finalFeedback`
  - `teacherDecision`
  - `reviewNotes`
- [x] Saída:
  - rascunho atualizado;
  - hash do rascunho;
  - pendências.

#### Tool 6 — `criar_previa_lancamento_lote`

**Tipo:** preparação de ação
**Objetivo:** gerar prévia consolidada do que será lançado no Moodle.

- [ ] Entrada:
  - `batchJobId`
  - `gradingItemIds`
  - `onlyReviewed`
- [ ] Saída:
  - `pendingActionId`
  - lista paginada de lançamentos;
  - totais;
  - bloqueios;
  - texto de confirmação literal.

#### Tool 7 — `confirmar_lancamento_lote_moodle`

**Tipo:** escrita no Moodle
**Objetivo:** lançar feedback e nota no Moodle após confirmação explícita.

- [ ] Entrada:
  - `pendingActionId`
  - `confirmationText`
- [ ] Saída:
  - status;
  - quantidade enviada;
  - quantidade falhada;
  - falhas por estudante;
  - `auditId`.

#### Tool 8 — `consultar_auditoria_correcao`

**Tipo:** leitura
**Objetivo:** consultar auditoria de uma ação.

- [x] Entrada por `auditId`.
- [x] Entrada por `batchJobId`.
- [x] Saída com histórico, ator, data/hora e resultado do envio ao Moodle.
- [x] Saída com versão do rascunho, arquivos analisados e hash.

### 3.3 Matriz de metadados Apps SDK

No projeto C#, estes hints são expressos pelos campos `ReadOnly`, `Destructive`, `Idempotent` e `OpenWorld` de `[McpServerTool]`.

| Tool | ReadOnly | Destructive | Idempotent | OpenWorld | Observação |
|---|---:|---:|---:|---:|---|
| `listar_entregas_corrigiveis` | true | false | true | false | Consulta paginada sem efeito colateral. |
| `criar_lote_correcao_assistida` | false | false | false | false | Cria job interno; só pode ser idempotente com `idempotencyKey`. |
| `consultar_status_lote_correcao` | true | false | true | false | Consulta status do job. |
| `cancelar_lote_correcao_assistida` | false | false | true | false | Cancela job interno; retry por `batchJobId` deve ser seguro. |
| `consultar_item_correcao_assistida` | true | false | true | false | Consulta rascunho/análise de um item. |
| `atualizar_rascunho_correcao` | false | false | true | false | Atualiza estado interno; exigir versão/hash para evitar sobrescrita acidental. |
| `criar_previa_lancamento_lote` | false | false | false | false | Cria `PendingMoodleAction`; só pode ser idempotente com chave de idempotência. |
| `confirmar_lancamento_lote_moodle` | false | true | true | false | Executa escrita oficial no Moodle após confirmação; retry por `pendingActionId` deve ser seguro. |
| `consultar_auditoria_correcao` | true | false | true | false | Consulta auditoria sem efeito colateral. |

---

## 4. Modelo de lote para 300–400 atividades

### 4.1 Estados do lote

```text
created
queued
collecting_context
fetching_files
extracting_text
analyzing
ready_for_review
partially_reviewed
ready_to_commit
committing
committed
completed_with_errors
cancelled
failed
```

### 4.2 Estados por item

```text
pending
context_ready
file_fetch_failed
file_extract_failed
analysis_ready
needs_teacher_review
blocked_missing_criteria
blocked_unreadable_file
reviewed
ready_to_commit
commit_pending
committed
commit_failed
```

### 4.3 Estratégia de processamento

- [ ] Coletar uma vez por atividade:
  - enunciado;
  - rubrica;
  - grade máxima;
  - escala;
  - tentativas;
  - critérios;
  - materiais de apoio.
- [ ] Cachear materiais comuns da atividade para não processar 400 vezes o mesmo arquivo.
- [ ] Baixar anexos dos estudantes sob demanda e com limite de tamanho.
- [ ] Gerar hash SHA-256 de cada arquivo.
- [ ] Extrair texto e criar resumo técnico por arquivo.
- [ ] Usar chunks quando o arquivo exceder limite de contexto.
- [ ] Gerar análise estruturada por critério.
- [ ] Salvar resultado como rascunho, não como nota oficial.
- [ ] Retornar ao ChatGPT apenas resumo e itens prontos; manter detalhes no backend/UI.

### 4.4 Concorrência sugerida

Valores devem ser configuráveis por ambiente.

- [ ] Moodle API:
  - `MOODLE_MAX_CONCURRENT_REQUESTS=5`
  - retry com backoff exponencial;
  - timeout por chamada;
  - circuit breaker.
- [ ] Download de arquivos:
  - `FILE_DOWNLOAD_WORKERS=5`
  - limite por arquivo;
  - limite total por lote.
- [ ] Extração:
  - `EXTRACTION_WORKERS=4`
  - sandbox de conversão;
  - timeout por arquivo.
- [ ] Análise IA:
  - `AI_ANALYSIS_WORKERS=3`
  - fila com prioridade;
  - custo estimado por lote;
  - persistência de resposta estruturada.
- [ ] Lançamento Moodle:
  - chunks de 10–50 itens;
  - idempotency key por item;
  - retry apenas em falhas transitórias.

### 4.5 Limites iniciais

- [x] `MAX_BATCH_ITEMS=400`
- [x] `MAX_FILE_SIZE_MB=25`
- [x] `MAX_FILES_PER_SUBMISSION=10`
- [x] `MAX_TEXT_CHARS_PER_SUBMISSION=120000`
- [x] `MAX_REVIEW_ITEMS_PER_PAGE=25`
- [x] `RAW_FILE_RETENTION_DAYS=7`
- [x] `DRAFT_RETENTION_DAYS=180` ou conforme política institucional.

---

## 5. Estrutura de dados

### 5.1 Tabelas principais

Não criar novas tabelas genéricas para ação pendente e auditoria sem necessidade. O projeto já possui:

- `moodle_pending_actions`;
- `moodle_confirmed_actions`;
- `moodle_audit_logs`;
- `moodle_connector_schema_versions`.

Adicionar tabelas específicas de correção em scripts versionados, por exemplo `003_grading_batches.sql`, executados pelo `ConnectorDbContextSchemaInitializer`.

```sql
grading_batch (
  id,
  course_id,
  assignment_ids_json,
  created_by_user_id,
  status,
  total_items,
  processed_items,
  ready_items,
  blocked_items,
  failed_items,
  created_at,
  updated_at
);

grading_item (
  id,
  batch_id,
  course_id,
  assignment_id,
  submission_id,
  moodle_user_id,
  attempt_number,
  status,
  suggested_grade,
  final_grade,
  confidence,
  draft_feedback,
  final_feedback,
  review_status,
  reviewed_by_user_id,
  reviewed_at,
  commit_status,
  commit_error,
  idempotency_key,
  created_at,
  updated_at
);

grading_artifact (
  id,
  grading_item_id,
  artifact_type,
  filename,
  mime_type,
  sha256,
  size_bytes,
  extraction_status,
  extracted_text_ref,
  summary_ref,
  created_at
);

grading_evidence (
  id,
  grading_item_id,
  criterion_id,
  criterion_text,
  max_points,
  suggested_points,
  evidence_text,
  gaps_text,
  teacher_review_required,
  created_at
);
```

Relação com estruturas existentes:

- [x] `grading_batch` e `grading_item` guardam o ciclo de correção assistida.
- [x] `grading_artifact` guarda metadados/hash de arquivos e referências ao texto extraído.
- [x] `grading_evidence` guarda evidências por critério.
- [x] `moodle_pending_actions.PayloadJson` guarda o payload final a lançar.
- [x] `moodle_pending_actions.PreviewJson` guarda a prévia sanitizada.
- [x] `moodle_audit_logs` guarda confirmação, commit e falhas de lançamento.
- [ ] `moodle_audit_logs` guarda criação de lote e revisão humana.

---

## 6. Contratos de payload

### 6.1 Criar lote

```json
{
  "courseId": 123,
  "assignmentIds": [456],
  "submissionIds": [],
  "maxItems": 400,
  "includeRubric": true,
  "includeSubmissionFiles": true,
  "includeCourseMaterials": true,
  "teacherInstructions": "Usar linguagem acolhedora, apontar evidências por critério e sugerir recuperação quando necessário.",
  "priority": "normal"
}
```

### 6.2 Saída do lote

```json
{
  "batchJobId": "gb_20260612_0001",
  "status": "queued",
  "acceptedItems": 386,
  "blockedItems": 14,
  "warnings": [
    "14 submissões foram bloqueadas por ausência de arquivo ou ausência de permissão de leitura."
  ]
}
```

### 6.3 Item de correção

```json
{
  "gradingItemId": "gi_001",
  "student": {
    "moodleUserId": 789,
    "displayName": "Nome do estudante"
  },
  "assignment": {
    "id": 456,
    "name": "SA 01 - Atividade Avaliativa",
    "gradeMax": 100
  },
  "analysis": {
    "suggestedGrade": 82,
    "confidence": "media",
    "criterionAnalysis": [
      {
        "criterionId": "C1",
        "criterion": "Identifica corretamente os riscos do cenário proposto.",
        "maxPoints": 30,
        "suggestedPoints": 24,
        "evidenceFound": "O estudante identificou riscos físicos e ergonômicos.",
        "gaps": "Não detalhou riscos químicos presentes no cenário.",
        "teacherReviewRequired": true
      }
    ],
    "feedbackToStudent": "Olá! Obrigado pelo envio da atividade...",
    "privateNotesToTeacher": "Revisar critério C3, pois a rubrica não explicita norma técnica."
  },
  "blocks": []
}
```

### 6.4 Prévia de lançamento

```json
{
  "pendingActionId": "pa_001",
  "items": [
    {
      "gradingItemId": "gi_001",
      "assignmentId": 456,
      "studentId": 789,
      "finalGrade": 85,
      "finalFeedbackPreview": "Olá! Obrigado pelo envio..."
    }
  ],
  "confirmationText": "CONFIRMO O LANÇAMENTO DE 1 CORREÇÃO NO MOODLE PARA O LOTE gb_20260612_0001"
}
```

---

## 7. Implementação no MCP Server

### 7.1 Estrutura de pastas sugerida

```text
moodle-conector/
  src/
    MoodleConnector.Domain/
      Grading/
        GradingBatch.cs
        GradingItem.cs
        GradingArtifact.cs
        GradingEvidence.cs
        GradingStatus.cs
    MoodleConnector.Application/
      Grading/
        CreateAssistedGradingBatchCommand.cs
        GetAssistedGradingBatchStatusQuery.cs
        GetAssistedGradingItemQuery.cs
        UpdateAssistedGradingDraftCommand.cs
        CreateGradingCommitPreviewCommand.cs
        ConfirmGradingCommitCommand.cs
      Abstractions/
        IMoodleAssignmentGradingGateway.cs
        IMoodleSubmissionFileGateway.cs
        IDocumentExtractionService.cs
        IGradingAnalysisService.cs
        IGradingBatchRepository.cs
    MoodleConnector.Infrastructure/
      Database/
        Scripts/
          003_grading_batches.sql
      MoodleAssignmentGradingGateway.cs
      MoodleSubmissionFileGateway.cs
      DocumentExtraction/
        DocumentExtractionService.cs
      Persistence/
        GradingBatchRepository.cs
    MoodleConnector.Presentation/
      Tools/
        Grading/
          MoodleGradingTools.cs
      wwwroot/
        grading-review.html  (fase posterior, se houver UI)
  tests/
    MoodleConnector.Application.Tests/
      Grading/
      Tools/
        Grading/
```

Notas de integração:

- [x] A classe `MoodleGradingTools` deve usar `[McpServerToolType]`, como as tools atuais.
- [x] Se o arquivo ficar em subpasta `Tools/Grading`, manter namespace compatível ou atualizar os `using` de `Program.cs`.
- [x] Registrar a classe em `Program.cs` com `mcpServerBuilder.WithTools<MoodleGradingTools>()`.
- [x] Atualizar `McpToolMetadataTests`: tools de consulta continuam `ReadOnly=true`; tools internas de preparo/rascunho usam `ReadOnly=false`, `Destructive=false`, `OpenWorld=false`; `confirmar_lancamento_lote_moodle` usa `ReadOnly=false`, `Destructive=true`, `Idempotent=true` e `OpenWorld=false`.

### 7.2 Registro de tool de leitura — exemplo C#

```csharp
[McpServerTool(
    Name = "consultar_status_lote_correcao",
    Title = "Consultar status de lote de correção",
    ReadOnly = true,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    UseStructuredContent = true,
    OutputSchemaType = typeof(ToolResponse<GradingBatchStatusResponse>))]
[Description("Consulta o andamento de um lote de correção assistida sem alterar dados no Moodle.")]
public async Task<CallToolResult> ConsultarStatusLoteCorrecaoAsync(
    string batchJobId,
    CancellationToken cancellationToken = default)
{
    var result = await mediator.Send(new GetAssistedGradingBatchStatusQuery(batchJobId), cancellationToken);
    return StandardResult(result);
}
```

Notas para o projeto:

- [ ] A autorização não deve depender apenas dos metadados da tool; validar no handler usando `ICurrentUserContext`.
- [ ] Para leitura do lote, validar se o usuário criou o lote, é revisor autorizado ou possui escopo administrativo.
- [ ] Retornar dados paginados e minimizados, seguindo o padrão das tools atuais.

### 7.3 Registro de tool de escrita no Moodle — exemplo C#

```csharp
[McpServerTool(
    Name = "confirmar_lancamento_lote_moodle",
    Title = "Confirmar lançamento de correções no Moodle",
    ReadOnly = false,
    Destructive = true,
    Idempotent = true,
    OpenWorld = false,
    UseStructuredContent = true,
    OutputSchemaType = typeof(ToolResponse<GradingCommitResult>))]
[Description("Lança notas e feedbacks no Moodle somente após confirmação explícita do professor.")]
public async Task<CallToolResult> ConfirmarLancamentoLoteMoodleAsync(
    Guid pendingActionId,
    string confirmationText,
    CancellationToken cancellationToken = default)
{
    var result = await mediator.Send(
        new ConfirmGradingCommitCommand(pendingActionId, confirmationText),
        cancellationToken);

    return StandardResult(result);
}
```

Notas para o projeto:

- [x] A tool de escrita deve chamar `IActionConfirmationService.ConfirmAsync`.
- [x] Usar `requiredScope: "moodle.write"` no MVP.
- [x] Além do escopo, validar `CanWrite = true` e feature flags antes da escrita.
- [x] Validar disponibilidade de `mod_assign_save_grade` no catálogo de funções antes do envio.
- [ ] Validar permissão Moodle real do professor/tutor no curso e na atividade.
- [x] Só depois da confirmação chamar `IMoodleAssignmentGradingGateway`.
- [x] Registrar sucesso/falha em `moodle_audit_logs`.

---

## 8. Integração Moodle

### 8.1 Funções a validar na instância

- [x] O projeto já usa `mod_assign_get_submissions` para leitura de entregas.
- [ ] `mod_assign_get_assignments`
- [ ] `mod_assign_get_submission_status`
- [ ] `mod_assign_get_grades`
- [ ] `mod_assign_save_grade`
- [ ] `mod_assign_save_grades`
- [ ] `core_files_get_files`
- [ ] `/webservice/pluginfile.php`
- [ ] `/webservice/upload.php`, se houver necessidade de enviar arquivos de feedback.
- [ ] Função para rubric/grading form, se disponível na instância.
- [ ] Função customizada se o Moodle atual não expuser todos os dados necessários.

Checklist prático de descoberta:

- [ ] Confirmar quais funções estão habilitadas no serviço usado por `MoodleApi:LoginService`.
- [ ] Confirmar se o mesmo token do usuário autenticado pode avaliar ou se será necessário `MoodleApi:WriteServiceToken`.
- [ ] Se usar `WriteServiceToken`, validar governança: ele não pode permitir escrita ampla sem checagem do professor/tutor autenticado no conector.
- [ ] Capturar respostas reais de erro do Moodle para falta de capability e mapear para mensagens claras no MCP.

### 8.2 Recomendação para anexos

- [ ] Preferir `/webservice/pluginfile.php` para baixar arquivos grandes.
- [ ] Evitar base64 para arquivos grandes, pois aumenta consumo de memória.
- [ ] Registrar `fileurl`, `filename`, `mimetype`, `filesize`, `sha256`.
- [ ] Respeitar permissões do usuário autenticado.

### 8.3 Recomendação para lançamento

- [x] Usar `mod_assign_save_grade` para envio individual.
- [ ] Usar `mod_assign_save_grades` para envio em lote, se habilitado e testado.
- [ ] Enviar em chunks.
- [ ] Tratar escala 0–10, 0–100, conceito ou rubrica conforme configuração da atividade.
- [ ] Validar nota máxima antes do commit.
- [ ] Não sobrescrever feedback existente sem confirmação explícita adicional.
- [x] No MVP, preferir envio individual com `mod_assign_save_grade` para simplificar auditoria e idempotência.
- [ ] Só usar lote Moodle nativo (`mod_assign_save_grades`) depois de testar falhas parciais e comportamento transacional.

---

## 9. Motor de análise pedagógica

### 9.1 Entrada mínima para sugerir nota

- [ ] Enunciado da atividade.
- [ ] Critérios ou rubrica.
- [ ] Nota máxima ou escala.
- [ ] Submissão legível.
- [ ] Tabela de conversão, quando aplicável.
- [ ] Instruções do professor, quando houver.

### 9.2 Saída obrigatória

- [ ] Parecer preliminar.
- [ ] Pontos fortes.
- [ ] Pontos a melhorar.
- [ ] Evidências por critério.
- [ ] Lacunas por critério.
- [ ] Nota ou conceito sugerido, quando houver base.
- [ ] Feedback ao estudante.
- [ ] Observações internas ao professor.
- [ ] Nível de confiança.
- [ ] Bloqueios ou limitações.

### 9.3 Bloqueios automáticos

- [ ] Ausência de critérios.
- [ ] Ausência de submissão.
- [ ] Arquivo ilegível.
- [ ] Arquivo protegido por senha.
- [ ] Arquivo corrompido.
- [ ] Escala de nota desconhecida.
- [ ] Rubrica incompatível com nota máxima.
- [ ] Suspeita de plágio ou autoria duvidosa, sem decisão automática.
- [ ] Conteúdo que dependa de julgamento presencial não evidenciado no arquivo.

---

## 10. Painel de revisão no ChatGPT

O painel é opcional e deve entrar depois do MVP `tool-only`. Se for implementado, seguir padrão desacoplado do Apps SDK:

- [ ] Tools de dados (`consultar_status_lote_correcao`, `consultar_item_correcao_assistida`, `atualizar_rascunho_correcao`) retornam `structuredContent` e `_meta` sem anexar template de UI.
- [ ] Criar uma render tool específica, por exemplo `renderizar_painel_correcao`, para anexar `_meta.ui.resourceUri`.
- [ ] Usar `_meta["openai/outputTemplate"]` apenas como alias de compatibilidade ChatGPT, não como contrato principal.
- [ ] Registrar recurso HTML com MIME `text/html;profile=mcp-app` quando o stack C# permitir expor recurso Apps UI.
- [ ] Versionar URI do recurso, por exemplo `ui://grading-review/v1.html`, para evitar cache quebrado quando o HTML/JS mudar.
- [ ] Definir `_meta.ui.csp.connectDomains` e `_meta.ui.csp.resourceDomains` com allowlist mínima.
- [ ] Evitar `frameDomains` salvo necessidade real de iframe interno.

### 10.1 Funções do painel

- [ ] Mostrar status do lote.
- [ ] Filtrar por:
  - pronto para revisão;
  - bloqueado;
  - baixa confiança;
  - nota sugerida abaixo do mínimo;
  - atraso;
  - falha de leitura.
- [ ] Abrir item individual.
- [ ] Exibir evidências por critério.
- [ ] Permitir edição de nota e feedback.
- [ ] Marcar como revisado.
- [ ] Gerar prévia de lançamento.
- [ ] Confirmar envio ao Moodle.

### 10.2 Dados visíveis ao modelo vs. dados visíveis só à UI

- [ ] Enviar ao modelo apenas dados necessários para resposta.
- [ ] Usar `_meta` da tool para hidratar UI com detalhes que não precisam entrar no transcript.
- [ ] Não expor anexos completos no chat se não for necessário.
- [ ] Não exibir dados sensíveis de todos os estudantes no resumo.

---

## 11. Segurança, privacidade e governança

### 11.1 Autenticação

- [ ] OAuth obrigatório.
- [x] OAuth do GPT App já funciona com `chatgpt-mcp`, PKCE e audience `/mcp`.
- [ ] No MVP, usar o escopo OAuth atual `moodle-mcp-audience` e autorização interna.
- [ ] Separar escopos por tool em fase posterior, se necessário, com migração cuidadosa do seed OpenIddict.
- [ ] Verificar token, emissor, audiência, expiração e escopos em toda chamada.
- [ ] Não autorizar escrita com token de leitura.
- [ ] Não confiar em `_meta.openai/userLocation`, `_meta.openai/session` ou outros hints para autorização.

### 11.2 Autorização

- [x] Validar `currentUser.HasScope("moodle.write")` para qualquer commit no Moodle.
- [x] Validar conexão `ConnectorClients.CanWrite = true`.
- [x] Validar `Features:AssignmentGradeWriteEnabled` e/ou `Features:AssignmentFeedbackWriteEnabled`.
- [ ] Validar se o professor/tutor tem permissão no curso.
- [ ] Validar se o professor/tutor pode corrigir a atividade.
- [ ] Validar se o estudante pertence à turma.
- [ ] Validar se a tentativa ainda é corrigível.
- [ ] Validar se a nota está dentro da escala.
- [ ] Validar se o feedback final foi revisado.
- [ ] Validar no Moodle a capability `mod/assign:grade` ou comportamento equivalente retornado pela API.

### 11.3 Confirmação humana

- [x] Tool de escrita deve exigir `confirmationText` literal.
- [x] Confirmação deve conter:
  - lote;
  - quantidade de correções;
  - curso;
  - atividade;
  - escopo do envio.
- [ ] Para sobrescrita de nota/feedback já existente, exigir confirmação adicional.

### 11.4 Auditoria

- [ ] Registrar:
  - quem criou o lote;
  - quem revisou;
  - quem confirmou;
  - quando confirmou;
  - qual texto foi enviado;
  - nota final;
  - nota sugerida;
  - se houve edição humana;
  - hash dos arquivos analisados;
  - retorno do Moodle.
- [ ] Gerar `auditId` para cada envio.
- [ ] Permitir exportação de auditoria para coordenação, quando autorizado.

### 11.5 Retenção

- [ ] Definir política de retenção de arquivos brutos.
- [ ] Remover arquivos brutos após prazo.
- [ ] Manter apenas hashes e metadados quando possível.
- [ ] Redigir PII em logs técnicos.
- [ ] Separar logs técnicos de dados pedagógicos.

---

## 12. Testes

### 12.1 Testes unitários

- [ ] Validação de payloads.
- [ ] Conversão de escala.
- [ ] Cálculo de notas por critério.
- [ ] Geração de confirmation text.
- [ ] Idempotência.
- [ ] Bloqueios por ausência de critério.
- [ ] Bloqueios por arquivo ilegível.

### 12.2 Testes de integração Moodle

- [ ] Consultar curso.
- [ ] Listar tarefa.
- [ ] Listar submissões.
- [ ] Baixar anexo.
- [ ] Extrair conteúdo.
- [ ] Enviar feedback em ambiente sandbox.
- [ ] Enviar nota em ambiente sandbox.
- [ ] Simular falha parcial em lote.

### 12.3 Testes de carga

- [ ] Lote com 25 entregas.
- [ ] Lote com 100 entregas.
- [ ] Lote com 400 entregas.
- [ ] Arquivos pequenos.
- [ ] Arquivos grandes.
- [ ] Mistura de PDF, DOCX, imagens e ZIP.
- [ ] Moodle lento.
- [ ] Falhas intermitentes.
- [ ] Retry e backoff.

### 12.4 Testes pedagógicos

- [ ] Atividade com rubrica completa.
- [ ] Atividade com critérios simples.
- [ ] Atividade sem critérios.
- [ ] Entrega excelente.
- [ ] Entrega parcial.
- [ ] Entrega fora do tema.
- [ ] Entrega com evidência insuficiente.
- [ ] Feedback satisfatório.
- [ ] Feedback de recuperação.
- [ ] Feedback sem linguagem punitiva.

### 12.5 Testes de segurança

- [ ] Prompt injection em arquivo do estudante.
- [ ] Tentativa de mandar o modelo ignorar rubrica.
- [ ] Tentativa de sobrescrever nota sem confirmação.
- [ ] Usuário sem permissão tentando lançar nota.
- [ ] Reuso de `pendingActionId`.
- [ ] Confirmação divergente.
- [ ] Token expirado.
- [ ] Token sem escopo de escrita.

---

## 13. Plano de implantação

### Fase 0 — Descoberta técnica

- [x] Criar tool consolidada `executar_descoberta_tecnica_correcao` para relatar funcoes Moodle, anexos, `mod_assign_save_grade`, permissao de escrita, rubricas/escalas e modo de token sem executar escrita.
- [ ] Executar a descoberta contra Moodle real/sandbox para transformar os itens abaixo de "relatado" em confirmacao operacional.

- [ ] Confirmar versão do Moodle.
- [ ] Confirmar como rubricas são expostas.
- [ ] Confirmar como anexos de submissão são acessados.
- [ ] Confirmar permissões de tutor/professor.
- [ ] Confirmar escalas de nota usadas.
- [ ] Confirmar política institucional de retenção de dados.

### Fase 1 — MVP sem escrita no Moodle

- [x] Criar entidades/DTOs mínimos de correção em C#.
- [x] Criar `criar_lote_correcao_assistida` para lotes pequenos inicialmente.
- [x] Criar `consultar_status_lote_correcao`.
- [x] Criar `cancelar_lote_correcao_assistida`.
- [x] Criar `consultar_item_correcao_assistida`.
- [x] Baixar anexos retornados por `mod_assign_get_submissions` quando houver `fileUrl`.
- [x] Extrair texto de TXT/HTML/JSON/XML/CSV e PDF com texto embutido, persistindo `GradingArtifact.ExtractedTextRef`.
- [ ] Extrair texto de PDF escaneado/imagem via OCR.
- [x] Gerar parecer preliminar revisavel quando houver texto extraido.
- [ ] Gerar nota sugerida confiavel quando rubrica/criterios e escala estiverem disponiveis.
- [ ] Permitir professor copiar o feedback manualmente.
- [ ] Validar qualidade pedagógica com tutores.
- [ ] Não criar UI complexa nesta fase.

### Fase 2 — Rascunho revisável

- [x] Criar `atualizar_rascunho_correcao`.
- [x] Persistir rascunhos em tabelas `grading_*`.
- [ ] Criar painel de revisão somente se a revisão via tools ficar ruim para o professor.
- [x] Salvar edições do professor.
- [x] Separar nota sugerida de nota final.
- [x] Marcar item como revisado.

### Fase 3 — Escrita controlada no Moodle

- [x] Criar `criar_previa_lancamento_lote`.
- [x] Criar `confirmar_lancamento_lote_moodle`.
- [x] Reusar `PendingActionService` e `ActionConfirmationService`.
- [x] Integrar com `mod_assign_save_grade` ou `mod_assign_save_grades`.
- [x] Implementar idempotência no retry por `pendingActionId` e status de item já lançado.
- [x] Expandir auditoria existente para commit Moodle.
- [x] Criar `consultar_auditoria_correcao` por `auditId`.
- [x] Expandir auditoria para criação de lote e revisão humana.
- [ ] Testar em sandbox.
- [ ] Fazer piloto com turma pequena.

### Fase 4 — Escala 300–400

- [ ] Ativar fila e workers.
- [x] Integrar `IGradingBatchOrchestrator.EnqueueAsync` no fluxo de criação de lote.
- [x] Implementar orquestrador local MVP para enfileirar/cancelar/consultar status.
- [x] Criar `GradingContext` e `GradingContextBuilder` MVP usando apenas `ExtractedTextRef` já persistido.
- [ ] Processar 400 entregas com paginação.
- [ ] Adicionar painel de filtros.
- [ ] Adicionar revisão em lote com exceções.
- [ ] Adicionar falhas recuperáveis.
- [ ] Adicionar métricas.
- [ ] Ajustar limites por custo, tempo e carga no Moodle.

### Fase 5 — Governança e publicação

- [ ] Revisar descrições das tools.
- [ ] Revisar escopos OAuth.
- [ ] Configurar RBAC no workspace.
- [ ] Testar Developer Mode.
- [ ] Escanear tools no ChatGPT.
- [ ] Publicar como app interno.
- [ ] Treinar tutores/professores.
- [ ] Criar manual de uso e política de responsabilidade docente.

---

## 14. Critérios de aceite

### 14.1 MVP

- [ ] O professor consegue selecionar atividade e criar lote.
- [ ] O sistema baixa anexos de submissões.
- [ ] O sistema extrai texto dos principais formatos.
- [ ] O sistema gera feedback coerente com critérios.
- [ ] O sistema informa limitações.
- [ ] O sistema não lança nota no Moodle.

### 14.2 Escrita controlada

- [x] O professor revisa nota e feedback antes do envio.
- [x] O sistema exibe prévia completa.
- [x] O sistema exige confirmação literal.
- [x] O sistema envia nota/feedback ao Moodle.
- [x] O sistema registra auditoria de commit Moodle.
- [x] O sistema evita duplicidade com idempotência.

### 14.3 Escala

- [ ] Lote com 400 entregas não trava o chat.
- [ ] O processamento ocorre por fila.
- [ ] O professor consegue revisar por páginas/filtros.
- [ ] Falhas parciais não bloqueiam todo o lote.
- [x] Lançamento parcial é auditável no commit Moodle.
- [ ] O sistema respeita limites de Moodle, arquivos e IA.

---

## 15. Backlog priorizado

### P0 — Obrigatório

- [ ] Descobrir funções Moodle disponíveis.
- [ ] Validar permissões/capabilities reais para professor/tutor corrigir atividades.
- [ ] Definir token de escrita: usuário autenticado vs. `WriteServiceToken`.
- [x] Implementar download seguro de anexos.
- [x] Implementar extração de texto.
- [ ] Implementar pacote de correção para uma entrega.
- [x] Implementar análise por critério para uma entrega.
- [x] Implementar revisão humana.
- [x] Implementar confirmação explícita.
- [x] Implementar auditoria de commit Moodle.
- [x] Implementar escrita controlada individual com `mod_assign_save_grade`.

### P1 — Importante

- [ ] Lote assíncrono.
- [ ] Painel UI no ChatGPT.
- [ ] Rubricas avançadas.
- [ ] Conversão de escala.
- [ ] Análise de baixa confiança.
- [ ] Exportação de relatório.
- [ ] Dashboard de produtividade.

### P2 — Evolutivo

- [ ] Comparação entre turmas.
- [ ] Banco de feedbacks recorrentes.
- [ ] Sugestão de recuperação.
- [ ] Detecção assistida de inconsistências.
- [ ] Relatório para coordenação pedagógica.
- [ ] Métricas de qualidade dos feedbacks.

---

## 16. Riscos e mitigação

| Risco | Mitigação |
|---|---|
| Lançamento indevido de nota | Confirmação literal, prévia, escopo de escrita separado e auditoria. |
| Alucinação na correção | Exigir critérios, evidências e bloqueio quando a base for insuficiente. |
| Exposição de dados de estudantes | Minimização, paginação, logs sem PII e retenção curta. |
| Sobrecarga do Moodle | Fila, concorrência limitada, backoff, cache e chunking. |
| Custo alto de IA em lote | Cache de materiais, análise incremental, limites por lote e resumos intermediários. |
| Arquivos ilegíveis | Bloqueio do item e solicitação de revisão manual. |
| Plágio/autoria duvidosa | Sinalização para verificação humana; sem punição automática. |
| Rubrica ausente | Gerar parecer, mas não nota final confiável. |
| Duplicidade de lançamento | Idempotency key por item e checagem de status antes do commit. |
| Falha parcial em lote | Commit transacional por item, relatório de falhas e reprocessamento seguro. |

---

## 17. Definition of Done técnico

- [x] Tools registradas no MCP Server.
- [x] `Program.cs` registra `MoodleGradingTools` via `.WithTools<MoodleGradingTools>()`.
- [x] Tools implementadas em C# no padrão `[McpServerTool]`.
- [x] Schemas validados.
- [x] Scripts SQL versionados adicionados em `src/MoodleConnector.Infrastructure/Database/Scripts`.
- [x] `ConnectorDbContextSchemaInitializer` executa os novos scripts.
- [x] Serviços registrados em `DependencyInjection`.
- [x] `McpToolMetadataTests` cobre a nova família de tools e permite escrita apenas nas tools de preparo/confirmação.
- [ ] `search` e `fetch` continuam presentes, read-only e compatíveis com company knowledge/deep research.
- [x] `confirmar_lancamento_lote_moodle` expõe metadata conservadora: `ReadOnly=false`, `Destructive=true`, `Idempotent=true`, `OpenWorld=false`.
- [x] Tools que criam job ou pending action não são marcadas como idempotentes sem chave de idempotência.
- [ ] OAuth funcionando.
- [x] Autorização server-side validando `moodle.write`, `CanWrite` e feature flag.
- [ ] Autorização server-side validando permissão Moodle real no curso/atividade.
- [ ] UI opcional, se existir, usa render tool separada, `_meta.ui.resourceUri`, CSP mínima e URI versionada.
- [ ] Fila processando lotes.
- [ ] Download de anexos testado.
- [ ] Extração testada.
- [ ] Análise estruturada testada.
- [x] Prévia de lançamento testada.
- [x] Confirmação literal testada.
- [ ] Envio ao Moodle testado em sandbox.
- [ ] Endpoint `/mcp` reescaneado no ChatGPT Developer Mode após mudança de tool metadata.
- [x] Auditoria de commit Moodle testada.
- [x] `dotnet build` sem erros.
- [x] `dotnet test` passando.
- [ ] Testes de carga executados.
- [ ] Documentação de operação criada.
- [ ] Piloto pedagógico aprovado.

---

## 18. Observações finais

- A implementação real no Moodle depende de acesso ao repositório, ambiente de homologação, tokens, lista de web services habilitados e política de permissões.
- O primeiro deploy deve ser **somente leitura + rascunho**.
- A escrita no Moodle deve entrar apenas após piloto, validação pedagógica e autorização da coordenação/gestão responsável.
- O desenho recomendado para 300–400 atividades é um **orquestrador de lote**, não um conjunto de chamadas manuais feitas pelo modelo.
- A decisão final de nota e feedback permanece com o professor/tutor.
