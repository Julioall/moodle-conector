# Auditoria técnica — Moodle Conector

**Data da verificação:** 26/07/2026  
**Escopo:** código da branch `main`, testes locais e configuração versionada. Nenhuma operação de escrita foi feita em Moodle real.  
**Versão analisada:** `faf175dd4f47437b2a025508e88ab9a0f6127401` (`faf175d`).

> **Atualização de correção (26/07/2026):** após a auditoria, foram aplicadas e testadas a reivindicação atômica de confirmação, a prevenção de reexecução nos fluxos de escrita, a invalidação do catálogo por troca de credencial, o contexto central de usuário Moodle, o registro das dez tools ausentes, o contrato de erro seguro das tools universais e status/health/deploy rastreáveis. A decisão de manter as flags de escrita habilitadas no `appsettings.json` foi preservada por solicitação explícita do responsável. As constatações abaixo registram o estado original da auditoria.

## Resumo executivo

O conector possui um núcleo universal funcional para chamadas de leitura, descoberta de capacidades por conexão e uma primeira camada de escrita com *pending actions*. O commit de capacidades está efetivamente em `main`, a árvore rastreada está limpa, `origin/main` aponta para o mesmo SHA e a solução compila sem avisos; a suíte passou com 500 testes.

Os principais avanços confirmados são o transporte REST centralizado, serialização de parâmetros Moodle, catálogo de funções por conexão, executor universal exposto por MCP, política que bloqueia funções desconhecidas/destrutivas e o registro declarativo de cinco fluxos de cursos/entregas.

Ainda não é seguro afirmar que a escrita esteja pronta para produção. A configuração versionada habilita escrita por padrão e a confirmação genérica não é atômica: duas confirmações concorrentes podem executar a mesma escrita remota duas vezes. Há, inclusive, uma implementação de correção para esse ponto em uma *worktree* ignorada, mas ela não pertence à `main` analisada. Também faltam invalidação robusta de capacidades, contexto de sessão central, migração dos demais gateways para fluxos orientados por capacidade, contrato estável de erros e rastreabilidade do artefato implantado.

**Conclusão objetiva:** o núcleo de leitura independente de versão está em condição **parcial e testada**; a promoção de escrita para produção deve ser bloqueada até resolver os dois P0 abaixo e executar os testes concorrentes correspondentes.

## Identificação e evidências da versão

| Item | Evidência / resultado observado |
|---|---|
| Branch local | `main` |
| Commit | `faf175dd4f47437b2a025508e88ab9a0f6127401` — `Merge pull request #6 from Julioall/agent/capability-driven-moodle-api` |
| Remoto | `origin` = `https://github.com/Julioall/moodle-conector.git`; `origin/main` no mesmo SHA; divergência `0 0` |
| Estado local | `git status --short` sem alterações rastreadas antes deste relatório |
| Histórico relevante | `b409da5 feat: add capability-driven Moodle API`; merge atual altera 59 arquivos, +2.839/-755 linhas |
| Arquivos ignorados | artefatos `bin/obj` e `.worktrees/p0-write-flags/` |
| Implementação fora da branch | a worktree ignorada está em `agent/p0-write-flags`, SHA `38f4512`, não é ancestral de `main`; contém as correções de confirmação atômica (`34cc0d9`, `5cf4b94`, `38f4512`) |
| Deploy versionado | `.github/workflows/deploy-vps.yml` faz deploy em *push* de `main`, após testes, via Docker Compose |
| Deploy efetivamente instalado | **não verificável** sem acesso à VPS. `/api/status` não expõe SHA, data de build ou versão, portanto o host pode divergir do repositório |

Comandos executados: `git status --short`, `git branch --show-current`, `git log -10 --oneline --decorate`, `git remote -v`, `git rev-parse HEAD`, `git show --stat --oneline HEAD`, `git ls-remote origin refs/heads/main` e `git rev-list --left-right --count HEAD...origin/main`.

