# Roadmap por Domínios do Moodle Connector MCP

Este arquivo preserva o roadmap funcional de longo prazo. Ele não é uma lista de tarefas operacionais imediatas; para pendências prontas para execução, use `TODO.md`.

## Plano de implementação em fases

Este TODO reorganiza o roadmap do `MoodleConnector` por domínios funcionais do Moodle, mantendo a base técnica já construída: segurança, contrato MCP, `prepare_*` / `confirm_*`, auditoria, escopos, feature flags e hardening.

A ideia central é evoluir o conector na mesma ordem em que o usuário normalmente raciocina:

```text
Quais cursos eu tenho?
Quem está no curso?
O que existe na sala?
Quais conteúdos e recursos existem?
Quais atividades existem?
Quem entregou?
Como estão notas e avaliações?
Quem precisa de atenção?
Que relatório eu gero?
Que mensagem eu preparo?
Que feedback eu publico?
Que nota eu lanço?
```

---

## Diretriz central

- Tools somente leitura executam imediatamente.
- Tools com dados acadêmicos sensíveis devem retornar resposta estruturada padronizada e, quando fizer sentido, gerar auditoria.
- Tools de escrita nunca executam mudança direta na primeira chamada.
- Toda escrita deve preparar uma ação pendente, retornar prévia e exigir confirmação humana antes da execução.
- Nenhuma tool MCP de escrita deve chamar o Moodle diretamente sem passar por `PendingAction`.
- Toda confirmação deve validar:
  - usuário atual;
  - escopo;
  - vínculo Moodle;
  - expiração;
  - idempotência;
  - texto de confirmação;
  - feature flag;
  - status da ação pendente.

---

## Princípios de desenho das tools

### Nomeação

Preferir nomes por domínio e ação, sem codificar papéis como professor, tutor ou coordenação no nome da tool.

Exemplos bons:

```text
listar_meus_cursos
consultar_curso
listar_participantes_curso
listar_conteudos_curso
listar_atividades_curso
listar_entregas_atividade
consultar_notas_aluno
identificar_alunos_em_atencao
preparar_mensagem_curso
confirmar_mensagem_curso
```

Evitar:

```text
professor_listar_alunos
coordenador_relatorio_turma
tutor_enviar_mensagem
```

### Padrão de resposta

- Toda tool de leitura deve usar `ToolResponse<T>`.
- Toda escrita preparada deve retornar `PendingActionResponse`.
- Toda confirmação deve retornar `ActionConfirmationResponse`.
- Toda resposta MCP deve ter:
  - `Content`: narração curta, segura e útil para o ChatGPT.
  - `StructuredContent`: dados completos em JSON validável.
- Tools com retorno grande devem exigir:
  - paginação;
  - filtros;
  - `courseId`;
  - limite máximo;
  - opção de resumo.

### Classificação de risco

| Risco | Uso |
|---|---|
| `ReadOnly` | Consulta simples, sem dado sensível relevante. |
| `SensitiveRead` | Consulta de estudantes, notas, submissões, acessos ou risco. |
| `DraftOnly` | Gera rascunho, não publica nem envia. |
| `HumanConfirmedWrite` | Escrita com confirmação humana. |
| `CriticalHumanConfirmedWrite` | Escrita acadêmica crítica, como nota e ajuste avaliativo. |
| `AdminWrite` | Edição estrutural de curso, visibilidade, datas ou configuração. |

### Feature flags

```text
Features__MessagesWriteEnabled
Features__ScheduledMessagesEnabled
Features__AssignmentFeedbackWriteEnabled
Features__AssignmentGradeWriteEnabled
Features__CourseContentWriteEnabled
Features__AdminCourseWriteEnabled
```

Padrão recomendado em ambientes novos:

```text
MessagesWriteEnabled = false
ScheduledMessagesEnabled = false
AssignmentFeedbackWriteEnabled = false
AssignmentGradeWriteEnabled = false
CourseContentWriteEnabled = false
AdminCourseWriteEnabled = false
```

---

# Fase 0 — Base de segurança e contrato MCP

**Status:** concluída.

## Objetivo

Manter a fundação de segurança, contratos MCP, pending actions e auditoria para todas as próximas tools.

## Entregas

- [x] Manter `ToolRiskLevel` como classificação obrigatória para novas tools.
- [x] Manter `ToolResponse<T>` como envelope padrão para respostas de leitura.
- [x] Manter `PendingActionResponse` e `ActionConfirmationResponse` como contrato para ações de escrita.
- [x] Manter tabelas e repositórios de pending actions e audit logs.
- [x] Garantir que toda nova tool declare corretamente:
  - `ReadOnly`;
  - `Destructive`;
  - `Idempotent`;
  - `OpenWorld`;
  - `UseStructuredContent`;
  - `OutputSchemaType`.

## Definition of done

- [x] `dotnet test` passando.
- [x] Tool nova coberta por teste unitário ou integração MCP.
- [x] Structured content validado no teste.
- [x] Nenhuma escrita Moodle sem `prepare_*` / `confirm_*`.

## Evidências atuais

