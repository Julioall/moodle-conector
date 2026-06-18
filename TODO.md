# TODO.md — Correção Assistida SENAI para AVA/Moodle via GPT Apps

**Status atual:** MVP funcional avançado, validado em leitura real em dois cursos Moodle.
**Data-base atualizada:** 2026-06-17
**Repositório:** `moodle-conector`
**Objetivo:** transformar o GPT App/Conector do AVA em uma ferramenta de consulta, análise, rascunho, revisão e escrita assistida para apoiar professores/tutores na correção de atividades no Moodle, mantendo decisão humana, rastreabilidade, segurança institucional e confirmação explícita antes de qualquer escrita oficial.

---

## 0. Estado Atual Confirmado

O núcleo de correção assistida já está implementado no projeto C#/.NET atual, sem servidor paralelo.

Fluxo implementado:

1. descoberta técnica das funções Moodle de correção;
2. listagem de entregas corrigíveis;
3. criação de lote interno de correção assistida;
4. download de anexos da submissão quando há `fileUrl`;
5. extração de TXT, HTML, JSON, XML, CSV, PDF com texto embutido, DOCX, PPTX, XLSX, OpenDocument e chunking representativo para textos muito grandes;
6. varredura de materiais da seção da tarefa;
7. persistência de artefatos `submission_file` e `assignment_context`;
8. seleção heurística do provável enunciado/material de contexto;
9. tool de diagnóstico sanitizado do contexto selecionado;
10. montagem de `GradingContext` usando `ExtractedTextRef` persistido;
11. geração de rascunho preliminar;
12. sugestão de nota quando há critérios e valor máximo suficientes;
13. persistência e exposição de evidências/lacunas por critério;
14. consulta de status do lote;
15. consulta detalhada do item;
16. revisão humana de nota e feedback;
17. prévia de lançamento;
18. confirmação literal;
19. escrita individual controlada no Moodle via `mod_assign_save_grade`;
20. auditoria de criação de lote, revisão, bloqueios e commit Moodle;
21. proteções contra reenvio, prévia obsoleta, nota existente, tentativa/submissão alterada, feedback existente e estudante não inscrito antes do commit;
22. exportação de relatório consolidado do lote para coordenação, com contadores, atenção e critérios com lacunas.

Estado operacional correto:

- [x] Pronto para testes controlados no conector e sandbox/homologação.
- [x] Pronto para piloto pequeno sem escrita ampla em produção.
- [x] Conector validado em leitura real nos cursos `29972` e `33442` do Moodle Goiás/FIEG.
- [ ] Ainda não pronto para uso massivo 300–400 entregas sem fila real, governança e testes de carga.
- [ ] Ainda não pronto para liberar escrita em produção sem validação institucional de permissões, rubricas/escalas e token de escrita.

---

## 1. Evidências no Código

### 1.1 Tools registradas no MCP

- [x] `MoodleGradingTools` registrado em `Program.cs`.
- [x] `MoodleGradingContextDiagnosticsTools` registrado em `Program.cs`.
- [x] Tools principais usam `[McpServerTool]` com metadata MCP.
- [x] `McpToolMetadataTests` cobre a família de tools de correção.

Tools de correção expostas:

| Tool | Estado | Tipo |
|---|---|---|
| `descobrir_funcoes_moodle_correcao` | Implementada | leitura |
| `discover_moodle_grading_functions` | Implementada | leitura |
| `executar_descoberta_tecnica_correcao` | Implementada | leitura |
| `listar_entregas_corrigiveis` | Implementada | leitura |
| `criar_lote_correcao_assistida` | Implementada | criação interna |
| `consultar_status_lote_correcao` | Implementada | leitura |
| `exportar_relatorio_correcao_coordenacao` | Implementada | leitura |
| `cancelar_lote_correcao_assistida` | Implementada | escrita interna |
| `consultar_item_correcao_assistida` | Implementada | leitura |
| `consultar_contexto_item_correcao_assistida` | Implementada e registrada | leitura |
| `atualizar_rascunho_correcao` | Implementada | escrita interna |
| `criar_previa_lancamento_lote` | Implementada | ação pendente |
| `confirmar_lancamento_lote_moodle` | Implementada | escrita Moodle |
| `consultar_auditoria_correcao` | Implementada | leitura |
| `consultar_auditoria_correcao_lote` | Implementada | leitura |