## Evidências técnicas por componente

| Componente | Status | Arquivo, símbolo e linhas aprox. | Evidência |
|---|---|---|---|
| Cliente REST universal | IMPLEMENTADO | `src/MoodleConnector.Infrastructure/MoodleApi/MoodleRestClient.cs`, `ExecuteAsync`, 24–74 | único uso produtivo de `/webservice/rest/server.php`; `POST`, `FormUrlEncodedContent`, `wsfunction` e `moodlewsrestformat` centralizados |
| Token fora da URL | IMPLEMENTADO | `MoodleRestClient.cs`, 36–45; `MoodleRestClientTests.cs` | `wstoken` é campo do corpo. Teste confirma que não vai para query string |
| Resiliência/cancelamento | IMPLEMENTADO | `Infrastructure/DependencyInjection.cs`, 50–100 | `HttpClient` tipado e políticas Polly registradas centralmente; `CancellationToken` é encaminhado |
| Chamadas HTTP Moodle fora do REST | PARCIAL | `MoodleAccessTokenProvider.cs`, `MoodleCredentialValidator.cs`, `MoodleSubmissionFileGateway.cs`, `Reports/MoodleReportBuilderClient.cs`, `MoodleProxyGateway.cs` | login, download de arquivo, relatórios e proxy possuem HTTP próprio; não há outro chamador de `server.php` |
| Serializador | IMPLEMENTADO | `MoodleParameterSerializer.cs`, `Flatten`, 10–89 | suporta escalares, nulos omitidos, listas, dicionários, `JsonElement`, objetos aninhados e índices Moodle |
| Testes do serializador | PARCIAL | `MoodleParameterSerializerTests.cs`, 9–62 | cobrem bool/número, array, objeto aninhado e array JSON; faltam casos diretos de `users[0][id]`, reflexão de objeto, nulos e níveis mais profundos |
| Parser de resposta | IMPLEMENTADO | `MoodleResponseParser.cs`, `Parse`, 8–41 | identifica JSON inválido e exceção Moodle; não foi localizado teste unitário direto do parser |
| Mapeamento de erros | PARCIAL | `MoodleResponseParser.cs`; `MoodleApiException.cs`; `ToolResultHelper.cs` | códigos remotos são propagados, mas não há `MoodleApiErrorMapper` estável; handlers normalmente devolvem somente a mensagem e descartam código/etapa/auditId |
| Descoberta por conexão | IMPLEMENTADO | `MoodleFunctionCatalog.cs`, `GetProfileAsync`, 20–79 | usa `core_webservice_get_site_info`; chave de cache inclui `ConnectionId`, TTL 15 min, `forceRefresh` disponível |
| Isolamento entre conexões | IMPLEMENTADO | `MoodleFunctionCatalogTests.cs`, 11–28; teste de transporte de capacidades | perfis simulados independentes para duas conexões |
| Invalidação/descoberta parcial | PARCIAL | `MoodleFunctionCatalog.cs` | não invalida em troca de token, nem atualiza após `function_not_available`; ausência de lista de funções vira lista vazia sem estado explícito de descoberta parcial |
| Executor universal | IMPLEMENTADO | `MoodleFunctionExecutor.cs`, `ExecuteReadAsync`, 24–115; `MoodleUniversalTools.cs` | resolve conexão, consulta perfil, bloqueia indisponível/risco não-Read, chama REST e grava metadados de auditoria |
| Escrita universal | PARCIAL | `MoodleUniversalWriteService.cs`, 25–189; `MoodleUniversalWriteTools.cs` | preparação, hash, expiração de 15 min, confirmação literal, `CanWrite`, escopo e bloqueio de desconhecida/destrutiva; ver P0 de concorrência e flags |
| Risco de função | IMPLEMENTADO | `MoodleReadFunctionPolicy.cs`, 7–75 | allowlists explícitas Read/ControlledWrite; nomes destrutivos são bloqueados; funções desconhecidas não são leitura |
| Fluxos de negócio | PARCIAL | `MoodleBusinessFlowRegistry.cs`, 9–107 | cinco fluxos declarativos, com prioridade/fallback para cursos e entregas; demais domínios continuam em gateways especializados |
| Recursos/cursos | IMPLEMENTADO/PARCIAL | `MoodleResourceResolver.cs`; `MoodleCoursesGateway.cs` | reconhece id, URL de curso/categoria, `shortname`, `idnumber`, texto e `categoryid`; usa `core_course_get_courses_by_field` e fallback de cursos matriculados |
| Sessão Moodle central | PARCIAL | `MoodleCurrentUserIdGateway.cs`, 10–27; `MoodleCoursesGateway.cs`, ~254 | `core_webservice_get_site_info` ainda é chamado por gateways além do catálogo; não existe provedor único de contexto/cache de sessão |
| Auditoria | PARCIAL | `MoodleFunctionExecutor.cs`, 91–113; `MoodleUniversalWriteService.cs`; comandos de nota | executor universal audita metadados sem valores; fluxos especializados não têm contrato uniforme e erros de nota podem persistir `ex.Message` remoto |