```text
src/MoodleConnector.Domain/ToolRiskLevel.cs
src/MoodleConnector.Application/Tools/ToolResponse.cs
src/MoodleConnector.Application/PendingActions/PendingActionService.cs
src/MoodleConnector.Application/PendingActions/ActionConfirmationService.cs
src/MoodleConnector.Infrastructure/Persistence/ConnectorDbContext.cs
tests/MoodleConnector.Application.Tests/PendingActions/PendingActionServicesTests.cs
```

---

# Fase 1 — Autenticação, escopos e identidade Moodle

**Status:** concluída.

## Objetivo

Consolidar autenticação JWT OAuth, API key, escopos por categoria de tool e vínculo entre usuário autenticado e usuário Moodle.

## Entregas

- [x] Revisar escopos OAuth atuais e alinhar com `MoodleScopePolicies`.
- [x] Definir mapeamento base entre claims OAuth, usuário local e usuário Moodle.
- [x] Persistir vínculo usuário OAuth → conta local → conexões Moodle.
- [x] Melhorar `IMoodleUserResolver` para resolver usuário Moodle por alias/conexão quando a claim `moodle_user_id` não existir.
- [x] Registrar auditoria de falhas de autorização relevantes.

## Definition of done

- [x] Login OAuth funciona no ChatGPT Connector.
- [x] API key continua funcionando para clientes existentes.
- [x] Testes cobrem:
  - token JWT;
  - API key;
  - ausência de escopo;
  - usuário sem vínculo Moodle.

## Evidências atuais

```text
src/MoodleConnector.Presentation/Security/MoodleScopePolicies.cs
src/MoodleConnector.Presentation/Program.cs
src/MoodleConnector.Infrastructure/Auth/CurrentUserContext.cs
src/MoodleConnector.Infrastructure/Auth/MoodleUserResolver.cs
src/MoodleConnector.Infrastructure/Auth/AuthorizationAuditService.cs
src/MoodleConnector.Infrastructure/MoodleCurrentUserIdGateway.cs
tests/MoodleConnector.Application.Tests/Infrastructure/MoodleUserResolverTests.cs
tests/MoodleConnector.Application.Tests/Integration/McpJwtClaimsIntegrationTests.cs
```

---

# Fase 2 — Domínio Cursos

**Status:** concluída no escopo de cursos básicos.

## Objetivo

Permitir localizar, listar e consultar contexto básico de cursos sem executar consultas pesadas.

A fase de cursos deve ser leve. Ela não deve calcular notas, pendências, tentativas, risco, conclusão ou prazos derivados complexos.

## Tools candidatas

```text
listar_meus_cursos
buscar_cursos
consultar_curso
```

Evolução futura do domínio:

```text
consultar_contexto_curso
consultar_calendario_curso
```

## Escopos

```text
moodle.read.courses
moodle.read.calendar
```

## Risco

```text
ReadOnly
```

Pode virar `SensitiveRead` quando a resposta incluir participação, acesso, grupos, notas ou dados individuais.

## Funções Moodle candidatas

```text
core_webservice_get_site_info
core_course_get_courses_by_field
core_course_search_courses
core_enrol_get_users_courses
core_calendar_get_calendar_events
core_calendar_get_action_events_by_course
```

## Estratégia de performance

- `listar_meus_cursos` deve ser leve e cacheável por usuário/conexão.
- Não calcular pendências ou indicadores derivados na listagem.
- Dados derivados devem ficar em tools específicas por curso.
- Usar cache curto para respostas repetidas.
- Retornar apenas campos essenciais:
  - `courseId`;
  - `shortName`;
  - `fullName`;
  - `displayName`;
  - `categoryId`;
  - `categoryName`;
  - `visible`, quando disponível;
  - datas básicas, quando disponíveis;
  - URL e imagem do curso, quando disponíveis;
  - progresso, favorito e último acesso, quando o Moodle retornar esses dados.

## Definition of done

- [x] Usuário consegue listar cursos vinculados.
- [x] Usuário consegue buscar curso por termo, `shortname`, `idnumber` ou `courseId`.
- [x] Usuário consegue consultar dados básicos de um curso.
- [x] `listar_meus_cursos` não consulta notas, entregas, conclusão ou risco.
- [x] Erros Moodle retornam resposta controlada.
- [x] Testes cobrem curso encontrado, usuário sem curso e erro Moodle.
- [x] Testes cobrem busca de curso e curso não encontrado.

---

# Fase 3 — Domínio Participantes

## Objetivo

Consultar estudantes, participantes, grupos e vínculo no curso com privacidade e paginação.

## Tools candidatas

```text
listar_participantes_curso
listar_alunos_curso
consultar_aluno_curso
listar_grupos_curso
consultar_membros_grupo
consultar_acessos_participantes
```

## Escopos

```text
moodle.read.students
moodle.read.groups
moodle.read.access
```

## Risco

```text
SensitiveRead
```

## Funções Moodle candidatas

```text
core_enrol_get_enrolled_users
core_enrol_get_users_courses
core_group_get_course_groups
core_group_get_group_members
```

## Regras

- Paginação obrigatória.
- Filtro por ativo/inativo, quando disponível.
- Evitar expor e-mail por padrão.
- Não exibir dados pessoais além do necessário.
- Para relatório coletivo, preferir agregados.
- Para estudante individual, exigir `courseId` e `studentId` ou identificador inequívoco.
- Logs e auditoria não devem persistir e-mails em payload integral quando não necessário.

## Definition of done

