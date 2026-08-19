# Catálogo de Tools MCP

Este catálogo documenta as tools registradas no estado atual do repositório; aliases podem aparecer agrupados na tabela. Em caso de divergência, as fontes de registro são `src/MoodleConnector.Presentation/Tools`, e os fluxos de escrita devem ser conferidos também nos handlers em `src/MoodleConnector.Application`.

## Memória e orientações pedagógicas

O servidor MCP não vê a conversa por conta própria: memórias e orientações só são
consultadas ou registradas quando a IA ou o cliente chama as tools correspondentes e
envia o contexto necessário nos argumentos.

### `manage_user_memory`

Mantém memórias duráveis privadas do usuário autenticado. Aceita `action=salvar`,
`listar` ou `remover`; não altera o Moodle. Por remover estado interno, a tool anuncia
`ReadOnly=false`, `Destructive=true`, `Idempotent=true` e `OpenWorld=false`.

| Argumento | Uso |
| --- | --- |
| `action` | Obrigatório: `salvar`, `listar` ou `remover`. |
| `category` | Em `salvar`: `preferencia`, `caminho`, `correcao` ou `decisao`; também filtra `listar`. |
| `key`, `content`, `origin` | Obrigatórios em `salvar`; `origin` é `explicit` ou `inferred`. |
| `query`, `moodleAlias`, `courseId`, `limit` | Filtros opcionais de `listar`; `limit` padrão é 20. |
| `memoryId` | UUID obrigatório em `remover`. |

A resposta estruturada segue `ToolResponse<MemoryToolResponse>`. `data.action` repete a
ação; `data.memory` aparece ao salvar, `data.memories` ao listar e `data.removed` ao
remover. Erros de argumentos retornam resposta MCP controlada. Nunca envie senhas,
tokens, chaves, segredos ou dados pessoais/acadêmicos de alunos para esta tool.

Hints de uso: liste antes de remover para obter o `memoryId`; salve apenas fatos
duráveis e reutilizáveis; automatizações podem consultar preferências antes de agir,
mas não devem salvar inferências sensíveis nem remover memórias sem intenção explícita.

Para conteudos extensos como modelos de HTML/Markdown, use
`save_user_memory_document` e deixe a memoria `category=modelo` apenas como link
semantico para o documento completo.

### Documentos de memoria do usuario

Mantem documentos duraveis privados do usuario autenticado para modelos e referencias
extensas da IA; nao altera o Moodle. A superficie recomendada usa tools dedicadas:
`save_user_memory_document`, `list_user_memory_documents`,
`read_user_memory_document` e `remove_user_memory_document`. Ao salvar, cria
ou atualiza tambem uma memoria curta `category=modelo` apontando para o documento
completo.

| Argumento | Uso |
| --- | --- |
| `key`, `title`, `content`, `format`, `origin` | Obrigatorios em `save_user_memory_document`; `format` e `markdown`, `html` ou `text`; `origin` e `explicit` ou `inferred`. |
| `query`, `moodleAlias`, `courseId`, `limit` | Filtros opcionais de `list_user_memory_documents`; `limit` padrao e 20. |
| `documentId` | UUID obrigatorio em `read_user_memory_document` e `remove_user_memory_document`. |

A resposta estruturada segue `ToolResponse<MemoryDocumentToolResponse>`. `data.document`
aparece em `salvar` e `ler`, `data.documents` em `listar`, e `data.removed` em
`remover`. Use Markdown quando estiver criando um modelo novo para a IA; use HTML quando
for preservar um modelo Moodle existente, como cronogramas com tabela inline.

`gerenciar_documento_memoria_usuario` permanece como compatibilidade para `action=salvar`,
`listar` e `ler`, mas nao remove documentos. A remocao destrutiva fica isolada em
`remove_user_memory_document` para que hosts MCP/ChatGPT apliquem confirmacao e
safety ao caminho correto sem bloquear salvamentos internos.

### `get_pedagogical_guidelines`

Pesquisa os sete guias Markdown publicados junto da aplicação. Deve ser consultada
antes de avaliação, feedback, planejamento, fóruns, acompanhamento de estudantes e
relatórios pedagógicos. Aceita `query` obrigatória e `limit` opcional (padrão 5).
Retorna `ToolResponse<PedagogicGuidanceResponse>` com `data.results`; cada resultado
contém `relativePath`, `title`, `section`, `excerpt` e `score`.

Se `query` estiver vazia, retorna erro controlado. Os metadados são `ReadOnly=true`,
`Destructive=false`, `Idempotent=true` e `OpenWorld=false`. A busca é lexical e limitada
ao acervo local: não consulta a internet, não substitui julgamento profissional e não
garante orientação para assuntos ausentes dos guias. Em automações, consulte com os
conceitos centrais da tarefa e trate resultados como referência, preservando revisão
humana e minimização de dados.

## Tools implementadas