### 1.2 Serviços e gateways implementados

- [x] `IMoodleSubmissionFileGateway`
- [x] `IDocumentExtractionService`
- [x] `IAssignmentContextSelectionService`
- [x] `HeuristicAssignmentContextSelectionService`
- [x] `IGradingContextBuilder`
- [x] `GradingContextBuilder`
- [x] `IGradingAnalysisService`
- [x] `StructuredGradingAnalysisService`
- [x] `IGradingBatchOrchestrator`
- [x] `LocalGradingBatchOrchestrator`
- [x] `IGradingReviewRepository`
- [x] `IMoodleAssignmentGradingGateway`
- [x] `IMoodleAssignmentGradeReadGateway`
- [x] `IMoodleAssignmentSubmissionStatusGateway`
- [x] `IMoodleGradingCapabilitiesGateway`
- [x] `IMoodleAuditLogRepository`

### 1.3 Persistência

- [x] Scripts SQL versionados para `grading_batch`, `grading_item`, `grading_artifact` e `grading_evidence`.
- [x] `GradingArtifact.ExtractedTextRef` persiste texto extraído.
- [x] `GradingEvidence` persiste evidências/lacunas por critério.
- [x] `moodle_pending_actions` guarda prévias e payloads de confirmação.
- [x] `moodle_audit_logs` guarda eventos de commit e bloqueios.

---

## 2. Validações Moodle Já Confirmadas

Com base nos testes manuais relatados no Moodle Goiás/FIEG:

- [x] `mod_assign_get_assignments` disponível.
- [x] `mod_assign_get_submissions` disponível.
- [x] `mod_assign_get_submission_status` disponível.
- [x] `mod_assign_get_grades` disponível.
- [x] `mod_assign_save_grade` disponível.
- [x] `mod_assign_save_grades` disponível.
- [x] `core_files_get_files` disponível.
- [x] Anexo PDF real foi detectado e baixado.
- [x] PDF com texto embutido foi extraído com sucesso.
- [x] Item antes bloqueado por falta de conteúdo legível passou para `DraftReady`.
- [x] Escrita permaneceu bloqueada por feature flag, como esperado.
- [x] Fluxo de ação pendente com confirmação humana validado no conector Moodle.
- [x] Prévia de ação demonstrativa gerada sem escrita real.
- [x] Confirmação literal exigida antes de qualquer ação sensível.

### 2.1 Validação em Leitura Real (2026-06-17, commit `375b4b8`)

**Curso 29972 — Manutenção de Sistemas:**

- [x] Curso real localizado via `_get_course`.
- [x] Atividades do curso listadas via `_list_course_activities` (7 atividades: 2 tarefas, 1 SCORM, 1 quiz, 3 fóruns).
- [x] Tarefas listadas via `_list_course_assignments`.
- [x] Tarefa `Envio SAP 01 - Etapa 1` (CMID 941458, Instance 101112) localizada por CMID.
- [x] Submissão real aguardando correção localizada via `_list_submissions_awaiting_grading` (1 entrega, status `submitted`, `notgraded`, 1 arquivo, tentativa 0).
- [x] Material de contexto `SAP 01.pdf` localizado na seção `Situações de Aprendizagem`.

**Curso 33442 — Saude e Segurança do Trabalho:**

- [x] Curso real localizado via `_get_course`.
- [x] Atividades listadas (12 atividades: 5 tarefas, 1 SCORM, 1 quiz, 5 fóruns).
- [x] Listagem de atividades com datas/prazos validada.
- [x] Listagem de arquivos de curso validada com formatos PDF externo, PPSX e PNG.
- [x] Tarefa `Poste Aqui a Superação B` (CMID 1039037, Instance 110669) localizada.
- [x] 16 submissões aguardando correção localizadas (todas `submitted`/`notgraded`, maioria com 1 arquivo, uma com 3).

Pendências Moodle reais:

- [ ] Confirmar versão do Moodle.
- [ ] Confirmar endpoint confiável para rubricas/grading forms.
- [ ] Confirmar escalas/conceitos usados nas atividades reais.
- [ ] Confirmar permissões reais de professor/tutor por curso e atividade.
- [ ] Confirmar se `WriteServiceToken` será usado ou se a escrita deve usar token do usuário autenticado.
- [ ] Validar governança do `WriteServiceToken`, caso seja usado.
- [ ] Testar envio de nota/feedback em sandbox/homologação com feature flags habilitadas.