- [x] Usuário consegue listar participantes do curso.
- [x] Usuário consegue listar estudantes ativos.
- [x] Usuário consegue consultar grupos.
- [x] Usuário consegue consultar último acesso quando a API e permissões permitirem.
- [x] Dados pessoais são minimizados.
- [x] Testes cobrem paginação, filtro ativo/inativo, curso sem estudantes e usuário sem permissão.

---

# Fase 4 — Domínio Conteúdos e estrutura da sala

## Objetivo

Ler seções, tópicos, módulos, recursos, arquivos, páginas, URLs, livros, pastas e estrutura da sala Moodle.

Esta fase prepara a base para auditoria de sala e validação de materiais.

## Tools candidatas

```text
listar_conteudos_curso
consultar_modulo_curso
listar_recursos_curso
listar_arquivos_curso
listar_paginas_curso
listar_urls_curso
auditar_estrutura_curso
```

## Escopos

```text
moodle.read.contents
moodle.read.resources
```

## Risco

```text
ReadOnly
```

Pode virar `SensitiveRead` caso a resposta inclua URLs privadas, tokens, restrições por grupo ou dados de rastreamento individual.

## Funções Moodle candidatas

```text
core_course_get_contents
core_course_get_course_module
mod_resource_get_resources_by_courses
mod_page_get_pages_by_courses
mod_book_get_books_by_courses
mod_url_get_urls_by_courses
mod_folder_get_folders_by_courses
```

## Regras

- Não baixar arquivos grandes automaticamente.
- Não expor URLs privadas com token.
- Sanitizar links.
- Retornar estrutura por seções.
- Permitir filtro por tipo de módulo:
  - `resource`;
  - `page`;
  - `url`;
  - `book`;
  - `folder`;
  - `label`;
  - `assign`;
  - `quiz`;
  - `scorm`;
  - `forum`.
- `auditar_estrutura_curso` deve apontar achados, não alterar a sala.

## Definition of done

- [x] Usuário consegue ver a estrutura completa da sala.
- [x] Usuário consegue filtrar por tipo de recurso.
- [x] Usuário consegue identificar seções vazias.
- [x] Usuário consegue identificar recursos sem descrição ou com datas ausentes, quando disponível.
- [x] Nenhuma URL sensível é exposta indevidamente.
- [x] Testes cobrem curso com seções, curso vazio e módulos sem permissão.

---

# Fase 5 — Domínio Atividades

## Objetivo

Listar atividades do curso, prazos, configurações principais e status geral sem consultar submissões e notas em massa.

## Tools candidatas

```text
listar_atividades_curso
consultar_atividade
listar_tarefas_curso
consultar_tarefa
listar_quizzes_curso
consultar_quiz
listar_scorms_curso
consultar_prazos_atividades
consultar_linha_do_tempo_curso
listar_eventos_calendario_curso
listar_foruns_curso
```

## Escopos

```text
moodle.read.activities
moodle.read.assignments
moodle.read.quizzes
moodle.read.scorms
```

## Risco

```text
ReadOnly
```

Pode virar `SensitiveRead` se incluir status individual de estudante.

## Funções Moodle candidatas

```text
mod_assign_get_assignments
mod_quiz_get_quizzes_by_courses
mod_scorm_get_scorms_by_courses
core_course_get_contents
core_calendar_get_action_events_by_course
```

## Regras

- Não consultar submissões nesta fase.
- Não consultar notas nesta fase.
- Foco em inventário, datas, prazos e configurações.
- Sinalizar atividades sem prazo quando a API permitir identificar.
- Sinalizar atividades ocultas ou indisponíveis apenas quando essa informação estiver disponível e autorizada.

## Definition of done

- [x] Usuário consegue listar atividades de um curso.
- [x] Usuário consegue listar tarefas.
- [x] Usuário consegue listar quizzes.
- [x] Usuário consegue listar SCORMs.
- [x] Usuário consegue consultar prazos de abertura e fechamento.
- [x] Testes cobrem atividade sem data, curso sem atividades e erro de permissão.

---

# Fase 6 — Domínio Entregas e submissões

## Objetivo

Consultar entregas dos estudantes, pendências, atrasos, tentativas e itens aguardando correção.

## Tools candidatas

```text
listar_entregas_atividade
consultar_entrega_aluno
listar_entregas_pendentes
listar_entregas_atrasadas
listar_entregas_aguardando_correcao
consultar_status_submissao
consultar_tentativas_quiz
consultar_tentativas_scorm
```

## Escopos

```text
moodle.read.submissions
moodle.read.assignments
```

## Risco

```text
SensitiveRead
```

## Funções Moodle candidatas

```text
mod_assign_get_submissions
mod_assign_get_submission_status
mod_assign_get_grades
```

## Regras

- Exigir `courseId`.
- Exigir `assignmentId` para listagem por atividade.
- Paginação obrigatória.
- Filtros obrigatórios para relatórios grandes:
  - `status`;
  - `since`;
  - `before`;
  - `includeLate`;
  - `includeUngraded`.
- Não baixar anexos automaticamente.
- Não expor conteúdo integral da submissão sem solicitação específica e autorização.
- Em relatório coletivo, não expor texto de submissão individual.

## Definition of done