| Tool | Título | Risco | Leitura | Escrita | Status |
| --- | --- | --- | --- | --- | --- |
| `moodle_diagnose_connection` | Diagnosticar Conexão Moodle | `ReadOnly` | Verifica conectividade, site, funções, permissão de escrita e fluxos disponíveis | Não | Implementada |
| `moodle_list_functions` | Listar Funções Moodle | `ReadOnly` | Lista por conexão as funções autorizadas pelo serviço | Não | Implementada |
| `moodle_describe_function` / `moodle_check_function` | Descrever Função Moodle | `ReadOnly` | Consulta disponibilidade e classificação local de risco | Não | Implementadas |
| `moodle_list_available_flows` | Listar Fluxos Moodle Disponíveis | `ReadOnly` | Mostra a estratégia compatível e as funções ausentes | Não | Implementada |
| `moodle_execute_read` | Executar Leitura Moodle | `ReadOnly` | Executa somente funções classificadas como leitura | Não | Implementada |
| `moodle_prepare_write` | Preparar Escrita Moodle | `HumanConfirmedWrite` | Prévia, hash de parâmetros e ação pendente | Não | Implementada; desativada por padrão |
| `moodle_confirm_write` | Confirmar Escrita Moodle | `HumanConfirmedWrite` | Não | Executa escrita controlada após confirmação literal | Implementada; desativada por padrão |
| `save_user_memory_document` | Salvar documento de memoria do usuario | `InternalStateWrite` | Nao | Salva documento interno e link de memoria | Implementada |
| `list_user_memory_documents` | Listar documentos de memoria do usuario | `ReadOnly` | Sim | Nao | Implementada |
| `read_user_memory_document` | Ler documento de memoria do usuario | `ReadOnly` | Sim | Nao | Implementada |
| `remove_user_memory_document` | Remover documento de memoria do usuario | `InternalStateWrite` | Nao | Remove documento interno e link de memoria | Implementada |
| `gerenciar_documento_memoria_usuario` | Gerenciar documento de memoria do usuario | `InternalStateWrite` | Lista/le documentos | Compatibilidade para salvar; remocao desabilitada | Implementada |
| `get_pedagogical_guidelines` | Consultar orientações pedagógicas | `ReadOnly` | Sim | Não | Implementada |
| `manage_user_memory` | Gerenciar memória do usuário | `InternalStateWrite` | Lista memórias | Salva/remove memória interna | Implementada |
| `list_my_courses` | Listar Meus Cursos | `ReadOnly` | Sim | Não | Implementada |
| `list_courses` | List Courses | `ReadOnly` | Sim | Não | Implementada |
| `search` | Search Moodle courses | `ReadOnly` | Sim | Não | Implementada |
| `fetch` | Fetch Moodle course | `ReadOnly` | Sim | Não | Implementada |
| `search_courses` | Buscar Cursos | `ReadOnly` | Sim | Não | Implementada |
| `search_courses` | Search Courses | `ReadOnly` | Sim | Não | Implementada |
| `get_course` | Consultar Curso | `ReadOnly` | Sim | Não | Implementada |
| `get_course` | Get Course | `ReadOnly` | Sim | Não | Implementada |
| `list_course_participants` | Listar Participantes Curso | `SensitiveRead` | Sim | Não | Implementada |
| `list_course_participants` | List Course Participants | `SensitiveRead` | Sim | Não | Implementada |
| `list_course_students` | Listar Alunos Curso | `SensitiveRead` | Sim | Não | Implementada |
| `list_course_students` | List Course Students | `SensitiveRead` | Sim | Não | Implementada |
| `list_course_groups` | Listar Grupos Curso | `SensitiveRead` | Sim | Não | Implementada |
| `list_course_groups` | List Course Groups | `SensitiveRead` | Sim | Não | Implementada |
| `get_group_members` | Consultar Membros Grupo | `SensitiveRead` | Sim | Não | Implementada |
| `get_group_members` | Get Group Members | `SensitiveRead` | Sim | Não | Implementada |
| `list_course_contents` | Listar Conteudos Curso | `ReadOnly` | Sim | Não | Implementada |
| `list_course_contents` | List Course Contents | `ReadOnly` | Sim | Não | Implementada |
| `get_course_module` | Consultar Modulo Curso | `ReadOnly` | Sim | Não | Implementada |
| `get_course_module` | Get Course Module | `ReadOnly` | Sim | Não | Implementada |
| `list_course_resources` | Listar Recursos Curso | `ReadOnly` | Sim | Não | Implementada |
| `list_course_resources` | List Course Resources | `ReadOnly` | Sim | Não | Implementada |
| `list_course_files` | Listar Arquivos Curso | `ReadOnly` | Sim | Não | Implementada |
| `list_course_files` | List Course Files | `ReadOnly` | Sim | Não | Implementada |
| `list_course_pages` | Listar Paginas Curso | `ReadOnly` | Sim | Não | Implementada |
| `list_course_pages` | List Course Pages | `ReadOnly` | Sim | Não | Implementada |
| `list_course_urls` | Listar URLs Curso | `ReadOnly` | Sim | Não | Implementada |
| `list_course_urls` | List Course URLs | `ReadOnly` | Sim | Não | Implementada |
| `audit_course_structure` | Auditar Estrutura Curso | `ReadOnly` | Sim | Não | Implementada |
| `audit_course_structure` | Audit Course Structure | `ReadOnly` | Sim | Não | Implementada |
| `list_course_activities` | Listar Atividades Curso | `ReadOnly` | Sim | Não | Implementada |
| `list_course_activities` | List Course Activities | `ReadOnly` | Sim | Não | Implementada |
| `get_course_activity` | Consultar Atividade | `ReadOnly` | Sim | Não | Implementada |
| `get_course_activity` | Get Course Activity | `ReadOnly` | Sim | Não | Implementada |
| `list_course_assignments` | Listar Tarefas Curso | `ReadOnly` | Sim | Não | Implementada |
| `list_course_assignments` | List Course Assignments | `ReadOnly` | Sim | Não | Implementada |
| `get_assignment` | Consultar Tarefa | `ReadOnly` | Sim | Não | Implementada |
| `get_assignment` | Get Assignment | `ReadOnly` | Sim | Não | Implementada |
| `read_forum` | Ler Forum | `ReadOnly` | Sim | Não | Implementada |
| `read_forum` | Read Forum | `ReadOnly` | Sim | Não | Implementada |
| `create_forum_post_preview` | Criar Previa Post Forum | `HumanConfirmedWrite` | Não | Cria ação pendente | Implementada |
| `create_forum_post_preview` | Create Forum Post Preview | `HumanConfirmedWrite` | Não | Cria ação pendente | Implementada |
| `confirm_forum_post` | Confirmar Post Forum Moodle | `HumanConfirmedWrite` | Não | Escrita oficial no Moodle | Implementada |
| `confirm_forum_post` | Confirm Forum Post | `HumanConfirmedWrite` | Não | Escrita oficial no Moodle | Implementada |
| `list_course_quizzes` | Listar Quizzes Curso | `ReadOnly` | Sim | Não | Implementada |
| `list_course_quizzes` | List Course Quizzes | `ReadOnly` | Sim | Não | Implementada |
| `get_quiz` | Consultar Quiz | `ReadOnly` | Sim | Não | Implementada |
| `get_quiz` | Get Quiz | `ReadOnly` | Sim | Não | Implementada |
| `list_course_scorms` | Listar SCORMs Curso | `ReadOnly` | Sim | Não | Implementada |
| `list_course_scorms` | List Course SCORMs | `ReadOnly` | Sim | Não | Implementada |
| `list_activity_deadlines` | Consultar Prazos Atividades | `ReadOnly` | Sim | Não | Implementada |
| `list_activity_deadlines` | List Activity Deadlines | `ReadOnly` | Sim | Não | Implementada |
| `list_assignment_submissions` | Listar Entregas Atividade | `SensitiveRead` | Sim | Não | Implementada |
| `list_assignment_submissions` | List Assignment Submissions | `SensitiveRead` | Sim | Não | Implementada |
| `get_student_submission` | Consultar Entrega Aluno | `SensitiveRead` | Sim | Não | Implementada |
| `get_student_submission` | Get Student Submission | `SensitiveRead` | Sim | Não | Implementada |
| `list_pending_submissions` | Listar Entregas Pendentes | `SensitiveRead` | Sim | Não | Implementada |
| `list_pending_submissions` | List Pending Submissions | `SensitiveRead` | Sim | Não | Implementada |
| `list_late_submissions` | Listar Entregas Atrasadas | `SensitiveRead` | Sim | Não | Implementada |
| `list_late_submissions` | List Late Submissions | `SensitiveRead` | Sim | Não | Implementada |
| `list_submissions_awaiting_grading` | Listar Entregas Aguardando Correcao | `SensitiveRead` | Sim | Não | Implementada |
| `list_submissions_awaiting_grading` | List Submissions Awaiting Grading | `SensitiveRead` | Sim | Não | Implementada |
| `get_submission_status` | Consultar Status Submissao | `SensitiveRead` | Sim | Não | Implementada |
| `get_submission_status` | Get Submission Status | `SensitiveRead` | Sim | Não | Implementada |
| `get_student_completion` | Consultar Progresso Aluno | `SensitiveRead` | Sim | Não | Implementada |
| `get_student_completion` | Get Student Completion | `SensitiveRead` | Sim | Não | Implementada |
| `get_student_gradebook` | Consultar Boletim Aluno | `SensitiveRead` | Sim | Não | Implementada |
| `get_student_gradebook` | Get Student Gradebook | `SensitiveRead` | Sim | Não | Implementada |
| `report_students_at_risk` | Gerar Relatorio Risco Estudantes | `SensitiveRead` | Sim | Não | Implementada |
| `report_students_at_risk` | Report Students at Risk | `SensitiveRead` | Sim | Não | Implementada |
| `discover_grading_functions` | Descobrir Funcoes Moodle Correcao | `ReadOnly` | Sim | Não | Implementada |
| `discover_moodle_grading_functions` | Discover Moodle Grading Functions | `ReadOnly` | Sim | Não | Implementada |
| `execute_grading_discovery` | Executar Descoberta Tecnica Correcao | `ReadOnly` | Sim | Não | Implementada |
| `list_gradable_submissions` | Listar Entregas Corrigiveis | `SensitiveRead` | Sim | Não | Implementada |
| `create_assisted_grading_batch` | Criar Lote Correcao Assistida | `DraftOnly` | Não | Cria job interno | Implementada |
| `get_grading_batch_status` | Consultar Status Lote Correcao | `ReadOnly` | Sim | Não | Implementada |
| `export_grading_coordination_report` | Exportar Relatorio Correcao Coordenacao | `ReadOnly` | Sim | Não | Implementada |
| `cancel_assisted_grading_batch` | Cancelar Lote Correcao Assistida | `DraftOnly` | Não | Escrita interna | Implementada |
| `get_assisted_grading_item` | Consultar Item Correcao Assistida | `ReadOnly` | Sim | Não | Implementada |
| `get_grading_item_context` | Consultar Contexto Item Correcao Assistida | `ReadOnly` | Sim | Não | Implementada |
| `update_grading_draft` | Atualizar Rascunho Correcao | `DraftOnly` | Não | Escrita interna | Implementada |
| `prepare_submission_grading` | Preparar Correcao Entrega | `DraftOnly` | Não | Escrita interna | Implementada |
| `prepare_ai_grading_batch` | Preparar Lote Correcao IA | `ReadOnly` | Sim | Não | Implementada |
| `save_ai_grading_batch` | Salvar Correcoes IA Lote | `DraftOnly` | Não | Escrita interna | Implementada |
| `review_batch_feedbacks` | Revisar Feedbacks Lote | `ReadOnly` | Sim | Não | Implementada |
| `get_grading_batch_audit` | Consultar Auditoria Correcao Lote | `ReadOnly` | Sim | Não | Implementada |
| `create_batch_grade_launch_preview` | Criar Previa Lancamento Lote | `CriticalHumanConfirmedWrite` | Não | Cria ação pendente | Implementada |
| `confirm_batch_grade_launch` | Confirmar Lancamento Lote Moodle | `CriticalHumanConfirmedWrite` | Não | Escrita oficial no Moodle | Implementada |
| `prepare_welcome_message` / `confirm_welcome_message` | Mensagem Boas Vindas | `HumanConfirmedWrite` | Prévia | Mensagem individual no Moodle | Implementadas; bloqueadas por padrão |
| `prepare_access_reminder` / `confirm_access_reminder` | Mensagem Cobranca Acesso | `HumanConfirmedWrite` | Prévia | Mensagem individual no Moodle | Implementadas; bloqueadas por padrão |
| `prepare_activity_reminder` / `confirm_activity_reminder` | Mensagem Cobranca SA | `HumanConfirmedWrite` | Prévia | Mensagem individual no Moodle | Implementadas; bloqueadas por padrão |
| `prepare_recovery_message` / `confirm_recovery_message` | Mensagem Recuperacao | `HumanConfirmedWrite` | Prévia | Mensagem individual no Moodle | Implementadas; bloqueadas por padrão |
| `prepare_closing_message` / `confirm_closing_message` | Mensagem Encerramento | `HumanConfirmedWrite` | Prévia | Mensagem individual no Moodle | Implementadas; bloqueadas por padrão |
| `prepare_followup_message` / `confirm_followup_message` | Mensagem Acompanhamento | `HumanConfirmedWrite` | Prévia | Mensagem individual no Moodle | Implementadas; bloqueadas por padrão |
| `prepare_individual_grade_launch` / `prepare_individual_grade_launch` | Preparar Nota Individual | `CriticalHumanConfirmedWrite` | Nota atual/prévia | Cria ação pendente | Implementadas (aliases PT/EN) |
| `confirm_individual_grade_launch` / `confirm_individual_grade_launch` | Confirmar Nota Individual | `CriticalHumanConfirmedWrite` | Não | Nota/feedback individual no Moodle | Implementadas (aliases PT/EN) |
| `get_grading_audit` | Consultar Auditoria Correcao | `ReadOnly` | Sim | Não | Implementada |
| `grading-review-app` | Grading Review App | `ReadOnly` | Sim | Não | Implementada (MCP Resource) |
| `prepare_demo_action` | Preparar Acao Demo | `HumanConfirmedWrite` | Não | Não executa escrita real | Implementada como demo |
| `confirm_demo_action` | Confirmar Acao Demo | `HumanConfirmedWrite` | Não | Não executa escrita real | Implementada como demo |

