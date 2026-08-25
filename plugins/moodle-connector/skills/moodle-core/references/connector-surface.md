# Connector surface reference

Use esta referencia quando a tarefa envolver mais de uma familia, fallback de capability ou uma nova tool. O `SKILL.md` continua sendo a regra operacional principal.

## Preflight comum

1. Fixar alias e curso/estudante, quando aplicavel.
2. Diagnosticar ou reutilizar snapshot de capabilities.
3. Escolher tool especializada ou `moodle_execute_read`.
4. Declarar cobertura, truncamento, pagina e capabilities ausentes.
5. Encaminhar qualquer escrita para prepare/confirm.

## Matriz de roteamento

| Intencao | Tool/skill principal | Operacoes Moodle comuns | Handoff |
| --- | --- | --- | --- |
| Cursos e estrutura | `moodle-courses` | `core_enrol_get_users_courses`, `core_course_get_courses_by_field`, `core_course_get_contents` | activities, assignments, students |
| Atividades e prazos | `moodle-courses` | `core_course_get_contents`, `mod_assign_get_assignments`, modulos de atividade | assignments, classroom-audit |
| Entregas | `moodle-assignments` | `mod_assign_get_submissions`, `mod_assign_get_submission_status`, `mod_assign_get_grades` | grading, follow-up |
| Estudantes e grupos | `moodle-students` | `core_enrol_get_enrolled_users`, `core_group_get_course_groups`, `core_user_get_users_by_field` | follow-up, messaging |
| Conclusao e acesso | `moodle-follow-up` | `core_completion_get_activities_completion_status`, `core_completion_get_course_completion_status` | reports, messaging |
| Forum | `moodle-forums` | `mod_forum_get_forums_by_courses`, `mod_forum_get_forum_discussions`, `mod_forum_get_discussion_posts` | follow-up, messaging |
| Notas e correcao | `moodle-grading` | `gradereport_user_get_grade_items`, `gradereport_user_get_grades_table`, `mod_assign_get_grades` | assignments, pedagogy |
| Relatorios compostos | `moodle-reports` | leituras de enrollment, gradebook, completion e assignment | follow-up, grading |
| Orientacao pedagogica | `moodle-pedagogy` | tool local `get_pedagogical_guidelines` | grading, forums, messaging, reports |

## Fluxos registrados

O `MoodleBusinessFlowRegistry` pode selecionar estrategias para:

- `listar_cursos_ativos`: timeline ou cursos matriculados como fallback.
- `consultar_curso`: busca por campo ou fallback por cursos matriculados.
- `buscar_cursos`: busca nativa, busca por campo ou fallback matriculado.
- `listar_cursos_categoria`: busca por campo/categoria.
- `listar_entregas_aguardando_correcao`: assignments + submissions.

Use `moodle_list_available_flows` antes de assumir que a estrategia principal existe.

## Estados que nao devem ser confundidos

- `funcao_indisponivel`: o token/conexao nao oferece a funcao.
- `sem_permissao`: a chamada foi recusada no contexto.
- `dado_indisponivel`: a funcao respondeu sem o campo necessario.
- `zero_observado`: o escopo consultado terminou sem registros.
- `falha_parcial`: parte das fontes ou paginas falhou.
- `truncated`/`hasMore`: a resposta nao representa o escopo completo.

Nenhum desses estados autoriza inferir ausencia de aluno, entrega, forum, nota ou atividade sem o escopo correspondente.

## Fronteira portal x Moodle

`list_tasks`, `create_task`, `update_task`, `remove_task`, `list_agenda_events` e tools semelhantes persistem dados locais do portal. Nao use essas tools para afirmar que algo foi criado ou alterado no Moodle.