- [x] Usuário consegue ver quem entregou determinada tarefa.
- [x] Usuário consegue ver quem não entregou.
- [x] Usuário consegue ver entregas atrasadas.
- [x] Usuário consegue ver entregas aguardando correção.
- [x] Respostas grandes são paginadas.
- [x] Testes cobrem tarefa inexistente, estudante sem entrega, entrega enviada e entrega atrasada.

---

# Fase 7 — Domínio Avaliações e notas em leitura

## Objetivo

Consultar notas, itens avaliativos, distribuição de desempenho e pendências de correção sem executar escrita.

## Tools candidatas

```text
consultar_notas_aluno
consultar_resumo_avaliacoes
consultar_gradebook_curso
listar_itens_avaliativos
consultar_distribuicao_notas
listar_avaliacoes_sem_nota
listar_avaliacoes_pendentes_correcao
consultar_boletim_aluno
```

## Escopos

```text
moodle.read.grades
moodle.read.assessment
```

## Risco

```text
SensitiveRead
```

## Funções Moodle candidatas

```text
gradereport_user_get_grade_items
gradereport_user_get_grades_table
core_grades_get_grades
mod_assign_get_grades
```

## Regras

- Não decidir aprovação ou reprovação.
- Não expor nota individual em relatório coletivo sem necessidade.
- Agregados por padrão:
  - quantidade avaliada;
  - quantidade pendente;
  - média, quando permitido;
  - distribuição por faixas, quando configurada;
  - itens sem nota.
- Individual somente com `studentId` e finalidade clara.
- Não lançar nota nesta fase.

## Definition of done

- [ ] Usuário consegue consultar notas de um estudante.
- [ ] Usuário consegue consultar resumo de avaliações do curso.
- [ ] Usuário consegue consultar itens avaliativos.
- [ ] Usuário consegue identificar pendências de correção.
- [ ] Testes cobrem estudante sem nota, item sem nota, curso sem gradebook e ausência de permissão.

---

# Fase 8 — Domínio Progresso, conclusão e participação

## Objetivo

Consultar conclusão de atividades, progresso no curso, participação, acessos recentes e sinais de baixa participação.

## Tools candidatas

```text
consultar_conclusao_aluno
consultar_progresso_curso
listar_alunos_sem_acesso
listar_alunos_com_pendencias
buscar_participacao_alunos
consultar_participacao_foruns
consultar_progresso_aluno
consultar_acessos_aluno
consultar_discussao_forum
```

## Escopos

```text
moodle.read.completion
moodle.read.access
moodle.read.participation
```

## Risco

```text
SensitiveRead
```

## Funções Moodle candidatas

```text
core_completion_get_activities_completion_status
core_completion_get_course_completion_status
core_enrol_get_enrolled_users
mod_forum_get_forums_by_courses
mod_forum_get_forum_discussions
mod_forum_get_discussion_posts
```

## Regras

- Classificar como participação ou progresso, não como evasão.
- Critérios devem ser configuráveis:
  - dias sem acesso;
  - atividades incompletas;
  - ausência de participação em fóruns;
  - conclusão abaixo do esperado.
- Declarar limitações quando o Moodle não tiver rastreamento de conclusão habilitado.
- Não enviar mensagem nesta fase.

## Definition of done

- [ ] Usuário consegue consultar conclusão individual.
- [ ] Usuário consegue consultar progresso agregado do curso.
- [ ] Usuário consegue listar estudantes sem acesso recente.
- [ ] Usuário consegue listar estudantes com atividades incompletas.
- [ ] Testes cobrem conclusão desabilitada, ausência de acesso e dados incompletos.

---

# Fase 9 — Domínio Risco e acompanhamento pedagógico

## Objetivo

Cruzar dados de acesso, conclusão, entregas, notas e prazos para identificar estudantes em atenção e sugerir ações de acompanhamento.

## Tools candidatas

```text
identificar_alunos_em_atencao
identificar_alunos_em_risco
listar_alunos_risco_inatividade
listar_alunos_risco_desempenho
listar_alunos_risco_combinado
gerar_plano_intervencao
gerar_relatorio_risco_estudantes
consultar_recuperacao_aluno
consultar_perfil_aluno_360
```

## Escopos

```text
moodle.read.risk
moodle.read.reports
```

## Risco

```text
SensitiveRead
```

## Dependências

Esta fase depende das fases:

```text
Cursos
Participantes
Atividades
Entregas
Avaliações
Progresso
```

## Classificações permitidas

```text
Em atenção
Possível risco por inatividade
Possível risco por desempenho
Possível risco combinado
Necessita verificação humana
```

## Regras

- Não usar termos definitivos como:
  - evadido;
  - reprovado;
  - aprovado;
  - desistente;
  - abandono confirmado.
- Sempre distinguir:
  - achado;
  - hipótese;
  - recomendação;
  - limitação.
- Critérios devem ser configuráveis por ambiente ou entrada:
  - `inactiveDays`;
  - `minGradePercent`;
  - `maxPendingActivities`;
  - `includeCompletion`;
  - `includeGrades`;
  - `includeSubmissions`.
- Quando o usuário não informar critérios, declarar premissas usadas.

## Definition of done