## Mensagens tipificadas do tutor

`MoodleTutorMessageTools.cs` expõe seis pares `preparar_*` / `confirmar_*`: boas-vindas, cobrança de acesso, cobrança de SA, recuperação, encerramento e acompanhamento. A preparação recebe `courseId`, `recipientIds`, texto opcional e alias, resolve nomes quando possível e cria `PendingAction` com prévia, riscos, expiração e texto literal. A confirmação recebe `pendingActionId`, texto literal e alias, exige escopo `moodle.write`; a conexão precisa de `CanWrite=true`, e o gateway usa `core_message_send_instant_messages` para cada destinatário.

Limitações: são mensagens instantâneas individuais, sem broadcast atômico, agendamento ou garantia de leitura. O resultado pode ter sucessos e falhas por destinatário. No `appsettings.json` versionado atual, `MessagesWriteEnabled=true`; quando definida como `false` por ambiente, a flag bloqueia a preparação e a confirmação.

## Nota individual confirmada

`MoodleIndividualGradeTools.cs` expõe o par em português `prepare_individual_grade_launch` / `confirm_individual_grade_launch` e os aliases em inglês `prepare_individual_grade_launch` / `confirm_individual_grade_launch`. A preparação recebe curso, tarefa, estudante, nota proposta, justificativa, feedback opcional e alias; consulta a nota atual e cria `PendingAction` com prévia e confirmação literal que inclui a nota. A confirmação exige `moodle.write.assignments.grade`, conexão `CanWrite=true`, `AssignmentGradeWriteEnabled=true`, pending action válida e `mod_assign_save_grade` disponível.

Limitações: a escrita é individual e imediatamente visível ao estudante; não decide nota nem substitui revisão pedagógica. No `appsettings.json` versionado atual, `AssignmentGradeWriteEnabled=true`; quando definida como `false` por ambiente, a confirmação é recusada.

## `list_my_courses` / `list_courses`

Descrição:

- Lista cursos vinculados ao usuário autenticado no Moodle.
- Usa a conexão Moodle atual.
- Aceita alias opcional da conexão Moodle.
- Usa cache em memória por conexão/usuário para evitar chamadas repetidas ao Moodle.
- Não calcula notas, pendências, tentativas ou prazos. Esses dados devem ficar em tools específicas.
- Em falha de consulta ao Moodle, retorna erro controlado em `ToolResponse`.

Parâmetros:

| Nome | Tipo | Descrição |
| --- | --- | --- |
| `pagina` / `page` | `int` | Página de resultados, iniciando em 1. Padrão: 1. |
| `limite` / `limit` | `int` | Quantidade máxima de cursos por página, de 1 a 100. Padrão: 20. |
| `moodleAlias` | `string?` | Alias da conexão Moodle. Quando omitido, usa a conexão padrão. |

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `true` |
| `Destructive` | `false` |
| `Idempotent` | `true` |
| `OpenWorld` | `false` |
| `UseStructuredContent` | `true` |
| `OutputSchemaType` | `ToolResponse<ListMyCoursesResponse>` |

Resposta estruturada:

```json
{
  "status": "ok",
  "data": {
    "total": 42,
    "page": 1,
    "total_pages": 3,
    "has_next_page": true,
    "courses": [
      {
        "courseId": "123",
        "idNumber": "EXT-123",
        "shortName": "CURSO-001",
        "fullName": "Nome do curso",
        "displayName": "Nome exibido do curso",
        "categoryId": 10,
        "categoryName": "Categoria",
        "startDate": "2026-01-01T00:00:00+00:00",
        "endDate": "2026-12-31T00:00:00+00:00",
        "visible": true,
        "viewUrl": "https://moodle.example/course/view.php?id=123",
        "courseImage": "https://moodle.example/pluginfile.php/course.png",
        "progress": 75.5,
        "hasProgress": true,
        "isFavourite": false,
        "lastAccessAt": "2026-05-31T12:00:00+00:00"
      }
    ]
  },
  "warnings": [],
  "auditId": null,
  "timestamp": "2026-05-31T00:00:00Z"
}
```

## `search_courses` / `search_courses`

Descrição:

- Busca dentro dos cursos vinculados ao usuário autenticado.
- Usa o mesmo cache leve de `list_my_courses`.
- Filtra por `courseId`, `idNumber`, `shortName`, nome completo, nome de exibição ou categoria.
- Não consulta notas, entregas, conclusão ou risco.

Parâmetros:

| Nome | Tipo | Descrição |
| --- | --- | --- |
| `termo` / `query` | `string` | Termo de busca. |
| `limite` / `limit` | `int` | Quantidade máxima de cursos, de 1 a 20. |
| `moodleAlias` | `string?` | Alias da conexão Moodle. Quando omitido, usa a conexão padrão. |