Não foi encontrada lógica principal que selecione comportamento por `version.StartsWith("4.")` ou `version.StartsWith("5.")`. Release/versão são dados de diagnóstico do perfil, e não mecanismo de compatibilidade.

## Matriz de conformidade

| Requisito | Status | Evidência | Risco | Ação necessária |
|---|---|---|---|---|
| Cliente REST universal | CONCLUÍDO | `MoodleRestClient` é o único uso de `server.php` | baixo | manter teste de transporte |
| POST/token fora da URL | CONCLUÍDO | corpo form-urlencoded e teste dedicado | baixo | manter redaction em logs futuros |
| Serializador universal | CONCLUÍDO | `MoodleParameterSerializer` | baixo | ampliar casos de teste |
| Parser de resposta | PARCIAL | parser existe, sem suíte direta abrangente | médio | testar erro/JSON/códigos estáveis |
| Mapeamento interno de erros | PARCIAL | exceção Moodle bruta; `ToolResultHelper.Error` perde contexto | médio | criar mapper e resposta estruturada sanitizada |
| Contexto central da conexão | PARCIAL | catálogo por conexão, chamadas de site-info duplicadas | médio | criar `MoodleSessionContextProvider` |
| Descoberta geral | PARCIAL | catálogo por conexão e cache; invalidação incompleta | médio | renovar por token/erro e marcar descoberta parcial |
| Perfil de capacidades por conexão | CONCLUÍDO | cache por `ConnectionId`, testes com duas conexões | baixo | incluir métrica/diagnóstico de expiração |
| Executor universal de leitura | CONCLUÍDO | `moodle_execute_read` registrado e testado | baixo | devolver código/auditId ao chamador |
| Catálogo/bloqueio de risco | CONCLUÍDO | allowlist + unknown/destructive bloqueados | baixo | aumentar corpus de classificação |
| Registro de fluxos e fallback | PARCIAL | cinco fluxos declarados | médio | migrar participantes, conteúdos, tarefas, notas, mensagens e fórum |
| Gateway de cursos migrado | CONCLUÍDO | registry + resolver + testes de fallback | baixo | cobrir paginação/erros remotos reais |
| Demais gateways migrados | PARCIAL | gateways especializados usam REST, mas não registry | médio | registrar estratégias por domínio |
| Escrita com pending action | PARCIAL | fluxo completo existe, confirmação concorrente não é atômica | **P0** | aplicar *claim* atômico antes da chamada remota |
| Escrita desabilitada por padrão | NÃO CONFORME | `appsettings.json`, 36–43, habilita todas as flags | **P0** | defaults `false`, registro condicional e config de produção explícita |
| Tools por capacidade | PARCIAL | tools estáticas; disponibilidade apresentada via fluxo | médio | expor disponibilidade no `tools/list` ou catálogo gerado |
| Status/rastreio do deploy | PARCIAL | `/api/status` e `/health` em `Program.cs`, 557–580 | médio | incluir SHA/build/tools e readiness de DB |
| Documentação | PARCIAL | README/catálogo divergem das flags reais | médio | gerar catálogo/configuração a partir do código |
| Regressão | PARCIAL | 500 testes passam; cobertura global 62,68% | médio | adicionar concorrência, parser e falhas de catálogo |

