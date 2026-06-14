# TODO.md — Correção Assistida SENAI para AVA/Moodle via GPT Apps

**Status atual:** MVP funcional avançado em validação técnica.  
**Data-base atualizada:** 2026-06-14  
**Repositório:** `moodle-conector`  
**Objetivo:** transformar o GPT App/Conector do AVA em uma ferramenta de consulta, análise, rascunho, revisão e escrita assistida para apoiar professores/tutores na correção de atividades no Moodle, mantendo decisão humana, rastreabilidade, segurança institucional e confirmação explícita antes de qualquer escrita oficial.

---

## 0. Resumo executivo do estado atual

A implementação já avançou além da proposta inicial. O núcleo de correção assistida está implementado em C#/.NET dentro da arquitetura atual do projeto.

O fluxo já cobre:

1. descoberta técnica das funções Moodle de correção;
2. listagem de entregas corrigíveis;
3. criação de lote interno de correção assistida;
4. download e extração de anexos da submissão;
5. varredura de materiais da seção da tarefa;
6. persistência de artefatos `submission_file` e `assignment_context`;
7. seleção heurística do provável enunciado/material de contexto;
8. montagem de `GradingContext`;
9. geração de rascunho preliminar;
10. sugestão de nota quando há critérios/valor máximo suficientes;
11. persistência de evidências/lacunas por critério;
12. consulta de status do lote;
13. consulta de detalhe do item;
14. revisão humana do rascunho;
15. prévia de lançamento;
16. confirmação literal;
17. escrita controlada individual no Moodle via `mod_assign_save_grade`;
18. auditoria de criação, revisão e commit Moodle.

Ainda **não** está pronto para produção plena em escala 300–400 sem validação adicional. O estado correto é: **MVP avançado, pronto para testes controlados/sandbox e piloto pequeno, não para uso massivo sem governança**.

---

## 1. Bloqueio imediato encontrado

### 1.1 Nova tool de diagnóstico de contexto

Foi criada a nova tool:

```text
consultar_contexto_item_correcao_assistida
```

Arquivos já adicionados:

```text
src/MoodleConnector.Application/Grading/AssistedGradingContextDiagnosticsQueries.cs
src/MoodleConnector.Presentation/Tools/Grading/MoodleGradingContextDiagnosticsTools.cs
tests/MoodleConnector.Application.Tests/Grading/AssistedGradingContextDiagnosticsQueryHandlerTests.cs
```

Ela retorna diagnóstico sanitizado de `assignment_context`, incluindo:

- `assignmentContextArtifactsCount`
- `assignmentContextExtractedArtifactsCount`
- `selectedAssignmentStatementSource`
- `selectedCourseMaterials`
- `selectedContextArtifactId`
- `selectedContextModuleId`
- `selectedContextFileName`
- `selectedContextScore`
- `selectedContextConfidence`
- `selectedContextClassification`
- `selectedContextReason`
- `extractedContextChars`
- `extractedContextWords`
- lista sanitizada de `artifacts[]`, sem retornar conteúdo integral dos documentos.

### 1.2 Pendência crítica

A classe `MoodleGradingContextDiagnosticsTools` existe e está anotada com `[McpServerToolType]`, mas **ainda não está registrada no MCP Server**.

Registro atual no `Program.cs`:

```csharp
.WithTools<MoodleAssignmentSubmissionsTools>()
.WithTools<MoodleGradingTools>();
```

Correção necessária:

```csharp
.WithTools<MoodleAssignmentSubmissionsTools>()
.WithTools<MoodleGradingTools>()
.WithTools<MoodleGradingContextDiagnosticsTools>();
```

Depois disso, reexecutar build/testes e reescanear o `/mcp` no ChatGPT Developer Mode.

---

## 2. Decisões de arquitetura

- [x] Adotar o modelo **Correção Assistida**, não correção automática.
- [x] Implementar dentro do projeto atual em C#/.NET, sem servidor paralelo em TypeScript.
- [x] Separar consulta, análise, revisão, prévia, confirmação, escrita e auditoria.
- [x] Evitar tool única `corrigir_e_enviar`.
- [x] Manter escrita oficial no Moodle sempre dependente de prévia revisável e confirmação literal.
- [x] Usar lote interno para pedidos com muitas entregas.
- [ ] Evoluir processamento assíncrono real para escala 300–400 com fila/workers robustos.
- [ ] Definir política institucional de retenção e descarte de dados/arquivos.

---

## 3. Tools MCP — estado atual

### 3.1 Tools de correção assistida expostas em `MoodleGradingTools`