Resposta estruturada:

- Mesmo contrato de `list_my_courses` / `list_courses`.

## `search` / `fetch`

Descrição:

- Implementam o formato padrão recomendado para ChatGPT Apps connector-like/company knowledge.
- `search` recebe apenas `query` e retorna `structuredContent.results[]` com `id`, `title` e `url`.
- `fetch` recebe apenas `id` e retorna `structuredContent` com `id`, `title`, `text`, `url` e `metadata`.
- O mesmo JSON também é retornado como um item `content` de texto para compatibilidade MCP.
- Ambos são somente leitura, idempotentes e usam apenas cursos vinculados ao usuário autenticado.

Parâmetros:

| Tool | Nome | Tipo | Descrição |
| --- | --- | --- | --- |
| `search` | `query` | `string` | Termo de busca em cursos Moodle autorizados. |
| `fetch` | `id` | `string` | `courseId`, `shortName` ou `idNumber` do curso. |

## `get_course` / `get_course`

Descrição:

- Consulta um curso vinculado ao usuário autenticado por `courseId`, `shortName` ou `idNumber`.
- Retorna apenas metadados básicos do curso.
- Não consulta notas, entregas, conclusão ou risco.

Parâmetros:

| Nome | Tipo | Descrição |
| --- | --- | --- |
| `courseId` | `string` | Identificador do curso, nome curto ou idnumber. |
| `moodleAlias` | `string?` | Alias da conexão Moodle. Quando omitido, usa a conexão padrão. |

Resposta estruturada:

```json
{
  "status": "ok",
  "data": {
    "course": {
      "courseId": "123",
      "idNumber": "EXT-123",
      "shortName": "CURSO-001",
      "fullName": "Nome do curso"
    }
  },
  "warnings": [],
  "auditId": null,
  "timestamp": "2026-05-31T00:00:00Z"
}
```

## `list_course_participants` / `list_course_participants`

Notas de diagnostico:

- A consulta solicita explicitamente `roles` e `groups` ao Moodle.
- Campos ausentes ou vazios nao invalidam a resposta; geram `warnings` quando a classificacao ou a separacao por grupos pode estar incompleta.
- Respostas vazias diferenciam ausencia de participantes de pagina possivelmente fora do intervalo.

Descrição:

- Lista participantes de um curso vinculado ao usuário autenticado.
- Resolve `courseId`, `shortName` ou `idnumber` contra os cursos do usuário antes de consultar participantes.
- Exige paginação explícita.
- Filtra por `active`, `suspended` ou `all`.
- Não retorna e-mail por padrão.
- Inclui papeis, grupos e datas de acesso quando o Moodle e as permissões retornarem esses campos.

Parâmetros:

| Nome | Tipo | Descrição |
| --- | --- | --- |
| `courseId` | `string` | Identificador do curso, nome curto ou idnumber. |
| `pagina` / `page` | `int` | Página iniciando em 1. |
| `tamanhoPagina` / `pageSize` | `int` | Tamanho de página, de 1 a 50. |
| `status` | `string` | `ativos`/`active`, `suspensos`/`suspended` ou `todos`/`all`. |
| `incluirEmail` / `includeEmail` | `bool` | Inclui e-mail somente quando solicitado e autorizado pelo Moodle. |
| `moodleAlias` | `string?` | Alias da conexão Moodle. Quando omitido, usa a conexão padrão. |

Resposta estruturada:

```json
{
  "status": "ok",
  "data": {
    "courseId": "123",
    "page": 1,
    "pageSize": 20,
    "status": "active",
    "studentsOnly": false,
    "includeEmail": false,
    "hasMore": false,
    "count": 1,
    "participants": [
      {
        "userId": "777",
        "fullName": "Aluno Teste",
        "email": null,
        "suspended": false,
        "firstAccessAt": "2026-01-01T00:00:00+00:00",
        "lastAccessAt": "2026-05-31T12:00:00+00:00",
        "lastCourseAccessAt": "2026-05-31T13:00:00+00:00",
        "roles": [
          {
            "roleId": "5",
            "shortName": "student",
            "name": "Estudante"
          }
        ],
        "groups": [
          {
            "groupId": "99",
            "name": "Grupo A"
          }
        ]
      }
    ]
  },
  "warnings": [],
  "auditId": null,
  "timestamp": "2026-06-01T00:00:00Z"
}
```

## `list_course_students` / `list_course_students`

Fallback de classificacao:

- Participantes sem qualquer papel (`roles: []`) sao incluidos por fallback, com warning explicito.
- Quando `roles` vier preenchido, somente participantes com papel de estudante/aluno sao incluidos; qualquer conjunto sem `student` e excluido.
- Quando roles estiverem preenchidas, o papel de estudante/aluno continua sendo a fonte primaria.

Descrição:

- Usa o mesmo contrato de participantes.
- Retorna apenas usuários com papel de estudante/aluno quando o Moodle retornar papeis no curso.
- Mantém e-mail omitido por padrão.

## `list_course_groups` / `list_course_groups`

Descrição:

- Lista grupos de um curso vinculado ao usuário autenticado.
- Não retorna descrição HTML do grupo para reduzir exposição de dados desnecessários.

Parâmetros:

| Nome | Tipo | Descrição |
| --- | --- | --- |
| `courseId` | `string` | Identificador do curso, nome curto ou idnumber. |
| `moodleAlias` | `string?` | Alias da conexão Moodle. Quando omitido, usa a conexão padrão. |

## `get_group_members` / `get_group_members`

Descrição:

- Lista membros de um grupo por `courseId` e `groupId`.
- Usa paginação e o mesmo contrato de resposta de participantes.
- Não retorna e-mail por padrão.

## `list_course_contents` / `list_course_contents`

Descrição:

- Lista seções e módulos de um curso vinculado ao usuário autenticado.
- Usa `core_course_get_contents`.
- Aceita filtro por tipo: `resource`, `page`, `url`, `book`, `folder`, `label`, `assign`, `quiz`, `scorm` ou `forum`.
- Não baixa arquivos; retorna apenas metadados.
- Sanitiza URLs removendo parâmetros sensíveis como `token`, `wstoken` e `sesskey`.

Parâmetros:

| Nome | Tipo | Descrição |
| --- | --- | --- |
| `courseId` | `string` | Identificador do curso, nome curto ou idnumber. |
| `tipoModulo` / `moduleType` | `string?` | Filtro opcional por tipo de módulo. |
| `incluirOcultos` / `includeHidden` | `bool` | Inclui itens ocultos quando o Moodle retornar esses dados. |
| `moodleAlias` | `string?` | Alias da conexão Moodle. Quando omitido, usa a conexão padrão. |

## `get_course_module` / `get_course_module`

Descrição:

- Consulta um módulo por `cmid` ou `instanceId`.
- Retorna o mesmo contrato de módulo usado na listagem de conteúdos.
- Não baixa arquivos.

## `list_course_resources` / `list_course_resources`

Descrição:

- Lista recursos de conteúdo: `resource`, `page`, `url`, `book`, `folder` e `label`.

## `list_course_files` / `list_course_files`

Descrição:

- Lista módulos que possuem arquivos retornados pelo Moodle.
- Retorna nome, caminho, tamanho, MIME type e URL sanitizada quando disponíveis.

## `list_course_pages` / `list_course_pages`

Descrição:

- Lista módulos `page` do curso.

## `list_course_urls` / `list_course_urls`

Descrição:

- Lista módulos `url` do curso com links sanitizados.

## `audit_course_structure` / `audit_course_structure`

Descrição:

- Audita a estrutura do curso em modo leitura.
- Aponta seções vazias, módulos sem descrição e módulos sem datas retornadas pelo Moodle.
- Não altera conteúdo, visibilidade ou configuração da sala.

## `list_course_activities` / `list_course_activities`

Descrição:

- Lista atividades do curso a partir de `core_course_get_contents`.
- Inclui `assign`, `quiz`, `scorm` e `forum`.
- Não consulta submissões, tentativas ou notas.
- Retorna visibilidade, disponibilidade e datas retornadas pelo Moodle.
- Sinaliza atividades sem datas e sem prazo de fechamento/entrega.