### 2.2 Problemas Detectados na Validação Real

- [ ] Investigar instabilidade do endpoint MCP em buscas textuais (`_search` retornou `Connection failed`).
- [x] Adicionar tratamento mais claro para falha de conexão em `_search` (agora inclui `ex.GetType().Name`).
- [ ] Registrar erro com `auditId` ou `correlationId` quando houver falha de rede.
- [x] Documentar que `_search` pesquisa cursos, não arquivos internos (Description atualizada nas tools `buscar_cursos`, `search_courses` e `search`).
- [ ] Criar ou expor busca específica para materiais/atividades do curso.
- [x] Validar se `fullName: null` nas submissões é decisão de privacidade, falta de permissão ou limitação da tool (confirmado: vem da API `core_enrol_get_enrolled_users` via `IMoodleParticipantsGateway`; quando o token não tem permissão, o Moodle omite os nomes).
- [ ] Se permitido institucionalmente, retornar nome do aluno para revisão docente.
- [x] Se não permitido, exibir explicitamente que os nomes foram omitidos por política de privacidade (warning adicionado no response de `listar_entregas_corrigiveis`).

---

## 3. Segurança e Escrita Moodle

Implementado:

- [x] Escrita exige confirmação literal via `IActionConfirmationService`.
- [x] Commit exige escopo `moodle.write`.
- [x] Commit valida `ConnectorClients.CanWrite`.
- [x] Commit valida `AssignmentGradeWriteEnabled`.
- [x] Commit valida `AssignmentFeedbackWriteEnabled` quando feedback textual é enviado.
- [x] Commit valida disponibilidade de `mod_assign_save_grade`.
- [x] Commit bloqueia quando não consegue validar nota existente via `mod_assign_get_grades`.
- [x] Commit bloqueia sobrescrita de nota existente.
- [x] Commit bloqueia prévia obsoleta por hash de versão do rascunho.
- [x] Commit é idempotente por item já lançado/status de commit.
- [x] Commit valida tentativa/status atual da submissão antes de escrever:
  - bloqueia quando a tentativa mudou;
  - bloqueia quando a submissão atual não está `submitted`.
- [x] Auditoria registra sucesso, falha e bloqueios de commit.

Ainda pendente:

- [ ] Validar capability real `mod/assign:grade` por professor/tutor no curso/atividade.
- [x] Validar se o estudante pertence à turma no momento do commit.
- [x] Bloquear sobrescrita de feedback existente quando houver leitura confiável do feedback atual.
- [ ] Separar escopos OAuth por domínio em fase posterior.
- [ ] Definir política de retenção de arquivos brutos e textos extraídos.
- [ ] Redigir PII em logs técnicos.
- [ ] Separar logs técnicos de dados pedagógicos sensíveis.

---

## 4. Contexto da Tarefa e Enunciado

Implementado:

- [x] `criar_lote_correcao_assistida` coleta materiais da seção/atividade quando `includeCourseMaterials=true`.
- [x] Materiais são persistidos como artefatos `assignment_context`.
- [x] `HeuristicAssignmentContextSelectionService` seleciona o candidato mais provável de enunciado/contexto.
- [x] `GradingContextBuilder` usa o contexto selecionado na análise.
- [x] `consultar_contexto_item_correcao_assistida` permite auditar qual documento foi escolhido.
- [x] Diagnóstico retorna arquivo selecionado, pontuação/confiança, classificação, motivo, caracteres/palavras e lista sanitizada de artefatos.

Ainda pendente:

- [x] Retestar no conector: material de contexto `SAP 01.pdf` localizado na seção correta do curso real `29972`.
- [x] Retestar no conector: materiais `Descrição Superação B.PNG` e `Descrição Superação B_Parte 2.PNG` localizados no curso `33442`.
- [ ] Confirmar se `selectedContextFileName` aponta para o SAP correto no curso de teste real.
- [ ] Evoluir seleção heurística se houver falso positivo em materiais administrativos.
- [ ] Futuro: plugar seleção por IA opcional apenas quando houver infraestrutura barata/viável.

---

## 5. Extração de Documentos

Implementado:

- [x] TXT.
- [x] HTML.
- [x] JSON.
- [x] XML.
- [x] CSV.
- [x] PDF com texto embutido via PdfPig.
- [x] DOCX.
- [x] PPTX.
- [x] XLSX.
- [x] ODT/ODS/ODP.
- [x] ZIP com múltiplos arquivos internos suportados.
- [x] Bloqueio específico para PDF escaneado ou sem texto extraível (`scanned_pdf`).
- [x] Falha estruturada para arquivo corrompido em formatos suportados.
- [x] Bloqueio claro quando não há conteúdo legível suficiente.
- [x] Chunking representativo para submissões muito grandes, preservando trechos distribuídos pelo documento dentro do limite de contexto.

Ainda pendente:

- [ ] Imagens.
- [ ] OCR para PDF escaneado/imagem.
- [ ] Arquivo protegido por senha.

---

## 6. Motor Pedagógico

Implementado:

- [x] Parecer preliminar quando há conteúdo legível.
- [x] Critérios extraídos de contexto textual quando possível.
- [x] Nota sugerida quando há critérios e pontuação máxima suficientes.
- [x] Evidências por critério.
- [x] Lacunas por critério.
- [x] Feedback ao estudante.
- [x] Confiança inicial.
- [x] Observações internas ao professor/tutor no rascunho.
- [x] Sinalização de baixa confiança para revisão humana.
- [x] Pendências para revisão humana.
- [x] Bloqueio quando falta conteúdo legível.
- [x] Relatório consolidado para coordenação com itens que exigem atenção e critérios com lacunas.

Ainda pendente:

- [ ] Rubrica real do Moodle.
- [ ] Escalas/conceitos reais do Moodle.
- [ ] Conversão robusta de escala.
- [ ] Suspeita de plágio/autoria duvidosa apenas como sinalização humana.
- [ ] Validação pedagógica com tutores antes de uso amplo.

---

## 7. Escala 300–400

Implementado no MVP:

- [x] Lote interno com `batchJobId`.
- [x] Status por lote.
- [x] Consulta paginada/sumarizada.
- [x] Revisão individual sob demanda.
- [x] Confirmação por subconjuntos revisados.
- [x] Limites configuráveis para lote, arquivo, quantidade de arquivos e texto.
- [x] `LocalGradingBatchOrchestrator` com enqueue/cancel/status local.
- [x] Timeout, retry/backoff e circuit breaker HTTP configuráveis nos gateways Moodle.
- [x] Cache de materiais comuns por curso+tarefa durante a criação do lote.
- [x] Falhas recuperáveis por item no orquestrador local.
- [x] Retomada local de lote parcialmente processado, reprocessando apenas itens ainda pendentes.

Ainda pendente para escala real:

- [ ] Fila real com workers/background service.
- [ ] Concorrência configurável por Moodle/download/extração/análise.
- [ ] Retomada durável de lote parcialmente processado após queda de processo/worker.
- [ ] Métricas de tempo, custo, falha e carga Moodle.
- [ ] Teste de carga com 25 entregas.
- [ ] Teste de carga com 100 entregas.
- [ ] Teste de carga com 400 entregas.

---

## 8. Painel UI no ChatGPT

Estado atual:

- [x] MVP funciona como `tool-only`.
- [x] Tools retornam `structuredContent` suficiente para revisão via chat.
- [ ] Nenhum painel UI obrigatório implementado.

Backlog:

- [ ] Criar render tool específica apenas se a revisão por chat/tools ficar ruim.
- [ ] Usar `_meta.ui.resourceUri`.
- [ ] Usar CSP restritiva.
- [ ] Exibir status do lote.
- [ ] Filtrar por status, atividade e confiança.
- [ ] Abrir item individual.
- [ ] Exibir evidências por critério.
- [ ] Permitir edição de nota e feedback.
- [ ] Gerar prévia e confirmação de envio.

---

## 9. Testes

Estado atual:

- [x] Testes de domínio de grading.
- [x] Testes de criação de lote.
- [x] Testes de download/extração de anexo.
- [x] Testes de persistência de `assignment_context`.
- [x] Testes de seleção heurística de contexto.
- [x] Testes de diagnóstico de contexto.
- [x] Testes de extração de critérios e nota máxima.
- [x] Testes de status/cancelamento de lote.
- [x] Testes de detalhe de item.
- [x] Testes de evidências por critério.
- [x] Testes de revisão humana.
- [x] Testes de prévia e confirmação de lançamento.
- [x] Testes de bloqueio por nota existente.
- [x] Testes de bloqueio por prévia obsoleta.
- [x] Testes de bloqueio por tentativa/submissão alterada.
- [x] Testes de auditoria.
- [x] Testes de metadata MCP.
- [x] Testes de resiliência HTTP Moodle.
- [x] Testes de extração DOCX/PPTX/XLSX/ODT/ODS/ODP.
- [x] Testes de extração ZIP e arquivo corrompido.
- [x] Testes de cache de contexto por atividade no lote.
- [x] Testes de falha parcial recuperável por item.
- [x] Testes de retomada local de lote parcialmente processado.
- [x] Testes de observações internas e baixa confiança no rascunho.
- [x] Testes de relatório consolidado para coordenação.
- [x] Testes de chunking representativo para texto muito grande.
- [x] Testes de bloqueio por estudante não inscrito.
- [x] Testes de bloqueio por feedback existente.

Última verificação conhecida:

- [x] `dotnet.exe test MoodleConnector.slnx` passando com 211 testes em 2026-06-15.
- [x] `dotnet.exe test tests/MoodleConnector.Application.Tests` passando com 243 testes em 2026-06-17.
- [x] `dotnet.exe test tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj --filter DocumentExtractionServiceTests` passando com 19 testes em 2026-06-15.

### 9.1 Validação Manual no Conector Moodle (2026-06-17)

- [x] Conector Moodle validado em leitura real no curso `29972 - Manutenção de Sistemas`.
- [x] Conector Moodle validado em leitura real no curso `33442 - Saude e Segurança do Trabalho`.
- [x] Fluxo de ação pendente com confirmação humana validado.
- [ ] Tools de correção assistida (lote, contexto, rascunho, prévia, commit, auditoria) ainda não expostas no GPT App/MCP para teste ponta a ponta.

Ainda pendente:

- [ ] Testes de integração em sandbox para escrita real.
- [ ] Testes de carga 25/100/400.
- [ ] Testes com imagens e PDF escaneado/OCR quando suporte for implementado.
- [x] Testes de token expirado e token sem escopo de escrita.
- [ ] Testes de usuário sem permissão Moodle real para corrigir.
- [ ] Reescanear `/mcp` no ChatGPT Developer Mode após mudanças de metadata/tools.
- [ ] Expor no GPT App/MCP as tools específicas de correção assistida para teste real ponta a ponta.

---

## 10. Próximas Entregas Priorizadas

### P0 — antes de liberar escrita real

- [x] Retestar no conector `consultar_contexto_item_correcao_assistida`.
- [x] Retestar no conector no curso de teste (`curso_12345`) e confirmar seleção do SAP correto (`SAP_01.pdf`).
- [x] Validar leitura real em dois cursos Moodle (29972 e 33442).
- [x] Validar fluxo de confirmação humana demonstrativo.
- [ ] Retestar no conector no curso de homologação (`curso_67890`) e confirmar SAP 01/SAP 02 por atividade.
- [ ] Confirmar rubricas/escalas reais no Moodle.
- [ ] Confirmar permissões/capabilities reais de professor/tutor.
- [ ] Definir definitivamente token de escrita: usuário autenticado ou `WriteServiceToken`.
- [ ] Rodar teste de escrita em sandbox/homologação com feature flags habilitadas.
- [ ] Documentar política de responsabilidade docente e retenção.
- [ ] Expor tool de diagnóstico de capabilities/permissões por curso e atividade.
- [ ] Expor tool de leitura de rubricas/grading forms quando disponível.
- [ ] Expor tool de leitura de escala/nota máxima da atividade.
- [ ] Expor tool de diagnóstico de versão/serviços Moodle disponíveis.

### P1 — robustez pedagógica e formatos

- [x] Implementar DOCX.
- [x] Implementar PPTX.
- [x] Implementar XLSX.
- [x] Implementar ODT/ODS/ODP.
- [x] Implementar ZIP e múltiplos arquivos internos.
- [x] Implementar OCR ou bloqueio específico para PDF escaneado.
- [x] Melhorar extração de critério/valor para SAP com `Valor da atividade` e critérios na mesma linha do cabeçalho.
- [ ] Melhorar extração de rubrica/critério/escala para outros formatos reais do Moodle.
- [x] Melhorar análise de baixa confiança.
- [x] Adicionar observações internas ao professor.
- [x] Exportar relatório consolidado para coordenação.