- [ ] Usuário consegue listar estudantes em atenção.
- [ ] Usuário consegue listar possível risco por inatividade.
- [ ] Usuário consegue listar possível risco por desempenho.
- [ ] Usuário consegue listar possível risco combinado.
- [ ] Resposta separa achados, riscos, ações e limitações.
- [ ] Testes cobrem critérios configuráveis, dados ausentes e estudante sem nota.

---

# Fase 10 — Domínio Relatórios

## Objetivo

Consolidar dados por curso, turma, atividade e período em relatórios úteis para acompanhamento, monitoria, tutoria, coordenação e operação.

## Tools candidatas

```text
gerar_resumo_curso
gerar_relatorio_tutoria
gerar_relatorio_monitoria
gerar_relatorio_coordenacao
gerar_relatorio_pendencias_entrega
gerar_relatorio_pendencias_correcao
gerar_relatorio_risco_estudantil
gerar_relatorio_participacao
gerar_auditoria_sala
gerar_relatorio_qualidade_dados
relatorio_desempenho_quiz
relatorio_participacao_foruns
auditar_sala_virtual_senai
validar_cronograma_vs_moodle
relatorio_conselho_classe
relatorio_pos_execucao
consultar_pesquisas_curso
exportar_painel_operacional
```

## Escopos

```text
moodle.read.reports
moodle.read.risk
moodle.read.grades
moodle.read.submissions
```

## Risco

```text
ReadOnly
SensitiveRead
```

O risco depende do tipo de dado incluído.

## Regras

- Relatórios devem trazer, sempre que aplicável:
  - status;
  - achados;
  - riscos;
  - ações recomendadas;
  - limitações.
- Relatórios coletivos devem priorizar dados agregados.
- Dados individuais só aparecem quando necessários ao objetivo.
- Não emitir decisão oficial de aprovação, retenção, evasão ou sanção.
- Relatórios devem declarar período analisado e origem dos dados.

## Definition of done

- [ ] Relatório de resumo do curso funciona.
- [ ] Relatório de pendências de entrega funciona.
- [ ] Relatório de pendências de correção funciona.
- [ ] Relatório de risco estudantil funciona.
- [ ] Auditoria de sala funciona em modo leitura.
- [ ] Testes cobrem relatório vazio, relatório com dados sensíveis e limitações de permissão.

---

# Fase 11 — Domínio Comunicação com confirmação humana

## Objetivo

Preparar e enviar mensagens para estudantes, grupos ou curso com confirmação humana obrigatória.

## Tools candidatas

```text
preparar_mensagem_curso
confirmar_mensagem_curso
preparar_mensagem_grupo
confirmar_mensagem_grupo
preparar_mensagem_alunos_pendentes
confirmar_mensagem_alunos_pendentes
preparar_mensagem_alunos_em_atencao
confirmar_mensagem_alunos_em_atencao
```

## Escopos

```text
moodle.write.messages
```

## Feature flag

```text
MessagesWriteEnabled
```

## Risco

```text
HumanConfirmedWrite
```

## Funções Moodle candidatas

```text
core_message_send_instant_messages
core_message_send_messages_to_conversation
```

## Regras

- Sempre usar `prepare_*` antes de `confirm_*`.
- Prévia deve exibir:
  - curso;
  - público-alvo;
  - critérios de seleção;
  - quantidade de destinatários;
  - assunto;
  - corpo sanitizado;
  - riscos.
- Confirmação deve exigir texto exato.
- Segunda confirmação da mesma ação não pode reenviar.
- Mensagem coletiva não deve expor:
  - nota individual;
  - situação de risco individual;
  - e-mail de outros estudantes;
  - dados pessoais desnecessários.

## Definition of done

- [ ] Preparação de mensagem cria `PendingAction`.
- [ ] Confirmação envia mensagem.
- [ ] Segunda confirmação não reenvia.
- [ ] Auditoria registra preparação, confirmação e resultado.
- [ ] Testes cobrem escopo ausente, texto divergente, ação expirada e idempotência.

---

# Fase 12 — Domínio Agendamento de comunicação

## Objetivo

Agendar mensagens futuras para abertura, fechamento, pendências e retomada de estudos, usando fila própria do conector.

## Tools candidatas

```text
preparar_agendamento_mensagem
confirmar_agendamento_mensagem
listar_mensagens_agendadas
cancelar_mensagem_agendada
consultar_mensagem_agendada
```

## Escopos

```text
moodle.write.messages
moodle.write.scheduled_messages
```

## Feature flag

```text
ScheduledMessagesEnabled
```

## Risco

```text
HumanConfirmedWrite
```

## Regras

- Agendamento não deve depender de função nativa de mensagem programada do Moodle.
- Criar fila própria do conector.
- Revalidar destinatários antes do envio, quando configurado.
- Registrar snapshot do público no momento do agendamento.
- Permitir cancelamento antes da execução.
- Auditar:
  - criação;
  - confirmação;
  - cancelamento;
  - tentativa de envio;
  - falha;
  - sucesso.

## Definition of done

- [ ] Mensagem agenda apenas após confirmação.
- [ ] Mensagem pode ser cancelada.
- [ ] Job executa mensagens no horário previsto.
- [ ] Falhas são rastreáveis.
- [ ] Testes cobrem agendamento expirado, cancelado, executado e reprocessamento idempotente.

---

# Fase 13 — Domínio Feedback assistido

## Objetivo