Parâmetros:

| Nome | Tipo | Descrição |
| --- | --- | --- |
| `courseId` | `string` | Identificador do curso, nome curto ou idnumber. |
| `incluirOcultas` / `includeHidden` | `bool` | Inclui atividades ocultas quando o Moodle retornar esses dados. |
| `moodleAlias` | `string?` | Alias da conexão Moodle. Quando omitido, usa a conexão padrão. |

## `get_course_activity` / `get_course_activity`

Descrição:

- Consulta uma atividade por `cmid` ou `instanceId`.
- Mantém o mesmo contrato de leitura de atividades.
- Não consulta submissões, tentativas ou notas.

## `list_course_assignments` / `list_course_assignments`

Descrição:

- Lista módulos `assign` do curso.
- Não consulta entregas, submissões ou notas.

## `get_assignment` / `get_assignment`

Descrição:

- Consulta uma tarefa por `cmid` ou `instanceId`.
- Não consulta entregas, submissões ou notas.

## `read_forum` / `read_forum`

Descrição:

- Lê discussões e posts de um fórum por `courseId` e `forumId`.
- Resolve `forumId` como `cmid` ou `instanceId` usando `core_course_get_contents`.
- Usa `mod_forum_get_forum_discussions` para listar discussões.
- Usa `mod_forum_get_discussion_posts` para carregar posts quando `incluirPosts` / `includePosts` estiver ativo.
- Retorna texto limpo em `messageText`, sem HTML bruto, e sanitiza URLs de anexos.
- É uma tool somente leitura.

Parâmetros:

| Nome | Tipo | Descrição |
| --- | --- | --- |
| `courseId` | `string` | Identificador do curso, nome curto ou idnumber. |
| `forumId` | `string` | Identificador do fórum. Pode ser `cmid` ou `instanceId`. |
| `pagina` / `page` | `int` | Página de discussões, iniciando em 1. |
| `tamanhoPagina` / `pageSize` | `int` | Tamanho da página de discussões, de 1 a 25. |
| `incluirPosts` / `includePosts` | `bool` | Carrega posts de cada discussão. |
| `postsPorDiscussao` / `postsPerDiscussion` | `int` | Máximo de posts retornados por discussão, de 1 a 100. |
| `ordenarPor` / `sortBy` | `string` | Campo de ordenação das discussões: `id`, `timemodified`, `timestart` ou `timeend`. |
| `ordem` / `sortDirection` | `string` | Direção de ordenação: `ASC` ou `DESC`. |
| `moodleAlias` | `string?` | Alias da conexão Moodle. Quando omitido, usa a conexão padrão. |

## `create_forum_post_preview` / `create_forum_post_preview`

Descrição:

- Prepara uma publicação em fórum Moodle sem escrever imediatamente.
- Resolve `courseId` e `forumId` contra dados autorizados do usuário.
- Sem `discussionId`, cria prévia para nova discussão via `mod_forum_add_discussion`.
- Com `discussionId`, cria prévia para resposta via `mod_forum_add_discussion_post`.
- Se `replyToPostId` não for informado em uma resposta, usa o post inicial da discussão.
- Retorna `pendingActionId`, prévia completa e `confirmationText` literal.

Parâmetros:

| Nome | Tipo | Descrição |
| --- | --- | --- |
| `courseId` | `string` | Identificador do curso, nome curto ou idnumber. |
| `forumId` | `string` | Identificador do fórum. Pode ser `cmid` ou `instanceId`. |
| `assunto` / `subject` | `string` | Assunto da discussão ou resposta. |
| `mensagemHtml` / `messageHtml` | `string` | Mensagem enviada ao Moodle como HTML. |
| `discussionId` | `string?` | Discussão alvo quando for resposta. Omitir para nova discussão. |
| `replyToPostId` | `string?` | Post alvo da resposta. Quando omitido, usa o post inicial da discussão. |
| `groupId` | `int` | Grupo Moodle para nova discussão. `0` usa o padrão do Moodle. |
| `moodleAlias` | `string?` | Alias da conexão Moodle. Quando omitido, usa a conexão padrão. |

## `confirm_forum_post` / `confirm_forum_post`

Descrição:

- Confirma e executa uma publicação pendente em fórum Moodle.
- Exige `pendingActionId` e `confirmationText` literal retornado pela prévia.
- Usa token de escrita, conexão com `CanWrite=true` e escopo `moodle.write`.
- Grava auditoria com a função Moodle executada e o resumo da resposta.
- Não executa novamente uma ação que já esteja confirmada, evitando duplicidade no fórum.

Parâmetros:

| Nome | Tipo | Descrição |
| --- | --- | --- |
| `pendingActionId` | `Guid` | Identificador da ação pendente. |
| `confirmationText` | `string` | Texto literal de confirmação retornado na prévia. |

## `list_course_quizzes` / `list_course_quizzes`

Descrição:

- Lista módulos `quiz` do curso.
- Não consulta tentativas ou notas.

## `get_quiz` / `get_quiz`

Descrição:

- Consulta um quiz por `cmid` ou `instanceId`.
- Não consulta tentativas ou notas.

## `list_course_scorms` / `list_course_scorms`

Descrição:

- Lista módulos `scorm` do curso.
- Não consulta tentativas ou notas.

## `list_activity_deadlines` / `list_activity_deadlines`

Descrição:

- Retorna datas de atividades e prazos inferidos pelos rótulos de datas retornados pelo Moodle.
- Separa `openAt`, `dueAt` e `closeAt` quando o rótulo permite identificar.
- Sinaliza atividades sem datas ou sem prazo de entrega/fechamento.

## `list_assignment_submissions` / `list_assignment_submissions`

Descrição:

- Lista submissões de uma tarefa `assign` por `courseId` e `assignmentId`.
- Aceita `assignmentId` como `cmid` ou `instanceId`; a chamada ao Moodle usa o instance id resolvido.
- Cruza submissões com estudantes ativos para identificar enviados e pendentes.
- Respostas são paginadas e aceitam filtros `status`, `since`, `before`, `includeLate` e `includeUngraded`.
- Não baixa anexos automaticamente.
- Não expõe texto integral da submissão em relatórios coletivos; retorna apenas metadados como `hasOnlineText` e `fileCount`.

Parâmetros:

| Nome | Tipo | Descrição |
| --- | --- | --- |
| `courseId` | `string` | Identificador do curso, nome curto ou idnumber. |
| `assignmentId` | `string` | Identificador da tarefa, como `cmid` ou `instanceId`. |
| `pagina` / `page` | `int` | Página de resultados, iniciando em 1. |
| `tamanhoPagina` / `pageSize` | `int` | Tamanho da página, de 1 a 100. |
| `status` | `string` | `todos`/`all`, `entregues`/`submitted`, `pendentes`/`pending`, `atrasadas`/`late` ou `aguardando_correcao`/`awaiting_grading`. |
| `desde` / `since` | `DateTimeOffset?` | Filtro opcional de submissões modificadas desde a data. |
| `antes` / `before` | `DateTimeOffset?` | Filtro opcional de submissões modificadas antes da data. |
| `incluirAtrasadas` / `includeLate` | `bool` | Quando falso, remove atrasadas de relatórios gerais. |
| `incluirNaoCorrigidas` / `includeUngraded` | `bool` | Quando falso, remove itens aguardando correção de relatórios gerais. |
| `moodleAlias` | `string?` | Alias da conexão Moodle. |

## `get_student_submission` / `get_student_submission`

Descrição:

- Consulta uma submissão individual por `courseId`, `assignmentId` e `studentId`.
- Retorna status, atraso, necessidade de correção, tentativa, datas e presença de arquivos/texto online.
- Não retorna o conteúdo textual integral da entrega nem baixa anexos.

## `list_pending_submissions` / `list_pending_submissions`

Descrição:

- Lista estudantes ativos sem submissão entregue para uma tarefa.
- Usa o mesmo contrato paginado de `list_assignment_submissions`.