## Inventário das tools MCP

O registro estático está em `src/MoodleConnector.Presentation/Program.cs`, 274–293. A contagem por atributos é **143** implementações: **127** registradas incondicionalmente, **4** de nota individual registradas porque `AssignmentGradeWriteEnabled=true` no `appsettings.json`, e **2** de demonstração desativadas. Logo, a configuração versionada deve expor **131 tools** no início do processo; a confirmação por `tools/list` foi feita somente pelo `WebApplicationFactory` em `McpJwtClaimsIntegrationTests.cs`, não por um processo local com banco real.

Legenda: `R` = leitura; `W` = escrita/persistência. Função Moodle e gateway variam dentro de cada classe, mas todos os acessos Moodle passam pelo gateway/serviço injetado da categoria; onde há confirmação, ela é indicada. O campo de auditoria é “universal” apenas quando há evidência em `MoodleFunctionExecutor`/`MoodleUniversalWriteService`; nos demais não foi verificado contrato uniforme por tool. Os testes são de classe/integração de categoria quando existentes, não uma prova individual de cada alias.

| Classe registrada | Categoria / modo / confirmação / auditoria / teste | Tools expostas |
|---|---|---|
| `MoodleCoursesTools` | cursos; R; não; não uniforme; testes de cursos/capacidades | `list_my_courses`, `list_courses`, `search_courses`, `search_courses`, `search`, `get_course`, `get_course`, `fetch` |
| `MoodleUniversalTools` | universal; R; não; sim; executor/catálogo testados | `moodle_diagnose_connection`, `moodle_list_functions`, `moodle_check_function`, `moodle_describe_function`, `moodle_list_available_flows`, `moodle_execute_read` |
| `MoodleUniversalWriteTools` | universal; preparar é marcado R embora persista pending action; confirmar W; sim; sim; testes de escrita | `moodle_prepare_write`, `moodle_confirm_write` |
| `MoodleParticipantsTools` | participantes; R; não; não uniforme; testes de categoria | `list_course_participants`, `list_course_participants`, `list_course_students`, `list_course_students`, `list_course_groups`, `list_course_groups`, `get_group_members`, `get_group_members` |
| `MoodleCourseContentsTools` | conteúdos; R; não; não uniforme; testes de categoria | `list_course_contents`, `list_course_contents`, `get_course_module`, `get_course_module`, `list_course_resources`, `list_course_resources`, `list_course_files`, `list_course_files`, `list_course_pages`, `list_course_pages`, `list_course_urls`, `list_course_urls`, `audit_course_structure`, `audit_course_structure` |
| `MoodleCourseActivitiesTools` | atividades; R; não; não uniforme; testes de categoria | `list_course_activities`, `list_course_activities`, `get_course_activity`, `get_course_activity`, `list_course_assignments`, `list_course_assignments`, `get_assignment`, `get_assignment`, `list_course_quizzes`, `list_course_quizzes`, `get_quiz`, `get_quiz`, `list_course_scorms`, `list_course_scorms`, `list_activity_deadlines`, `list_activity_deadlines` |
| `MoodleForumTools` | fórum; R/W; sim para publicação; pendente/confirmado; testes específicos | `read_forum`, `read_forum`, `create_forum_post_preview`, `create_forum_post_preview`, `confirm_forum_post`, `confirm_forum_post` |
| `MoodleAssignmentSubmissionsTools` | entregas; R; não; não uniforme; testes de categoria | `list_assignment_submissions`, `list_assignment_submissions`, `get_student_submission`, `get_student_submission`, `list_pending_submissions`, `list_pending_submissions`, `list_late_submissions`, `list_late_submissions`, `list_submissions_awaiting_grading`, `list_submissions_awaiting_grading`, `get_submission_status`, `get_submission_status` |
| `MoodleGradingTools` | correção; R/W; parcial; lote/auditoria própria; testes de correção | `discover_grading_functions`, `discover_moodle_grading_functions`, `execute_grading_discovery`, `list_gradable_submissions`, `create_assisted_grading_batch`, `get_grading_batch_status`, `export_grading_coordination_report`, `cancel_assisted_grading_batch`, `get_assisted_grading_item`, `update_grading_draft`, `update_grading_drafts_batch`, `create_batch_grade_launch_preview`, `confirm_batch_grade_launch`, `get_grading_audit`, `get_grading_batch_audit`, `prepare_ai_grading_batch`, `save_ai_grading_batch`, `prepare_submission_grading` |
| `MoodleIndividualGradeTools` | nota individual; W; sim; parcial; testes de comandos, sem concorrência de banco | `prepare_individual_grade_launch`, `prepare_individual_grade_launch`, `confirm_individual_grade_launch`, `confirm_individual_grade_launch` |
| `MoodleGradebookTools` | boletim; R; não; não uniforme; testes de categoria | `get_student_gradebook`, `get_student_gradebook` |
| `MoodleCompletionTools` | progresso; R; não; não uniforme; testes de categoria | `get_student_completion`, `get_student_completion` |
| `MoodleRiskAnalysisTools` | risco; R; não; não uniforme; testes de categoria | `report_students_at_risk`, `report_students_at_risk` |
| `MoodleGradingContextDiagnosticsTools` | diagnóstico de correção; R; não; consulta de auditoria; testes de correção | `get_grading_item_context` |
| `MoodleGradingReviewAppTools` | revisão; R; não; consulta de lote; testes de correção | `review_batch_feedbacks` |
| `MoodleTutorMessageTools` | mensagens; preparar R, confirmar W; sim; pending/auditoria; testes de mensagens | `prepare_welcome_message`, `confirm_welcome_message`, `prepare_access_reminder`, `confirm_access_reminder`, `prepare_activity_reminder`, `confirm_activity_reminder`, `prepare_recovery_message`, `confirm_recovery_message`, `prepare_closing_message`, `confirm_closing_message`, `prepare_followup_message`, `confirm_followup_message` |
| `MoodleReportTools` | relatórios; R; não; não uniforme; testes de categoria | `generate_weekly_performance_report`, `generate_weekly_performance_report`, `generate_class_council_report`, `generate_class_council_report`, `generate_course_summary`, `generate_full_post_execution_report` |
| `MoodleMonitorTools` | monitoria; R; não; não uniforme; testes de categoria | `audit_virtual_classroom_checklist`, `audit_virtual_classroom_checklist`, `generate_monitor_class_report`, `generate_monitor_class_report` |
| `MoodleMemoryTools` | memória local; W; não; não se aplica a Moodle; testes de memória | `manage_user_memory` |
| `MoodleMemoryDocumentTools` | documentos de memória local; R/W; não; não se aplica a Moodle; testes de memória | `gerenciar_documento_memoria_usuario`, `save_user_memory_document`, `list_user_memory_documents`, `read_user_memory_document`, `remove_user_memory_document` |
| `MoodlePedagogyTools` | orientação pedagógica; R; não; não se aplica diretamente a Moodle; testes de categoria | `get_pedagogical_guidelines` |