| Tool | Estado | Tipo | Observação |
|---|---|---|---|
| `descobrir_funcoes_moodle_correcao` | Implementada | leitura | Descoberta das funções Moodle de correção. |
| `discover_moodle_grading_functions` | Implementada | leitura | Alias em inglês. |
| `executar_descoberta_tecnica_correcao` | Implementada | leitura | Relatório técnico consolidado. |
| `listar_entregas_corrigiveis` | Implementada | leitura | Lista entregas que podem entrar em lote. |
| `criar_lote_correcao_assistida` | Implementada | criação interna | Cria lote, baixa anexos e coleta contexto. |
| `consultar_status_lote_correcao` | Implementada | leitura | Consulta lote e métricas. |
| `cancelar_lote_correcao_assistida` | Implementada | escrita interna | Cancela lote interno. |
| `consultar_item_correcao_assistida` | Implementada | leitura | Retorna detalhe, rascunho, evidências e pendências. |
| `atualizar_rascunho_correcao` | Implementada | escrita interna | Salva revisão humana. |
| `criar_previa_lancamento_lote` | Implementada | ação pendente | Cria prévia revisável, sem escrever no Moodle. |
| `confirmar_lancamento_lote_moodle` | Implementada | escrita Moodle | Escreve após confirmação literal. |
| `consultar_auditoria_correcao` | Implementada | leitura | Consulta auditoria por `auditId`. |
| `consultar_auditoria_correcao_lote` | Implementada | leitura | Consulta auditoria por `batchJobId`. |

### 3.2 Tool nova ainda não exposta

| Tool | Estado | Próxima ação |
|---|---|---|
| `consultar_contexto_item_correcao_assistida` | Implementada no código, não registrada no MCP | Adicionar `.WithTools<MoodleGradingContextDiagnosticsTools>()` no `Program.cs`. |

### 3.3 Metadados esperados

| Tool | ReadOnly | Destructive | Idempotent | OpenWorld |
|---|---:|---:|---:|---:|
| `listar_entregas_corrigiveis` | true | false | true | false |
| `criar_lote_correcao_assistida` | false | false | false | false |
| `consultar_status_lote_correcao` | true | false | true | false |
| `cancelar_lote_correcao_assistida` | false | false | true | false |
| `consultar_item_correcao_assistida` | true | false | true | false |
| `consultar_contexto_item_correcao_assistida` | true | false | true | false |
| `atualizar_rascunho_correcao` | false | false | true | false |
| `criar_previa_lancamento_lote` | false | false | false | false |
| `confirmar_lancamento_lote_moodle` | false | true | true | false |
| `consultar_auditoria_correcao` | true | false | true | false |
| `consultar_auditoria_correcao_lote` | true | false | true | false |

---

## 4. Componentes implementados

### 4.1 Domain

- [x] Entidades de lote de correção.
- [x] Entidades de item de correção.
- [x] Artefatos de correção (`GradingArtifact`).
- [x] Evidências por critério (`GradingEvidence`).
- [x] Status de lote, item, revisão e commit.

### 4.2 Application

- [x] Commands/queries para criação de lote.
- [x] Commands/queries para status do lote.
- [x] Commands/queries para detalhe do item.
- [x] Commands/queries para atualização do rascunho.
- [x] Commands/queries para prévia de lançamento.
- [x] Commands/queries para confirmação de lançamento.
- [x] Commands/queries para auditoria.
- [x] `GradingContext` e `GradingContextBuilder`.
- [x] `IAssignmentContextSelectionService`.
- [x] `HeuristicAssignmentContextSelectionService`.
- [x] `IGradingAnalysisService`.
- [x] `IGradingBatchOrchestrator`.
- [x] `IGradingReviewRepository`.
- [x] Diagnóstico de contexto via `GetAssistedGradingContextDiagnosticsQuery`.

### 4.3 Infrastructure

- [x] Persistência das tabelas `grading_*`.
- [x] Scripts SQL versionados.
- [x] Inicialização de schema via `ConnectorDbContextSchemaInitializer`.
- [x] Gateway de download de arquivo/submissão.
- [x] Gateway de escrita Moodle via `mod_assign_save_grade`.
- [x] Serviço de extração de TXT, HTML, JSON, XML, CSV e PDF com texto embutido.
- [ ] Extração DOCX, PPTX, XLSX, ODT.
- [ ] OCR para PDF escaneado/imagem.
- [ ] Fila/workers robustos para escala.

### 4.4 Presentation