## `list_late_submissions` / `list_late_submissions`

Descrição:

- Lista submissões enviadas após o prazo da tarefa retornado pelo Moodle.
- Usa o mesmo contrato paginado de `list_assignment_submissions`.

## `list_submissions_awaiting_grading` / `list_submissions_awaiting_grading`

Descrição:

- Lista submissões enviadas com status de avaliação ainda pendente.
- Usa o mesmo contrato paginado de `list_assignment_submissions`.

## `get_submission_status` / `get_submission_status`

Descrição:

- Alias de consulta individual de status da submissão por estudante.
- Usa o mesmo contrato de `get_student_submission`.

## `discover_grading_functions` / `discover_moodle_grading_functions`

Descricao:

- Consulta `core_webservice_get_site_info` no Moodle atual e classifica as funcoes necessarias para o MVP de correcao assistida.
- Verifica leitura de submissoes, status de submissao, notas, arquivos e escrita de notas individual/lote.
- Nao baixa anexos, nao lista estudantes e nao executa escrita no Moodle.
- Nao retorna token, senha, URL privada com token ou dados pessoais de estudantes.

Parametros:

| Nome | Tipo | Descricao |
| --- | --- | --- |
| `moodleAlias` | `string?` | Alias da conexao Moodle. Quando omitido, usa a conexao padrao. |

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `true` |
| `Destructive` | `false` |
| `Idempotent` | `true` |
| `OpenWorld` | `false` |

## `execute_grading_discovery`

Descricao:

- Consolida a descoberta técnica para correção assistida; a rastreabilidade da antiga Fase 0 está na tabela de migração de `docs/roadmap.md`.
- Consulta o catalogo de funcoes Moodle e classifica disponibilidade de leitura de entregas, leitura de notas, anexos, `mod_assign_save_grade`, `mod_assign_save_grades`, rubricas/escalas e modo de token de escrita.
- Relata `CanWrite`, feature flags de escrita e que a escrita sempre usa o token do usuario do tenant correspondente, sem retornar o valor do token.
- Nao baixa anexos, nao lista estudantes e nao executa escrita no Moodle.
- Campos como permissao real de professor/tutor, acesso efetivo a pluginfile, rubricas e escalas ficam marcados como prova pendente quando dependem de curso/tarefa Moodle real.

Parametros:

| Nome | Tipo | Descricao |
| --- | --- | --- |
| `moodleAlias` | `string?` | Alias da conexao Moodle. Quando omitido, usa a conexao padrao. |

Resposta estruturada:

- `overallStatus`: `blocked` ou `requires_real_moodle_probe`.
- `functions[]`: funcoes Moodle avaliadas e disponibilidade.
- `attachments`, `gradeWrite`, `permissions`, `rubricsAndScales`: area, status, evidencias e proximos passos.
- `writeToken`: modo `user_token` ou `write_service_token`, flags de escrita e permissao da conexao.
- `blockingIssues` e `warnings`: motivos que bloqueiam ou exigem prova em sandbox.

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `true` |
| `Destructive` | `false` |
| `Idempotent` | `true` |
| `OpenWorld` | `false` |

## `create_assisted_grading_batch`

Descricao:

- Cria um job interno em `grading_batch` e itens em `grading_item` a partir das entregas retornadas pelas queries de submissao existentes.
- Quando `includeSubmissionFiles=true`, baixa os anexos retornados pela submissao com `fileUrl`, respeita os limites de arquivo configurados e persiste `GradingArtifact` com o resultado da extracao.
- A extracao suporta textos simples/HTML/JSON/XML/CSV, PDF com texto embutido, DOCX, PPTX, XLSX, OpenDocument e ZIP com arquivos internos suportados. Textos muito grandes usam chunking representativo com trechos distribuídos pelo documento dentro do limite de contexto. PDF escaneado ou composto apenas por imagem retorna status `scanned_pdf` e ainda exige OCR.
- Quando `includeRubric=true` ou `includeCourseMaterials=true`, escaneia a secao da tarefa, persiste candidatos de contexto como `assignment_context` e usa selecao heuristica para escolher o provavel enunciado/material principal.
- O orquestrador inline do MVP processa itens pendentes logo apos a criacao do lote: se houver texto extraido, gera um parecer preliminar revisavel; se nao houver conteudo legivel, marca o item como bloqueado.
- Nota sugerida confiavel continua dependente de rubrica/criterios e escala disponiveis.
- Nao escreve nota ou feedback no Moodle.
- Limita `maxItems` entre 1 e 400 e retorna warnings quando o lote foi truncado ou alguma tarefa nao foi encontrada.

Parametros:

| Nome | Tipo | Descricao |
| --- | --- | --- |
| `courseId` | `string` | Identificador numerico do curso nesta versao inicial. |
| `assignmentIds` | `string[]` | Identificadores numericos das tarefas Moodle. |
| `submissionIds` | `string[]?` | Submissoes especificas a incluir. Quando vazio, usa o filtro de entregas. |
| `maxItems` | `int` | Limite do lote, de 1 a 400. |
| `onlyAwaitingGrading` | `bool` | Quando true, cria itens apenas para entregas aguardando correcao. |
| `moodleAlias` | `string?` | Alias da conexao Moodle. Quando omitido, usa a conexao padrao. |

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `false` |
| `Destructive` | `false` |
| `Idempotent` | `false` |
| `OpenWorld` | `false` |

## `get_grading_batch_status`

Descricao:

- Consulta um lote interno criado por `create_assisted_grading_batch`.
- Retorna contadores do lote e uma pagina de itens sem anexar template de UI.
- Nao consulta o Moodle e nao executa escrita.

Parametros:

| Nome | Tipo | Descricao |
| --- | --- | --- |
| `batchJobId` | `Guid` | Identificador do lote. |
| `pagina` | `int` | Pagina de itens, iniciando em 1. |
| `tamanhoPagina` | `int` | Tamanho da pagina, de 1 a 100. |

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `true` |
| `Destructive` | `false` |
| `Idempotent` | `true` |
| `OpenWorld` | `false` |

## `export_grading_coordination_report`

Descricao:

- Gera um relatorio consolidado de um lote interno de correcao assistida para coordenacao.
- Retorna contadores de processamento, revisao, commit, baixa confianca, medias de confianca/nota, itens que exigem atencao e criterios com lacunas.
- Inclui `reportMarkdown` para exportacao/copiar em atas, acompanhamentos ou documentos internos.
- Nao retorna anexos completos, e-mail de estudante ou feedback completo de submissao.
- Nao consulta nem escreve no Moodle.

Parametros:

| Nome | Tipo | Descricao |
| --- | --- | --- |
| `batchJobId` | `Guid` | Identificador do lote retornado por `create_assisted_grading_batch`. |

Campos principais de resposta:

| Campo | Tipo | Descricao |
| --- | --- | --- |
| `statusCounts` | `object` | Contagem por status interno do item. |
| `reviewStatusCounts` | `object` | Contagem por status de revisao humana. |
| `commitStatusCounts` | `object` | Contagem por status de lancamento Moodle. |
| `attentionItems` | `array` | Ate 25 itens que exigem atencao, com estudante, tarefa, status e motivo resumido. |
| `criteriaNeedingReview` | `array` | Ate 10 criterios com lacunas ou revisao obrigatoria. |
| `reportMarkdown` | `string` | Relatorio em Markdown, sem textos completos de feedback/anexo. |

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `true` |
| `Destructive` | `false` |
| `Idempotent` | `true` |
| `OpenWorld` | `false` |

## `cancel_assisted_grading_batch`

Descricao:

- Cancela um lote interno de correcao assistida ainda nao finalizado.
- Nao escreve no Moodle e nao altera nota/feedback de estudantes.
- Usa o orquestrador de lote para aplicar cancelamento de forma retry-safe por `batchJobId`.

Parametros:

| Nome | Tipo | Descricao |
| --- | --- | --- |
| `batchJobId` | `Guid` | Identificador do lote retornado por `create_assisted_grading_batch`. |

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `false` |
| `Destructive` | `false` |
| `Idempotent` | `true` |
| `OpenWorld` | `false` |