**Implementadas, mas não registradas:** `MoodleForumParticipationTools` (`list_students_without_forum_participation`, `list_students_without_forum_participation`), `MoodleAccessMonitoringTools` (`list_students_without_recent_access`, `list_students_without_recent_access`), `MoodleStudentPerformanceTools` (`get_student_activity_grades`, `get_student_activity_grades`, `list_students_below_min_grade`, `list_students_below_min_grade`) e `MoodlePendingSubmissionsTools` (`list_students_with_pending_submissions`, `list_students_with_pending_submissions`).

**Demonstração não ativa:** `DemoPendingActionTools` (`prepare_demo_action`, `confirm_demo_action`) só é registrada se `DemoToolsEnabled=true`, hoje `false`.

Há aliases em português/inglês deliberados. A contagem e o catálogo não são gerados automaticamente; isso é fonte de divergência futura.

## Segurança de escrita

O desenho do fluxo universal é adequado: criação de ação pendente, hash imutável, texto literal de confirmação, expiração, validação de dono/conexão/escopo (`moodle.write`), `CanWrite` e bloqueio de função desconhecida ou destrutiva. A confirmação genérica em `ActionConfirmationService.cs`, 24–94, porém, lê uma ação pendente, altera o estado em memória e só então persiste. `PendingMoodleActionRepository.cs`, 14–21, não faz atualização condicional nem *claim* transacional. Chamadores como `IndividualGradeCommands.cs`, 234–267, executam a escrita remota após essa confirmação.