Preparar feedback textual para atividades, com base em enunciado, submissão, critérios e rubrica quando disponível, sem alterar nota automaticamente.

## Tools candidatas

```text
preparar_pacote_correcao
rascunhar_feedback_atividade
preparar_feedback_atividade
confirmar_feedback_atividade
preparar_feedback_em_lote
```

## Escopos

```text
moodle.read.submissions
moodle.read.assignments
moodle.write.assignments.feedback
```

## Feature flag

```text
AssignmentFeedbackWriteEnabled
```

## Risco

```text
DraftOnly
HumanConfirmedWrite
```

## Funções Moodle candidatas

```text
mod_assign_get_assignments
mod_assign_get_submissions
mod_assign_get_submission_status
mod_assign_get_grades
mod_assign_save_grade
mod_assign_save_grades
```

## Regras

- `rascunhar_feedback_atividade` deve ser `DraftOnly`.
- `preparar_feedback_atividade` cria `PendingAction`.
- `confirmar_feedback_atividade` publica somente após confirmação.
- Feedback em lote deve permanecer `DraftOnly` até revisão explícita.
- Não inventar rubrica.
- Não lançar nota nesta fase.
- Prévia deve mostrar:
  - aluno;
  - atividade;
  - entrega referenciada;
  - feedback final;
  - limitações;
  - se houve ou não rubrica localizada.

## Definition of done

- [ ] Pacote de correção reúne dados necessários.
- [ ] Feedback preliminar é gerado como rascunho.
- [ ] Feedback não é publicado sem confirmação.
- [ ] Auditoria permite rastrear o texto aprovado.
- [ ] Testes cobrem ausência de rubrica, submissão vazia, confirmação errada e feature flag desabilitada.

---

# Fase 14 — Domínio Notas e avaliação crítica

## Objetivo

Permitir lançamento ou ajuste de nota apenas com controles reforçados, confirmação forte e autorização explícita.

## Tools candidatas

```text
preparar_lancamento_nota
confirmar_lancamento_nota
preparar_ajuste_nota
confirmar_ajuste_nota
consultar_previa_alteracao_nota
```

## Escopos

```text
moodle.write.assignments.grade
moodle.read.grades
moodle.read.submissions
```

## Feature flag

```text
AssignmentGradeWriteEnabled
```

## Risco

```text
CriticalHumanConfirmedWrite
```

## Funções Moodle candidatas

```text
mod_assign_save_grade
mod_assign_save_grades
mod_assign_submit_grading_form
```

## Regras

- Desabilitado por padrão em ambientes novos.
- Operações em lote ficam fora do primeiro release.
- Justificativa obrigatória.
- Confirmação deve incluir:
  - aluno;
  - atividade;
  - nota final;
  - confirmação explícita de lançamento.
- Quando possível, registrar nota anterior.
- Validar intervalo da nota.
- Validar que a atividade e o estudante pertencem ao curso informado.
- Exigir escopo específico e feature flag ativa.
- Avaliar se exige segundo fator operacional ou escopo administrativo.

## Definition of done

- [ ] Nota individual pode ser preparada.
- [ ] Nota individual só é lançada após confirmação forte.
- [ ] Nota fora do intervalo é rejeitada.
- [ ] Aluno incorreto é rejeitado.
- [ ] Atividade incorreta é rejeitada.
- [ ] Auditoria registra nota anterior quando a API permitir.
- [ ] Testes cobrem limites, permissão, idempotência, feature flag e confirmação textual.

---

# Fase 15 — Domínio Conteúdo de curso com escrita

## Objetivo

Permitir rascunhar ou atualizar conteúdos de curso com controle humano e prévia de alteração.

Esta fase deve vir depois da maturidade das fases de leitura, relatórios, comunicação e feedback.

## Tools candidatas

```text
preparar_topico_curso
confirmar_topico_curso
preparar_recurso_curso
confirmar_recurso_curso
preparar_atualizacao_descricao_atividade
confirmar_atualizacao_descricao_atividade
preparar_publicacao_pagina
confirmar_publicacao_pagina
```

## Escopos

```text
moodle.write.course_content
```

## Feature flag

```text
CourseContentWriteEnabled
```

## Risco

```text
HumanConfirmedWrite
CriticalHumanConfirmedWrite
AdminWrite
```

## Regras

- Desabilitado por padrão.
- Conteúdos extensos devem ser tratados como rascunho antes da publicação.
- Prévia deve mostrar diferença entre estado atual e proposto, quando aplicável.
- Não sobrescrever conteúdo sem diff ou confirmação explícita.
- Não publicar links privados ou sensíveis.
- HTML deve ser sanitizado.
- Para cronogramas e HTML institucional, respeitar padrão visual aprovado.

## Definition of done

- [ ] Conteúdo pode ser preparado como rascunho.
- [ ] Prévia mostra alteração proposta.
- [ ] Escrita só ocorre após confirmação.
- [ ] Auditoria registra payload sanitizado.
- [ ] Falhas de API retornam erro recuperável.
- [ ] Testes cobrem diff, HTML inválido, link sensível e feature flag desabilitada.

---

# Fase 16 — Domínio Administração de sala e configurações

## Objetivo

Avaliar, em fase avançada, tools de administração com impacto direto na experiência do estudante.

## Tools candidatas