## `get_assisted_grading_item`

Descricao:

- Consulta os dados minimos de um item de correcao assistida salvo em `grading_item`.
- Retorna status, revisao, nota sugerida/final, feedback de rascunho/final e observacoes privadas do rascunho quando existirem.
- Nao retorna anexos completos, e-mail do estudante ou dados sensiveis desnecessarios.
- Nao executa escrita no Moodle.

Parametros:

| Nome | Tipo | Descricao |
| --- | --- | --- |
| `gradingItemId` | `Guid` | Identificador do item retornado por `get_grading_batch_status`. |

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `true` |
| `Destructive` | `false` |
| `Idempotent` | `true` |
| `OpenWorld` | `false` |

## `update_grading_draft`

Descricao:

- Salva a revisao humana de nota, feedback, decisao e observacoes internas em `grading_item`.
- Nao escreve no Moodle; apenas prepara o item para uma futura previa/confirmacao de lancamento.
- Usa `expectedReviewStatus` para evitar sobrescrita acidental. Repetir o mesmo payload apos uma primeira gravacao retorna o mesmo resultado sem nova persistencia.
- Se o item foi revisado com conteudo diferente desde a ultima leitura, a tool retorna erro controlado e orienta consultar novamente.

Parametros:

| Nome | Tipo | Descricao |
| --- | --- | --- |
| `gradingItemId` | `Guid` | Identificador do item de correcao. |
| `finalGrade` | `decimal?` | Nota final revisada. |
| `finalFeedback` | `string` | Feedback final revisado. |
| `teacherDecision` | `string` | Decisao do professor/tutor, por exemplo `approved` ou `needs_changes`. |
| `reviewNotes` | `string?` | Observacoes internas da revisao. |
| `expectedReviewStatus` | `string` | Status visto antes da edicao; use `NotReviewed` para rascunho ainda nao revisado. |

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `false` |
| `Destructive` | `false` |
| `Idempotent` | `true` |
| `OpenWorld` | `false` |

## `create_batch_grade_launch_preview`

Descricao:

- Gera a previa consolidada dos itens revisados e prontos para lancamento.
- Cria uma `PendingMoodleAction` com texto de confirmacao literal e expiracao curta.
- Nao escreve no Moodle; a escrita oficial fica bloqueada ate a chamada de confirmacao.
- Filtra itens sem revisao final, sem nota final ou sem feedback final e retorna avisos.

Parametros:

| Nome | Tipo | Descricao |
| --- | --- | --- |
| `batchJobId` | `Guid` | Identificador do lote de correcao assistida. |
| `gradingItemIds` | `IReadOnlyCollection<Guid>?` | Subconjunto opcional de itens do lote. |
| `onlyReviewed` | `bool` | Quando verdadeiro, inclui somente itens revisados e prontos. |

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `false` |
| `Destructive` | `false` |
| `Idempotent` | `false` |
| `OpenWorld` | `false` |

## `confirm_batch_grade_launch`

Descricao:

- Confirma uma acao pendente criada por `create_batch_grade_launch_preview`.
- Exige o texto literal de confirmacao e o escopo server-side `moodle.write`.
- Exige `Features:AssignmentGradeWriteEnabled=true`; quando houver feedback, exige tambem `Features:AssignmentFeedbackWriteEnabled=true`.
- Bloqueia o envio se `mod_assign_save_grade` nao estiver disponivel no catalogo de funcoes do servico Moodle autorizado.
- Envia cada item por `mod_assign_save_grade` usando `IMoodleAssignmentGradingGateway`.
- Registra `commit_succeeded`, `commit_failed` ou `commit_blocked` em `moodle_audit_logs` por item.
- Repeticoes com o mesmo `pendingActionId` ignoram itens ja marcados como enviados com sucesso.

Parametros:

| Nome | Tipo | Descricao |
| --- | --- | --- |
| `pendingActionId` | `Guid` | Identificador da acao pendente retornada pela previa. |
| `confirmationText` | `string` | Texto exato de confirmacao exigido pela acao pendente. |

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `false` |
| `Destructive` | `true` |
| `Idempotent` | `true` |
| `OpenWorld` | `false` |

## `get_grading_audit`

Descricao:

- Consulta eventos sanitizados de auditoria por `auditId`/correlation id.
- Retorna eventos paginados de criacao/confirmacao/commit vinculados ao mesmo fluxo.
- Nao escreve no Moodle e nao altera estado interno.
- Nesta versao, a consulta por `batchJobId` ainda nao foi implementada.

Parametros:

| Nome | Tipo | Descricao |
| --- | --- | --- |
| `auditId` | `string` | AuditId/correlation id retornado pela confirmacao de lancamento. |
| `pagina` | `int` | Pagina de eventos, iniciando em 1. |
| `tamanhoPagina` | `int` | Tamanho da pagina, de 1 a 100. |

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `true` |
| `Destructive` | `false` |
| `Idempotent` | `true` |
| `OpenWorld` | `false` |

## `prepare_demo_action`

Descrição:

- Cria uma ação pendente de demonstração.
- Não executa escrita real no Moodle.
- Útil para validar o fluxo prepare/confirm.

Parâmetros:

| Nome | Tipo | Descrição |
| --- | --- | --- |
| `message` | `string` | Texto demonstrativo exibido na prévia. |

## `confirm_demo_action`

Descrição:

- Confirma uma ação pendente de demonstração.
- Exige `pendingActionId` e texto exato de confirmação.
- Não executa escrita real no Moodle.

Parâmetros:

| Nome | Tipo | Descrição |
| --- | --- | --- |
| `pendingActionId` | `Guid` | Identificador da ação pendente. |
| `confirmationText` | `string` | Texto exato retornado na preparação. |

## `get_student_completion` / `get_student_completion`

Descricao:

- Consulta o progresso e conclusao de um estudante em um curso usando `core_completion_get_activities_completion_status`.
- Retorna `completed`, `timecompleted` e lista de atividades com `state`, `timecompleted` e `tracking`.
- O campo `state` segue os valores do Moodle: 0 = incompleto, 1 = completo, 2 = aprovado, 3 = reprovado.
- Nao consulta notas, submissoes ou risco.

Parametros:

| Nome | Tipo | Descricao |
| --- | --- | --- |
| `courseId` | `string` | Identificador do curso Moodle. |
| `studentId` | `string` | Identificador do estudante (ID do Moodle). |
| `moodleAlias` | `string?` | Alias da conexao Moodle. Quando omitido, usa a conexao padrao. |

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `true` |
| `Destructive` | `false` |
| `Idempotent` | `true` |
| `OpenWorld` | `false` |

## `get_student_gradebook` / `get_student_gradebook`

Descricao:

- Consulta o boletim (gradebook) de um estudante em um curso usando `gradereport_user_get_grade_items`.
- Retorna itens avaliativos com `itemName`, `itemType`, `itemModule`, `gradeRaw`, `gradeFormatted`, `gradeMin`, `gradeMax`, `percentageFormatted`, `feedback`, `graderId` e datas de submissao/correcao.
- Nao consulta progresso, submissoes ou risco.

Parametros:

| Nome | Tipo | Descricao |
| --- | --- | --- |
| `courseId` | `string` | Identificador do curso Moodle. |
| `studentId` | `string` | Identificador do estudante (ID do Moodle). |
| `moodleAlias` | `string?` | Alias da conexao Moodle. Quando omitido, usa a conexao padrao. |

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `true` |
| `Destructive` | `false` |
| `Idempotent` | `true` |
| `OpenWorld` | `false` |

## `report_students_at_risk` / `report_students_at_risk`

Diagnostico do relatorio:

- Reutiliza o fallback inclusivo de alunos quando o Moodle nao retorna roles, evitando relatorio falsamente vazio.
- Emite warnings quando nenhum participante foi encontrado, nenhum fator de risco foi detectado apos a analise ou notas/conclusao ficaram parcialmente indisponiveis.

Descricao:

- Gera um relatorio cruzando inatividade, notas baixas e progresso pendente para identificar estudantes em risco.
- Analisa ate `maxStudentsToAnalyze` estudantes ativos do curso.
- Classifica cada estudante como risco `Alto`, `Medio` ou `Baixo` com base nos fatores detectados.
- Retorna lista de `StudentRiskReport` com `studentId`, `studentName`, `riskLevel` e `riskFactors`.

Parametros:

| Nome | Tipo | Descricao |
| --- | --- | --- |
| `courseId` | `string` | Identificador do curso Moodle. |
| `maxStudentsToAnalyze` | `int` | Maximo de estudantes a analisar. Padrao: 50. |
| `inactivityThresholdDays` | `int` | Limite de dias de inatividade para risco. Padrao: 7. |
| `minGradePercentage` | `decimal` | Nota minima em % (0-100) para risco. Padrao: 60. |
| `moodleAlias` | `string?` | Alias da conexao Moodle. Quando omitido, usa a conexao padrao. |

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `true` |
| `Destructive` | `false` |
| `Idempotent` | `true` |
| `OpenWorld` | `false` |

## `generate_course_grades_report`

Gera o relatorio estruturado de notas totais do curso por estudante. Reutiliza a mesma query do exportador Excel do frontend e nao soma notas de atividades localmente.

Parametros:

| Nome | Tipo | Descricao |
| --- | --- | --- |
| `courseId` | `string` | Identificador do curso Moodle. |
| `pageSize` | `int` | Tamanho das paginas de leitura de participantes, de 1 a 100. Padrao: 100. |
| `moodleAlias` | `string?` | Alias da conexao Moodle. Quando omitido, usa a conexao padrao. |

O resultado inclui estudantes, nota total, percentual, ultimo acesso, contadores, media e advertencias de cobertura. Para consumo humano ou arquivamento, use `export_course_grades_excel`.

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `true` |
| `Destructive` | `false` |
| `Idempotent` | `true` |
| `OpenWorld` | `false` |

## `export_course_grades_excel`

Gera o mesmo relatorio de notas em um arquivo `.xlsx` formatado e o entrega como recurso binario anexado ao resultado MCP. O resultado estruturado informa nome, tipo, tamanho e resumo do arquivo.

Parametros:

| Nome | Tipo | Descricao |
| --- | --- | --- |
| `courseId` | `string` | Identificador do curso Moodle. |
| `pageSize` | `int` | Tamanho das paginas de leitura de participantes, de 1 a 100. Padrao: 100. |
| `moodleAlias` | `string?` | Alias da conexao Moodle. Quando omitido, usa a conexao padrao. |

O arquivo e gerado sob demanda, sem alterar notas, participantes ou configuracoes do Moodle.

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `true` |
| `Destructive` | `false` |
| `Idempotent` | `true` |
| `OpenWorld` | `false` |

## `list_gradable_submissions`

Descricao:

- Lista entregas corrigiveis de uma ou mais tarefas com contadores e paginacao agregada.
- Usada como passo de preparo antes de `create_assisted_grading_batch`.
- Aceita filtro por status e flag `onlyAwaitingGrading`.
- Nao baixa anexos nem retorna conteudo integral de submissoes.

Parametros:

| Nome | Tipo | Descricao |
| --- | --- | --- |
| `courseId` | `string` | Identificador do curso Moodle. |
| `assignmentIds` | `string[]` | Identificadores das tarefas Moodle. |
| `status` | `string` | Filtro: `all`, `submitted`, `pending`, `late`, `awaiting_grading`. Padrao: `awaiting_grading`. |
| `onlyAwaitingGrading` | `bool` | Quando true, forca filtro apenas para aguardando correcao. |
| `includeLate` | `bool` | Quando false, remove entregas atrasadas da lista. |
| `page` | `int` | Pagina de resultados, iniciando em 1. |
| `perPage` | `int` | Tamanho da pagina, de 1 a 100. Padrao: 25. |
| `moodleAlias` | `string?` | Alias da conexao Moodle. Quando omitido, usa a conexao padrao. |

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `true` |
| `Destructive` | `false` |
| `Idempotent` | `true` |
| `OpenWorld` | `false` |

## `prepare_submission_grading`

Descricao:

- Prepara o contexto de correcao de uma entrega individual fora de um lote.
- Baixa anexos se disponíveis e extrai texto para o pacote de correcao.
- Nao escreve nota ou feedback no Moodle.

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `false` |
| `Destructive` | `false` |
| `Idempotent` | `false` |
| `OpenWorld` | `false` |

## `prepare_ai_grading_batch`

Descricao:

- Prepara o pacote de dados de um lote para consumo por IA externa.
- Retorna itens pendentes com texto extraido, rubrica e instrucoes do professor em formato otimizado para prompt.
- Nao executa analise de IA nem escreve no Moodle.

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `true` |
| `Destructive` | `false` |
| `Idempotent` | `true` |
| `OpenWorld` | `false` |

## `save_ai_grading_batch`

Descricao:

- Salva nota e feedback gerados pela IA como rascunho interno para cada aluno do lote.
- Nao escreve no Moodle.
- Apos salvar, o fluxo obrigatorio e chamar `review_batch_feedbacks` para exibir a interface de revisao humana. Nunca pular a revisao.

Parametros:

| Nome | Tipo | Descricao |
| --- | --- | --- |
| `batchJobId` | `Guid` | Identificador do lote retornado por `create_assisted_grading_batch`. |
| `items` | `AiGradingItemInput[]` | Array de correcoes. Cada item deve conter `gradingItemId`, nota e feedback. |

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `false` |
| `Destructive` | `false` |
| `Idempotent` | `true` |
| `OpenWorld` | `false` |

## `review_batch_feedbacks`

Descricao:

- Retorna a interface de revisao humana dos feedbacks gerados por IA para um lote.
- Deve ser chamada apos `save_ai_grading_batch` e antes de `create_batch_grade_launch_preview`.
- Nao escreve no Moodle.

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `true` |
| `Destructive` | `false` |
| `Idempotent` | `true` |
| `OpenWorld` | `false` |

## `get_grading_item_context`

Descricao:

- Consulta o contexto de correcao de um item individual, incluindo artefatos de assignment_context e selecao heuristica do enunciado.
- Nao retorna anexos completos nem escreve no Moodle.

Parametros:

| Nome | Tipo | Descricao |
| --- | --- | --- |
| `gradingItemId` | `Guid` | Identificador do item retornado pelo status do lote. |

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `true` |
| `Destructive` | `false` |
| `Idempotent` | `true` |
| `OpenWorld` | `false` |

## `get_grading_batch_audit`

Descricao:

- Consulta eventos de auditoria de um lote completo de correcao por `batchJobId`.
- Complementa `get_grading_audit` que busca por `auditId` individual.
- Retorna eventos paginados vinculados ao lote.
- Nao escreve no Moodle.

Parametros:

| Nome | Tipo | Descricao |
| --- | --- | --- |
| `batchJobId` | `Guid` | Identificador do lote de correcao assistida. |
| `pagina` | `int` | Pagina de eventos, iniciando em 1. |
| `tamanhoPagina` | `int` | Tamanho da pagina, de 1 a 100. |

Metadados MCP:

| Campo | Valor |
| --- | --- |
| `ReadOnly` | `true` |
| `Destructive` | `false` |
| `Idempotent` | `true` |
| `OpenWorld` | `false` |

## `grading-review-app`

Descricao:

- MCP Resource (nao tool): expoe a interface SPA de revisao de correcao assistida como HTML servido pelo servidor.
- Acessivel via URI `ui://grading-review/app.html`.
- Nao executa escrita no Moodle e nao requer autenticacao adicional alem do token MCP.

## Planejado

O planejamento canônico está organizado pelas sete jornadas em `docs/roadmap.md`. Este catálogo descreve as tools existentes e não deve ser usado para inferir que um domínio inteiro está ausente ou concluído. As lacunas prioritárias atuais incluem paginação 1-based uniforme, cobertura/truncamento, estados vazios não ambíguos e testes de todos os fluxos Nível A.