Em duas requisições simultâneas, ambas podem observar “pendente” e chamar Moodle. Não existe teste de concorrência com banco que prove “uma única execução”. A worktree ignorada mencionada acima possui commits destinados a corrigir exatamente isso, mas não faz parte de `main`.

Além disso, `src/MoodleConnector.Presentation/appsettings.json`, 36–43, define `MessagesWriteEnabled`, `ScheduledMessagesEnabled`, `AssignmentFeedbackWriteEnabled`, `AssignmentGradeWriteEnabled`, `UniversalMoodleWriteEnabled` e `CourseContentWriteEnabled` como `true`. Isso contradiz README e `docs/technical/mcp-tools-catalog.md`, que afirmam defaults seguros/desabilitados. O `MoodleUniversalWriteTools.moodle_prepare_write` está também marcado `ReadOnly=true` embora crie estado persistente.

## Problemas encontrados

### P0 — segurança ou perda de dados

1. **Confirmação não atômica pode duplicar escrita Moodle.** Afeta notas e outros fluxos que confirmam antes de chamar o endpoint remoto. Aplicar *compare-and-set* no banco (por exemplo, `UPDATE ... WHERE status = Pending`), recuperar o resultado do *claim*, gravar auditoria no mesmo limite transacional e executar a chamada externa somente para o vencedor.
2. **Flags de escrita habilitadas na configuração versionada.** A aplicação não inicia em modo seguro. Alterar todos os defaults para `false`; manter habilitação somente por variável/segredo de ambiente de produção explicitamente aprovado e ocultar tools de escrita quando a flag estiver desabilitada.

### P1 — fluxo principal/operacional

1. **Artefato implantado não é identificável.** Workflow aponta para `main`, mas `/api/status` não contém SHA, versão, data de build, número de tools ou flags seguras; `/health` é apenas liveness. Não foi possível verificar a VPS.
2. **Falhas Moodle não têm contrato MCP estável.** Mensagens remotas podem ser repassadas e `ToolResultHelper` perde código, etapa, conexão, retry e auditId. Isso prejudica clientes e pode expor detalhe remoto indevido.

### P2 — limitação importante

