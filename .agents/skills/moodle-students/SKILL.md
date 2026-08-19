---
name: moodle-students
description: Resolver participantes, estudantes, matriculas, grupos, identidade e sinais de acesso no Moodle sem alterar membros ou preferencias.
---

# moodle-students

Use para roster, estudantes, grupos, matricula, acesso recente e resolucao de identidade.

## Roteamento

- Participantes: `list_course_participants`/`list_course_students` sobre `core_enrol_get_enrolled_users`.
- Grupos: `list_course_groups` e `get_group_members` sobre `core_group_get_course_groups` e fluxos de grupo.
- Busca de usuario: `core_enrol_search_users`, `core_user_get_users_by_field` ou `core_user_get_course_user_profiles`, se disponiveis.
- Acesso recente: `list_students_without_recent_access`; o resultado depende do campo de ultimo acesso retornado pelo Moodle.
- Entrega, nota, risco, follow-up e mensagem devem ser encaminhados as skills de dominio.

## Identidade

Resolva a conexao antes de consultar e mantenha o Moodle `userid` como identificador autoritativo. Se nome, email ou identificador curto tiver mais de um resultado, retorne a ambiguidade e peca refinamento. Nunca reutilize um `userid` de outra conexao.

## Completude e classificacao

Resultados de roster/grupos podem paginar e campos como roles, groups e lastaccess podem faltar. Diferencie `zero_observado`, `dado_indisponivel`, `funcao_indisponivel`, `sem_permissao` e `falha_parcial`. Nao trate ausencia de papel ou acesso como prova de que o aluno nao existe ou nao esta matriculado.

Nao altere matriculas, grupos ou preferencias por esta skill; escritas devem seguir fluxo controlado especifico.