```text
preparar_alteracao_data_atividade
confirmar_alteracao_data_atividade
preparar_ocultar_modulo
confirmar_ocultar_modulo
preparar_exibir_modulo
confirmar_exibir_modulo
preparar_criacao_atividade
confirmar_criacao_atividade
```

## Escopos

```text
moodle.write.course_admin
```

## Feature flag

```text
AdminCourseWriteEnabled
```

## Risco

```text
AdminWrite
CriticalHumanConfirmedWrite
```

## Regras

- Fora do MVP.
- Exigir autorização institucional.
- Exigir confirmação forte.
- Exigir justificativa.
- Exigir diff.
- Exigir registro completo de auditoria.
- Recomendado restringir a ambiente de homologação até aprovação formal.

## Definition of done

- [ ] Governança aprovada.
- [ ] Feature flag ativa apenas em ambiente controlado.
- [ ] Escopo administrativo separado.
- [ ] Testes cobrem impacto, confirmação forte e rollback operacional documentado.

---

# Fase 17 — Auditoria, observabilidade e suporte operacional

## Objetivo

Dar visibilidade operacional para diagnosticar autenticação, autorização, chamadas Moodle, execução de tools e falhas.

## Entregas

- [ ] Logs estruturados com `correlationId`.
- [ ] Endpoint administrativo protegido para consultar audit logs.
- [ ] Guia de troubleshooting para:
  - OAuth;
  - scopes;
  - API key;
  - broker OAuth local;
  - chamadas Moodle;
  - pending actions;
  - feature flags.
- [ ] Comandos documentados para VPS:
  - `docker compose logs`;
  - filtros por `correlationId`;
  - status de containers;
  - reinício controlado.
- [ ] Alertas básicos para:
  - falhas repetidas de autenticação;
  - falhas repetidas de escrita;
  - falhas Moodle;
  - timeouts.

## Regras

- Nenhum log deve incluir:
  - senha;
  - token Moodle;
  - API key;
  - JWT completo;
  - refresh token;
  - link privado com token.
- Logs de payload devem ser sanitizados.

## Definition of done

- [ ] Uma falha em produção pode ser rastreada por `correlationId`.
- [ ] Nenhum log inclui segredo.
- [ ] Documentação cobre fluxo ChatGPT Connector + broker OAuth local + MCP.
- [ ] Operador consegue diagnosticar falhas de login, escopo e Moodle.

---

# Fase 18 — Hardening de produção

## Objetivo

Reduzir risco antes de habilitar tools de escrita reais.

## Entregas

- [x] Rate limiting por usuário/conector.
- [x] Timeouts por gateway Moodle.
- [x] Retry policy por gateway Moodle.
- [x] Circuit breaker para instabilidade Moodle.
- [x] Sanitização centralizada de payloads de auditoria.
- [x] Revisão de `OpenWorld` por tool.
- [x] Revisão de `Destructive` por tool.
- [x] Revisão de secrets e variáveis de deploy.
- [x] Migrar criação de schema para migrations EF ou script versionado de banco.
- [x] Checklist de segurança por release.
- [x] Rollback documentado.

## Definition of done

- [ ] Checklist de segurança aprovado.
- [x] Escritas reais ficam desabilitadas por default em ambientes novos.
- [x] Deploy rollback documentado.
- [ ] Testes de segurança cobrem escopos, idempotência, expiração e tentativas indevidas.

---

# Fase 19 — Documentação e handoff

## Objetivo

Deixar o projeto fácil de operar, evoluir, revisar e transferir para novos desenvolvedores ou operadores.

## Entregas

- [ ] README atualizado com:
  - stack;
  - setup local;
  - autenticação;
  - MCP;
  - deploy;
  - testes.
- [ ] Documentação de como criar uma nova tool seguindo o padrão.
- [ ] Matriz de tools:
  - nome;
  - domínio;
  - status;
  - risco;
  - escopo;
  - feature flag;
  - função Moodle;
  - ReadOnly/Destructive/Idempotent/OpenWorld.
- [ ] Exemplos de requests MCP para:
  - leitura;
  - `prepare_*`;
  - `confirm_*`.
- [ ] Guia de release por fase.
- [ ] Runbook de troubleshooting.
- [ ] Runbook de deploy.
- [ ] Guia funcional para usuário final.

## Definition of done

- [ ] Novo desenvolvedor consegue criar uma tool seguindo o padrão sem consultar histórico do chat.
- [ ] Operador consegue diagnosticar login, scopes e logs com comandos documentados.
- [ ] Usuário funcional entende limites do conector.
- [ ] Documentação diferencia implementado, planejado e fora do escopo.

---

# Matriz resumida de domínios