1. Catálogo de capacidades não invalida por alteração de token nem após função indisponível e não registra descoberta parcial.
2. Não há `MoodleSessionContextProvider` central; `core_webservice_get_site_info` é repetido fora do catálogo.
3. Apenas cinco fluxos usam `MoodleBusinessFlowRegistry`; participantes, conteúdos, tarefas, boletim, mensagens, fórum e notas não declaram alternativas por capacidade.
4. Dez tools implementadas não são registradas. A disponibilidade é estática no início do processo, não por conexão.
5. README e catálogo técnico afirmam defaults de escrita que o `appsettings.json` contradiz.
6. A dependência transitiva de testes `System.Security.Cryptography.Xml 10.0.7` foi reportada com vulnerabilidades altas. Não foi reportada no runtime de produção pelo comando executado.

### P3 — melhoria técnica

1. Cobrir diretamente parser, nulos/dicionários/objetos multi-nível do serializador, expiração/refresh do catálogo e sanitização de logs.
2. Atualizar dependências diretas listadas por `dotnet list package --outdated` (MediatR, Microsoft.Extensions/EF, Npgsql, OpenIddict, MCP e pacotes de teste), após validação de compatibilidade.
3. Gerar catálogo de tools, flags e status de build a partir dos registros para evitar documentação manual divergente.
4. Tornar `moodle_prepare_write` semanticamente de escrita/persistência no metadado MCP.

## Plano de correção

| Prioridade | Arquivos afetados | Mudança necessária | Testes necessários | Dependências / risco | Critério de aceite |
|---|---|---|---|---|---|
| P0 | `ActionConfirmationService`, repositório/EF, comandos de nota/mensagem e migração | *Claim* atômico por ação e registro de auditoria transacional; só vencedor chama Moodle | integração com banco e duas confirmações concorrentes; mock deve receber uma chamada | exige revisar estados e recuperação em falha remota | 100+ confirmações concorrentes resultam em uma execução remota e um audit coerente |
| P0 | `appsettings*.json`, `Program.cs`, docs/deploy | defaults de todas as escritas `false`; registro condicional das tools W; configuração de produção explícita | inicialização padrão, `tools/list` com flags off/on, negativa de escrita | pode remover tools de clientes existentes até habilitação consciente | instalação limpa não oferece escrita e não cria pending action |
| P1 | `Program.cs`, pipeline de build/deploy | incorporar SHA/data/versão no build; status seguro; health/readiness de DB e MCP | teste de `/api/status`, health de dependência e inspeção de imagem | exige variáveis de build na CI | SHA de `origin/main` é observável no host e readiness falha sem DB |
| P1 | parser/mapper, tools presentation | `MoodleApiErrorMapper`, mensagens sanitizadas e campos código/etapa/retry/auditId | matriz de erros Moodle/HTTP/timeout sem token/senha | contrato MCP precisa versão/documentação | cada erro conhecido tem código estável; segredo não aparece |
| P2 | catálogo/executor/contexto | invalidar por token/erro, refresh único, estado parcial, contexto por conexão | troca de token, função removida, duas conexões e expiração | cache pode aumentar chamadas na transição | catálogo se recupera sem reinício e não mistura conexões |
| P2 | gateways/registry | declarar estratégias para todos os fluxos listados no escopo | testes por estratégia e fallback por conexão | migração incremental de gateways | ausência de função retorna `flow_unavailable` com funções faltantes |
| P2 | `Program.cs`, classes não registradas, docs | decidir registrar/remover as 10 tools e gerar inventário | teste completo de `tools/list` com contagem/schema | pode ampliar superfície MCP | lista publicada é idêntica ao registro esperado |
| P3 | testes e pacotes | ampliar cobertura e atualizar pacotes | restore/build/test/vulnerability scan | atualizações podem exigir ajustes | sem vulnerabilidades conhecidas no escopo de teste e regressão verde |

## Diferenças entre documentação e implementação

