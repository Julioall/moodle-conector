# TODO.md — Correção Assistida SENAI para AVA/Moodle via GPT Apps

**Status atual:** MVP funcional avançado em validação técnica controlada.
**Data-base atualizada:** 2026-06-14
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
5. extração de TXT, HTML, JSON, XML, CSV e PDF com texto embutido;
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
21. proteções contra reenvio, prévia obsoleta, nota existente e tentativa/submissão alterada antes do commit.

Estado operacional correto:

- [x] Pronto para testes controlados no conector e sandbox/homologação.
- [x] Pronto para piloto pequeno sem escrita ampla em produção.
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

Pendências Moodle reais:

- [ ] Confirmar versão do Moodle.
- [ ] Confirmar endpoint confiável para rubricas/grading forms.
- [ ] Confirmar escalas/conceitos usados nas atividades reais.
- [ ] Confirmar permissões reais de professor/tutor por curso e atividade.
- [ ] Confirmar se `WriteServiceToken` será usado ou se a escrita deve usar token do usuário autenticado.
- [ ] Validar governança do `WriteServiceToken`, caso seja usado.
- [ ] Testar envio de nota/feedback em sandbox/homologação com feature flags habilitadas.

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
- [ ] Validar se o estudante pertence à turma no momento do commit.
- [ ] Bloquear sobrescrita de feedback existente quando houver leitura confiável do feedback atual.
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

- [ ] Retestar no conector a tool `consultar_contexto_item_correcao_assistida` no curso `29972`.
- [ ] Confirmar se `selectedContextFileName` aponta para o SAP correto no curso `29972`.
- [ ] Confirmar no curso `32787` se SAP 01/SAP 02 são escolhidos conforme a atividade.
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
- [x] Bloqueio claro quando não há conteúdo legível suficiente.

Ainda pendente:

- [ ] DOCX.
- [ ] PPTX.
- [ ] XLSX.
- [ ] ODT/ODS/ODP.
- [ ] Imagens.
- [ ] PDF escaneado/OCR.
- [ ] ZIP e múltiplos arquivos internos.
- [ ] Arquivo protegido por senha.
- [ ] Arquivo corrompido.
- [ ] Chunking para submissões muito grandes além do limite atual.

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
- [x] Pendências para revisão humana.
- [x] Bloqueio quando falta conteúdo legível.

Ainda pendente:

- [ ] Rubrica real do Moodle.
- [ ] Escalas/conceitos reais do Moodle.
- [ ] Conversão robusta de escala.
- [ ] Observações internas ao professor/tutor.
- [ ] Melhor classificação de baixa confiança.
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

Ainda pendente para escala real:

- [ ] Fila real com workers/background service.
- [ ] Concorrência configurável por Moodle/download/extração/análise.
- [ ] Retry/backoff/circuit breaker operacional.
- [ ] Cache de materiais comuns por atividade/seção.
- [ ] Retomada segura de lote parcialmente processado.
- [ ] Falhas recuperáveis por item.
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

Última verificação conhecida:

- [x] `dotnet test MoodleConnector.slnx` passando com 185 testes em 2026-06-14.

Ainda pendente:

- [ ] Testes de integração em sandbox para escrita real.
- [ ] Testes de carga 25/100/400.
- [ ] Testes com DOCX/PPTX/XLSX/ODT/imagem quando suporte for implementado.
- [ ] Testes de token expirado e token sem escopo de escrita.
- [ ] Testes de usuário sem permissão Moodle real para corrigir.
- [ ] Reescanear `/mcp` no ChatGPT Developer Mode após mudanças de metadata/tools.

---

## 10. Próximas Entregas Priorizadas

### P0 — antes de liberar escrita real

- [x] Retestar no conector `consultar_contexto_item_correcao_assistida`.
- [x] Retestar no conector curso `29972` e confirmar seleção do SAP correto (`SAP 01.pdf`).
- [ ] Retestar no conector curso `32787` e confirmar SAP 01/SAP 02 por atividade.
- [ ] Confirmar rubricas/escalas reais no Moodle.
- [ ] Confirmar permissões/capabilities reais de professor/tutor.
- [ ] Definir definitivamente token de escrita: usuário autenticado ou `WriteServiceToken`.
- [ ] Rodar teste de escrita em sandbox/homologação com feature flags habilitadas.
- [ ] Documentar política de responsabilidade docente e retenção.

### P1 — robustez pedagógica e formatos

- [ ] Implementar DOCX.
- [ ] Implementar PPTX.
- [ ] Implementar XLSX.
- [ ] Implementar ODT/ODS/ODP.
- [ ] Implementar OCR ou bloqueio específico para PDF escaneado.
- [x] Melhorar extração de critério/valor para SAP com `Valor da atividade` e critérios na mesma linha do cabeçalho.
- [ ] Melhorar extração de rubrica/critério/escala para outros formatos reais do Moodle.
- [ ] Melhorar análise de baixa confiança.
- [ ] Adicionar observações internas ao professor.
- [ ] Exportar relatório consolidado para coordenação.

### P2 — escala e operação

- [ ] Implementar fila real com workers.
- [ ] Cachear materiais comuns por atividade/seção.
- [ ] Adicionar retry/backoff/circuit breaker.
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
- [x] Sistema bloqueia commit com prévia obsoleta.
- [x] Sistema bloqueia commit quando a tentativa/submissão mudou.
- [ ] Envio validado em sandbox/homologação.
- [ ] Piloto pedagógico aprovado.

### 11.3 Escala

- [ ] Lote com 400 entregas não trava o chat.
- [ ] Processamento ocorre por fila/workers reais.
- [ ] Materiais comuns são cacheados.
- [ ] Professor/tutor consegue revisar por páginas/filtros.
- [ ] Falhas parciais não bloqueiam todo o lote.
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