| Fase | Domínio | Tipo principal | Risco principal | Escrita? |
|---|---|---|---|---|
| 0 | Segurança e contrato MCP | Fundação | — | Não |
| 1 | Autenticação e identidade | Fundação | — | Não |
| 2 | Cursos | Leitura | `ReadOnly` | Não |
| 3 | Participantes | Leitura | `SensitiveRead` | Não |
| 4 | Conteúdos e estrutura | Leitura | `ReadOnly` | Não |
| 5 | Atividades | Leitura | `ReadOnly` | Não |
| 6 | Entregas e submissões | Leitura | `SensitiveRead` | Não |
| 7 | Avaliações e notas em leitura | Leitura | `SensitiveRead` | Não |
| 8 | Progresso e participação | Leitura | `SensitiveRead` | Não |
| 9 | Risco e acompanhamento | Análise | `SensitiveRead` | Não |
| 10 | Relatórios | Análise | `ReadOnly` / `SensitiveRead` | Não |
| 11 | Comunicação | Escrita confirmada | `HumanConfirmedWrite` | Sim |
| 12 | Agendamento | Escrita confirmada | `HumanConfirmedWrite` | Sim |
| 13 | Feedback assistido | Rascunho/escrita | `DraftOnly` / `HumanConfirmedWrite` | Sim |
| 14 | Notas | Escrita crítica | `CriticalHumanConfirmedWrite` | Sim |
| 15 | Conteúdo com escrita | Escrita confirmada | `HumanConfirmedWrite` / `AdminWrite` | Sim |
| 16 | Administração de sala | Escrita crítica | `AdminWrite` | Sim |
| 17 | Observabilidade | Operação | — | Não |
| 18 | Hardening | Operação | — | Não |
| 19 | Documentação e handoff | Operação | — | Não |

---

# Ordem recomendada de implementação prática

## Primeiro ciclo funcional

```text
Fase 2 - Cursos
Fase 3 - Participantes
Fase 4 - Conteúdos e estrutura
Fase 5 - Atividades
```

Entrega esperada:

```text
O usuário consegue saber onde está, quem está no curso e o que existe na sala.
```

## Segundo ciclo funcional

```text
Fase 6 - Entregas e submissões
Fase 7 - Avaliações e notas em leitura
Fase 8 - Progresso e participação
```

Entrega esperada:

```text
O usuário consegue acompanhar execução, desempenho e pendências.
```

## Terceiro ciclo funcional

```text
Fase 9 - Risco e acompanhamento
Fase 10 - Relatórios
```

Entrega esperada:

```text
O usuário consegue priorizar ações e gerar relatórios.
```

## Quarto ciclo funcional

```text
Fase 11 - Comunicação
Fase 12 - Agendamento
```

Entrega esperada:

```text
O usuário consegue agir com mensagem confirmada e auditável.
```

## Quinto ciclo funcional

```text
Fase 13 - Feedback assistido
Fase 14 - Notas
```

Entrega esperada:

```text
O usuário consegue apoiar correção e, se autorizado, lançar nota com controle forte.
```

## Sexto ciclo funcional

```text
Fase 15 - Conteúdo com escrita
Fase 16 - Administração de sala
```

Entrega esperada:

```text
O usuário consegue alterar sala somente com governança, diff e confirmação.
```

## Ciclo contínuo

```text
Fase 17 - Observabilidade
Fase 18 - Hardening
Fase 19 - Documentação
```

Entrega esperada:

```text
O projeto fica operável, seguro e transferível.
```

---

# Padrão para criar uma nova tool

Toda nova tool deve ter, antes do desenvolvimento:

```text
Nome
Domínio
Objetivo
Status
Tipo: leitura / draft / escrita confirmada / escrita crítica
Risco
Escopos
Feature flag, se houver
Funções Moodle candidatas
Entrada JSON
Saída JSON
Regras de privacidade
Regras de auditoria
Casos de erro
Testes obrigatórios
Definition of done
```

## Template

```markdown
## Tool: `nome_da_tool`

### Domínio

### Status

Planejada / Em desenvolvimento / Implementada / Descontinuada.

### Objetivo

### Tipo

Leitura / Draft / Escrita confirmada / Escrita crítica.

### Risco

### Escopos

### Feature flag

### Funções Moodle candidatas

### Entrada

### Saída

### Regras

### Erros previstos

### Testes

### Definition of done
```

---

# Política de privacidade por padrão

- Usar o mínimo necessário de dados pessoais.
- Preferir agregados.
- Mostrar dados individuais apenas quando necessário.
- Não expor notas em mensagens coletivas.
- Não expor e-mails em listas por padrão.
- Não registrar tokens, senhas, API keys, JWT completo, links sensíveis ou conteúdo integral de submissões em logs.
- Não classificar estudante de forma definitiva.
- Usar linguagem cautelosa:
  - “em atenção”;
  - “possível risco”;
  - “necessita verificação humana”.

---

# Fora do escopo inicial

Não implementar no MVP:

```text
lançamento de nota em lote
edição estrutural de sala
ocultar/exibir módulo
alterar datas de atividades
criar atividade avaliativa
matricular/desmatricular estudante
alterar permissões Moodle
alterar papéis
publicar conteúdo HTML sem diff
decisão automática de aprovação, reprovação ou evasão
```

---

# Próximo passo recomendado

Implementar a Fase 2 com foco em `listar_meus_cursos`, mantendo a listagem leve e sem cálculos derivados.

## Primeira entrega objetiva

```text
Tool: listar_meus_cursos
Tipo: ReadOnly
Escopo: moodle.read.courses
Retorno: ToolResponse<ListCoursesResult>
Sem notas
Sem entregas
Sem risco
Com cache curto
Com teste MCP
```

## Em seguida

```text
buscar_cursos
consultar_curso
consultar_calendario_curso
```

Depois avançar para Participantes, Conteúdos e Atividades.