- [x] `MoodleGradingTools` registrado em `Program.cs`.
- [x] Tools de correção assistida expostas em MCP.
- [x] Segurança OAuth/API key integrada ao `/mcp`.
- [x] Hints MCP aplicados nas tools principais.
- [ ] `MoodleGradingContextDiagnosticsTools` registrado em `Program.cs`.
- [ ] Reescaneamento do `/mcp` após registrar nova tool.

---

## 5. Estado por fase

### Fase 0 — Descoberta técnica

- [x] Criar `executar_descoberta_tecnica_correcao`.
- [x] Validar fluxo técnico inicial em Moodle real usando cursos de teste.
- [x] Confirmar leitura de submissões.
- [x] Confirmar criação de lote sem escrita oficial.
- [ ] Confirmar versão Moodle e capabilities reais por curso/atividade.
- [ ] Confirmar rubricas/escala via endpoint confiável.
- [ ] Confirmar política de retenção institucional.

### Fase 1 — MVP sem escrita no Moodle

- [x] Criar entidades/DTOs mínimos de correção.
- [x] Criar `criar_lote_correcao_assistida`.
- [x] Criar `consultar_status_lote_correcao`.
- [x] Criar `cancelar_lote_correcao_assistida`.
- [x] Criar `consultar_item_correcao_assistida`.
- [x] Baixar anexos retornados por `mod_assign_get_submissions` quando há `fileUrl`.
- [x] Extrair texto de formatos textuais e PDF com texto embutido.
- [x] Persistir `GradingArtifact.ExtractedTextRef`.
- [x] Escanear materiais da tarefa/seção.
- [x] Salvar artefatos `assignment_context`.
- [x] Selecionar heuristicamente provável enunciado/contexto.
- [x] Gerar parecer preliminar revisável quando há texto extraído.
- [x] Gerar nota sugerida quando há critérios e valor máximo extraídos.
- [x] Persistir e expor evidências/lacunas por critério.
- [x] Criar diagnóstico de contexto do item.
- [ ] Expor diagnóstico de contexto no MCP registrando `MoodleGradingContextDiagnosticsTools`.
- [ ] Validar qualidade pedagógica com tutores.

### Fase 2 — Rascunho revisável

- [x] Criar `atualizar_rascunho_correcao`.
- [x] Persistir rascunhos em tabelas `grading_*`.
- [x] Salvar edições do professor/tutor.
- [x] Separar nota sugerida de nota final.
- [x] Marcar item como revisado.
- [ ] Criar painel de revisão somente se o fluxo via chat/tools ficar ruim.

### Fase 3 — Escrita controlada no Moodle

- [x] Criar `criar_previa_lancamento_lote`.
- [x] Criar `confirmar_lancamento_lote_moodle`.
- [x] Reusar `PendingActionService` e `ActionConfirmationService`.
- [x] Integrar com `mod_assign_save_grade`.
- [x] Implementar idempotência por `pendingActionId` e status do item.
- [x] Expandir auditoria para commit Moodle.
- [x] Criar `consultar_auditoria_correcao` por `auditId`.
- [x] Criar `consultar_auditoria_correcao_lote` por `batchJobId`.
- [ ] Testar envio em sandbox/homologação.
- [ ] Fazer piloto com turma pequena antes de habilitar produção.

### Fase 4 — Escala 300–400

- [x] Integrar `IGradingBatchOrchestrator.EnqueueAsync` no fluxo.
- [x] Implementar orquestrador local MVP para enfileirar/cancelar/consultar status.
- [x] Criar `GradingContextBuilder` usando `ExtractedTextRef` persistido.
- [ ] Ativar fila/workers reais para alto volume.
- [ ] Cachear materiais comuns da atividade para não reprocessar o mesmo SAP 300–400 vezes.
- [ ] Processar 400 entregas com paginação e retomada segura.
- [ ] Implementar falhas recuperáveis e retry/backoff.
- [ ] Adicionar métricas de tempo, custo, falha e carga Moodle.
- [ ] Testes de carga com 25, 100 e 400 entregas.

### Fase 5 — Governança e publicação

- [ ] Revisar descrições finais das tools.
- [ ] Revisar escopos OAuth e RBAC.
- [ ] Reescanear `/mcp` no ChatGPT Developer Mode.
- [ ] Publicar como app interno.
- [ ] Criar manual de uso para tutores/professores.
- [ ] Criar política de responsabilidade docente.
- [ ] Validar fluxo com coordenação/gestão.

---

## 6. Testes e validações já realizados

### 6.1 Teste funcional — curso 29972

Resultado observado:

- [x] Seção da tarefa mapeada.
- [x] `SAP 01.pdf` localizado imediatamente antes da tarefa `Envio SAP 01 - Etapa 1`.
- [x] Lote criado com `includeRubric=true` e `includeCourseMaterials=true`.
- [x] Submissão com arquivo processada.
- [x] Item ficou `DraftReady`.
- [x] Sem `Blocked`.
- [x] Sem `Failed`.
- [x] `draftFeedback` gerado.
- [ ] Antes da nova tool, não havia confirmação direta se o `SAP 01.pdf` foi efetivamente escolhido como `assignment_context`.
- [ ] Retestar após registrar `consultar_contexto_item_correcao_assistida`.

### 6.2 Teste funcional — curso 32787

Resultado observado:

- [x] Materiais `SAP 01.pdf` e `SAP 02.pdf` encontrados na seção correta.
- [x] Lote criado com materiais de curso.
- [x] Itens sem conteúdo legível ficaram bloqueados.
- [x] Fluxo principal não quebrou.

### 6.3 Testes automatizados

- [x] Testes para criação de lote.
- [x] Testes para download/extração de anexo.
- [x] Testes para persistência de `assignment_context`.
- [x] Testes para seleção heurística de contexto.
- [x] Testes para extração de critérios e nota máxima.
- [x] Testes para status de lote.
- [x] Testes para detalhe de item.
- [x] Testes para evidências por critério.
- [x] Testes para revisão humana.
- [x] Testes para prévia/confirmacão de lançamento.
- [x] Testes para auditoria.
- [x] Testes para diagnóstico de contexto.
- [ ] Rodar novamente `dotnet build` após registrar a nova tool no MCP.
- [ ] Rodar novamente `dotnet test` após registrar a nova tool no MCP.

---

## 7. Lacunas técnicas atuais

### P0 — Corrigir antes do próximo teste no GPT App

- [ ] Registrar `MoodleGradingContextDiagnosticsTools` no `Program.cs`.
- [ ] Rodar `dotnet build`.
- [ ] Rodar `dotnet test`.
- [ ] Reescanear `/mcp` no ChatGPT Developer Mode.
- [ ] Retestar `consultar_contexto_item_correcao_assistida` no curso `29972`.
- [ ] Verificar se retorna `selectedContextFileName = SAP 01.pdf`.
- [ ] Verificar `selectedContextScore` e `selectedContextConfidence`.
- [ ] Verificar `assignmentContextArtifactsCount` e `assignmentContextExtractedArtifactsCount`.

### P1 — Importante para confiabilidade pedagógica

- [ ] Validar rubrica real do Moodle quando disponível.
- [ ] Melhorar extração de critérios e escala.
- [ ] Tratar nota por escala/conceito, não apenas decimal.
- [ ] Melhorar bloqueios por ausência de critérios.
- [ ] Melhorar bloqueios por arquivo ilegível.
- [ ] Adicionar observações internas ao professor/tutor.
- [ ] Validar feedbacks com tutores antes de uso amplo.

### P2 — Escala e operação

- [ ] Implementar fila real com workers.
- [ ] Cachear materiais comuns por atividade/seção.
- [ ] Adicionar retry/backoff/circuit breaker para Moodle.
- [ ] Adicionar métricas operacionais.
- [ ] Criar exportação de relatório para coordenação.
- [ ] Criar painel UI opcional para revisão em lote.

---

## 8. Segurança, privacidade e governança

### Já implementado

- [x] OAuth/OpenIddict integrado ao GPT App.
- [x] Audience `/mcp` configurada.
- [x] API key/JWT protegendo chamadas MCP conforme configuração.
- [x] `moodle.write` exigido para commit Moodle.
- [x] `ConnectorClients.CanWrite = true` validado antes de escrita.
- [x] Feature flags de escrita validadas.
- [x] Confirmação literal obrigatória.
- [x] Auditoria de commit Moodle.
- [x] Idempotência por ação pendente/status de item.
- [x] Não lançar nota sem prévia revisável.

### Pendências

- [ ] Validar capability real `mod/assign:grade` por curso/atividade quando houver endpoint confiável.
- [ ] Validar se o professor/tutor tem permissão real sobre a turma.
- [ ] Separar escopos OAuth por domínio em fase posterior.
- [ ] Definir política de retenção de arquivos brutos.
- [ ] Redigir PII em logs técnicos.
- [ ] Separar logs técnicos de dados pedagógicos.
- [ ] Criar política institucional de responsabilidade docente.

---

## 9. Critérios de aceite atualizados

### 9.1 MVP de rascunho