### P2 — escala e operação

- [ ] Implementar fila real com workers.
- [x] Cachear materiais comuns por atividade/seção.
- [x] Adicionar retry/backoff/circuit breaker.
- [ ] Adicionar métricas e dashboard operacional.
- [ ] Criar painel UI opcional.
- [ ] Executar testes de carga.
- [ ] Preparar manual de uso.

---

## 11. Critérios de Aceite

### 11.1 MVP de rascunho

- [x] Professor/tutor consegue criar lote.
- [x] Sistema baixa anexos de submissões quando há `fileUrl`.
- [x] Sistema extrai texto dos formatos suportados.
- [x] Sistema escaneia materiais da seção da tarefa.
- [x] Sistema seleciona contexto/enunciado provável heuristicamente.
- [x] Sistema expõe diagnóstico do contexto escolhido.
- [x] Sistema gera feedback preliminar quando há conteúdo legível.
- [x] Sistema sugere nota quando há critérios e valor máximo suficientes.
- [x] Sistema informa limitações/bloqueios.
- [x] Sistema não lança nota sem prévia e confirmação.
- [ ] Qualidade pedagógica validada com tutores.

### 11.2 Escrita controlada

- [x] Professor/tutor revisa nota e feedback antes do envio.
- [x] Sistema exibe prévia completa.
- [x] Sistema exige confirmação literal.
- [x] Sistema executa commit individual via `mod_assign_save_grade`.
- [x] Sistema registra auditoria de commit Moodle.
- [x] Sistema evita duplicidade com idempotência.
- [x] Sistema bloqueia sobrescrita de nota existente.
- [x] Sistema bloqueia sobrescrita de feedback existente.
- [x] Sistema bloqueia commit com prévia obsoleta.
- [x] Sistema bloqueia commit quando a tentativa/submissão mudou.
- [x] Sistema bloqueia commit quando estudante não está inscrito no curso.
- [ ] Envio validado em sandbox/homologação.
- [ ] Piloto pedagógico aprovado.

### 11.3 Escala

- [ ] Lote com 400 entregas não trava o chat.
- [ ] Processamento ocorre por fila/workers reais.
- [x] Materiais comuns são cacheados dentro do lote.
- [ ] Professor/tutor consegue revisar por páginas/filtros.
- [x] Falhas parciais não bloqueiam todo o lote.
- [x] Lançamento parcial é auditável no commit Moodle.
- [ ] Sistema respeita limites de Moodle, arquivos e IA em teste de carga.

---

## 12. Definition of Done Técnico

- [x] Tools registradas no MCP Server.
- [x] `Program.cs` registra `MoodleGradingTools`.
- [x] `Program.cs` registra `MoodleGradingContextDiagnosticsTools`.
- [x] Tools implementadas em C# com `[McpServerTool]`.
- [x] Schemas estruturados usando `ToolResponse<T>`.
- [x] Scripts SQL versionados adicionados.
- [x] `ConnectorDbContextSchemaInitializer` executa scripts.
- [x] Serviços principais registrados em DI.
- [x] Autorização server-side valida `moodle.write`, `CanWrite` e feature flags para escrita.
- [x] Prévia de lançamento implementada.
- [x] Confirmação literal implementada.
- [x] Auditoria de commit Moodle implementada.
- [x] Testes automatizados cobrem o núcleo do fluxo.
- [x] `dotnet test` passando na última verificação conhecida.
- [ ] Autorização server-side valida permissão Moodle real no curso/atividade.
- [ ] Endpoint `/mcp` reescaneado no ChatGPT Developer Mode após mudanças recentes.
- [ ] Envio ao Moodle testado em sandbox.
- [ ] Testes de carga executados.
- [ ] Documentação de operação criada.
- [ ] Piloto pedagógico aprovado.

---

## 13. Observações Finais

- O primeiro deploy operacional deve continuar como leitura + rascunho, sem escrita ampla.
- Escrita no Moodle deve ser usada apenas após sandbox, piloto pequeno e autorização institucional.
- O caminho de baixo custo para seleção de enunciado agora é heurístico; IA local/Ollama foi descartada por enquanto por limitação de VPS.
- Para 300–400 entregas, a próxima grande fronteira técnica é fila real com workers, cache de materiais e métricas.
- A decisão final de nota e feedback permanece sempre com o professor/tutor.
