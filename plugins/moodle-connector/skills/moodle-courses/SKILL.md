---
name: moodle-courses
description: Listar, buscar, resolver e inspecionar cursos Moodle, incluindo estrutura, conteudos, atividades, recursos e prazos, respeitando conexao e capabilities.
---

# moodle-courses

Use para localizar cursos e entender sua estrutura. Comece sempre por `moodle-core`.

## Roteamento

- Cursos do usuario: `list_my_courses`.
- Busca/resolucao por id, shortname, idnumber, nome ou categoria: `search_courses`, `get_course`, ou os formatos MCP `search`/`fetch`.
- Secoes e modulos: `list_course_contents`, `get_course_module` e `audit_course_structure`.
- Inventario de atividades e prazos: `list_course_activities`, `get_course_activity` e `list_activity_deadlines`.
- Tarefas, entregas, estudantes, notas, risco e mensagens pertencem as skills de dominio correspondentes.

## Operacoes Moodle

As operacoes canonicas mais comuns sao `core_enrol_get_users_courses`, `core_course_get_courses_by_field` e `core_course_get_contents`. Outras leituras podem usar `core_course_search_courses`, `core_course_get_course_module`, `core_course_get_course_module_by_instance` e funcoes de recursos/atividades registradas.

Quando uma rota possuir alternativas, consulte `moodle_list_available_flows` e use a estrategia selecionada. Nao troque silenciosamente um curso explicito por outro curso do usuario.

## Identificadores e evidencias

Preserve `courseId`, `shortName`, `idnumber`, `cmid` e `instanceId` conforme recebidos. `cmid` nao e automaticamente `instanceId`. Sanitize URLs e nao baixe arquivos apenas para listar estrutura.

Resultado vazio, funcao ausente, permissao negada e pagina truncada sao estados diferentes. Informe a conexao, escopo, paginacao e timestamp quando isso afetar a conclusao.