- [x] Professor/tutor consegue criar lote.
- [x] Sistema baixa anexos de submissões quando há `fileUrl`.
- [x] Sistema extrai texto dos formatos suportados.
- [x] Sistema escaneia materiais da seção da tarefa.
- [x] Sistema gera feedback preliminar quando há conteúdo legível.
- [x] Sistema sugere nota quando há critérios e valor máximo suficientes.
- [x] Sistema informa limitações/bloqueios.
- [x] Sistema não lança nota sem prévia e confirmação.
- [ ] Sistema expõe diagnóstico direto de contexto no MCP.

### 9.2 Escrita controlada

- [x] Professor/tutor revisa nota e feedback antes do envio.
- [x] Sistema exibe prévia completa.
- [x] Sistema exige confirmação literal.
- [x] Sistema executa commit individual via `mod_assign_save_grade`.
- [x] Sistema registra auditoria de commit Moodle.
- [x] Sistema evita duplicidade com idempotência.
- [ ] Envio validado em sandbox/homologação.
- [ ] Piloto pedagógico aprovado.

### 9.3 Escala

- [ ] Lote com 400 entregas não trava o chat.
- [ ] Processamento ocorre por fila/workers reais.
- [ ] Materiais comuns são cacheados.
- [ ] Professor/tutor consegue revisar por páginas/filtros.
- [ ] Falhas parciais não bloqueiam todo o lote.
- [x] Lançamento parcial é auditável no commit Moodle.
- [ ] Sistema respeita limites de Moodle, arquivos e IA.

---

## 10. Backlog priorizado

### P0 — Próximo commit recomendado

1. Registrar `MoodleGradingContextDiagnosticsTools` no `Program.cs`.
2. Atualizar/expandir `McpToolMetadataTests` para cobrir `consultar_contexto_item_correcao_assistida`.
3. Rodar `dotnet build`.
4. Rodar `dotnet test`.
5. Reescanear tools no ChatGPT Developer Mode.
6. Retestar curso `29972`:
   - confirmar existência de `assignment_context`;
   - confirmar se `SAP 01.pdf` foi selecionado;
   - validar `selectedContextFileName`;
   - validar `selectedContextScore`;
   - validar `selectedContextConfidence`;
   - validar caracteres/palavras extraídos;
   - validar artefatos de suporte.

### P1 — Robustez

1. Melhorar extração de rubrica/critério/escala.
2. Validar rubricas reais do Moodle.
3. Implementar DOCX/PPTX/XLSX/ODT.
4. Implementar OCR ou bloqueio claro para PDFs escaneados.
5. Melhorar classificação de bloqueios.
6. Criar relatório consolidado de lote para coordenação.

### P2 — Produção e escala

1. Fila real com workers e concorrência configurável.
2. Cache de materiais por atividade.
3. Métricas e dashboard operacional.
4. Testes de carga 25/100/400.
5. Painel UI opcional no ChatGPT.
6. Manual de uso e governança institucional.

---

## 11. Definition of Done técnico atualizado

- [x] Tools principais de correção registradas no MCP Server.
- [x] `Program.cs` registra `MoodleGradingTools`.
- [ ] `Program.cs` registra `MoodleGradingContextDiagnosticsTools`.
- [x] Tools principais implementadas em C# com `[McpServerTool]`.
- [x] Schemas estruturados usando `ToolResponse<T>`.
- [x] Scripts SQL versionados adicionados.
- [x] `ConnectorDbContextSchemaInitializer` executa scripts.
- [x] Serviços principais registrados em DI.
- [x] Autorização server-side valida `moodle.write`, `CanWrite` e feature flag para escrita.
- [ ] Autorização server-side valida permissão Moodle real no curso/atividade.
- [x] Prévia de lançamento implementada.
- [x] Confirmação literal implementada.
- [x] Auditoria de commit Moodle implementada.
- [x] Testes automatizados cobrem o núcleo do fluxo.
- [ ] `dotnet build` executado após última alteração de diagnóstico.
- [ ] `dotnet test` executado após última alteração de diagnóstico.
- [ ] Endpoint `/mcp` reescaneado no ChatGPT Developer Mode após mudança de tool metadata.
- [ ] Envio ao Moodle testado em sandbox.
- [ ] Testes de carga executados.
- [ ] Documentação de operação criada.
- [ ] Piloto pedagógico aprovado.

---

## 12. Observações finais

- O primeiro deploy operacional deve continuar como **leitura + rascunho**, sem escrita ampla.
- A escrita no Moodle deve ser usada apenas após sandbox, piloto pequeno e autorização institucional.
- O modelo de escala para 300–400 atividades precisa de fila/workers, cache de materiais e métricas.
- A decisão final de nota e feedback permanece com o professor/tutor.
- O próximo passo técnico é pequeno e objetivo: registrar a nova tool de diagnóstico no MCP e retestar o curso `29972`.