| Fonte | Declaração | Implementação observada |
|---|---|---|
| `README.md` e `docs/technical/mcp-tools-catalog.md` | escrita/universal desabilitadas por padrão | `appsettings.json` versionado habilita as seis flags de escrita relevantes |
| Catálogo técnico | lista é documentação operacional | há 10 tools implementadas sem registro no `Program.cs`; catálogo não é gerado |
| Workflow de deploy | deploy de `main` após testes | isso confirma a intenção da CI, não o SHA instalado na VPS |
| Testes MCP | valida aliases em `tools/list` | não valida inventário completo, contagem 131, flags de inicialização ou host real |
| Código de capacidades | compatibilidade por funções | confirmado para executor universal e cursos; ainda parcial nos demais gateways |

## Testes, build e dependências

| Verificação | Resultado |
|---|---|
| `dotnet restore MoodleConnector.slnx` | concluído |
| `dotnet build MoodleConnector.slnx --configuration Release --no-restore` | sucesso, 0 warnings, 0 errors |
| `dotnet test MoodleConnector.slnx --configuration Release --no-build` | 500 aprovados, 0 falhos, 0 ignorados |
| Cobertura Cobertura/XPlat | linhas 62,68% (13.025/20.778); branches 43,66% (3.721/8.521); Infrastructure 47,21%, Presentation 59,94%, Application 71,53%, Domain 91,52% |
| Vulnerabilidades | `System.Security.Cryptography.Xml 10.0.7` transitivo no projeto de testes, com avisos high; demais projetos não reportaram vulnerabilidade atual |
| Desatualizações | pacotes diretos em Application/Infrastructure/Presentation/Tests foram reportados pelo comando `--outdated`; requer atualização planejada |

O servidor não foi iniciado contra configuração real: `appsettings.json` contém host PostgreSQL placeholder e o startup aplica schemas fora do ambiente de testes. As integrações `WebApplicationFactory` e `MoodleCapabilitiesTransportIntegrationTests` exercitam MCP/HTTP com ambientes simulados, mas não substituem leitura contra Moodle real ou inspeção da VPS.

## Estimativas de maturidade

As percentagens abaixo são estimativas qualitativas baseadas em evidência de código registrado, execução da suíte e cobertura medida; não são métricas de produção.

| Área | Estimativa | Critério |
|---|---:|---|
| Conector acadêmico especializado | 75% | ampla superfície de leitura e testes, porém 10 tools não registradas e migração de capacidades incompleta |
| Núcleo universal da API Moodle | 70% | transporte, serializer, catálogo e executor prontos; mapper de erros e contexto central incompletos |
| Descoberta orientada por capacidades | 55% | por conexão/cache/force refresh e cursos com fallback, mas invalidação e adoção geral ausentes |
| Segurança de escrita | 35% | controles de design existem, mas dois P0 impedem confiança operacional |
| Documentação e rastreabilidade | 35% | documentação detalhada, porém flags divergentes e sem SHA no deploy |
| Cobertura de testes | 63% | cobertura de linha medida em 62,68%, com lacunas em infraestrutura/concorrência/erros |

## Comandos de validação reproduzíveis

```powershell
git status --short
git branch --show-current
git log -10 --oneline --decorate
git remote -v
git rev-parse HEAD
git show --stat --oneline HEAD
git ls-remote origin refs/heads/main
git rev-list --left-right --count HEAD...origin/main

rg -n '/webservice/rest/server\.php|core_webservice_get_site_info|McpServerTool|MessagesWriteEnabled|AssignmentGradeWriteEnabled' src tests

dotnet restore MoodleConnector.slnx
dotnet build MoodleConnector.slnx --configuration Release --no-restore
dotnet test MoodleConnector.slnx --configuration Release --no-build
dotnet test MoodleConnector.slnx --configuration Release --no-build --collect 'XPlat Code Coverage'
dotnet list MoodleConnector.slnx package --vulnerable --include-transitive
dotnet list MoodleConnector.slnx package --outdated
```

Para validar a implantação depois dos P0, consultar o endpoint autenticado/seguro de status no host e comparar seu `gitCommit` ao resultado de `git rev-parse HEAD`; não usar essa etapa para executar tools de escrita.
