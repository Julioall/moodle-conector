# Configuração do Webservice Moodle

Este documento lista o que o conector espera do Moodle para funcionar. A configuração exata no Moodle pode variar conforme a versão e as permissões da instituição.

## Estado implementado

O conector usa credenciais Moodle cadastradas no portal ou pelo endpoint administrativo para obter um token em:

```text
<MOODLE_BASE_URL>/login/token.php
```

O serviço usado é configurado por:

```text
MoodleApi__LoginService=moodle_mobile_app
```

## Funções Moodle usadas hoje

| Função Moodle | Uso no conector | Status |
| --- | --- | --- |
| `core_webservice_get_site_info` | Resolver o `userid` do token atual. | Implementado |
| `core_enrol_get_users_courses` | Listar cursos do usuário Moodle atual. | Implementado |
| `core_enrol_get_enrolled_users` | Listar participantes, estudantes e membros de grupo de cursos autorizados. | Implementado |
| `core_group_get_course_groups` | Listar grupos de cursos autorizados. | Implementado |
| `core_course_get_contents` | Ler seções, módulos, recursos, atividades, datas e metadados de arquivos do curso. | Implementado |
| `mod_forum_get_forum_discussions_paginated` | Ler discussões paginadas de um fórum autorizado. | Implementado |
| `mod_forum_get_discussion_posts` | Ler posts de uma discussão de fórum autorizada. | Implementado |
| `mod_forum_add_discussion` | Criar nova discussão em fórum autorizado após confirmação humana. | Implementado |
| `mod_forum_add_discussion_post` | Responder a um post de discussão em fórum autorizado após confirmação humana. | Implementado |
| `mod_assign_get_assignments` | Ler atividades dos cursos listados. | Planejado |
| `mod_assign_get_submissions` | Ler submissões de tarefas para compor resumo de pendências, atrasos, tentativas e correção pendente, sem baixar anexos. | Implementado |
| `gradereport_user_get_grade_items` | Ler nota/resumo do curso. | Planejado |

## Permissões necessárias

O usuário Moodle usado pela conexão precisa ter permissão suficiente para executar as funções implementadas no contexto dos cursos relevantes.

Para as próximas tools de leitura acadêmica, será necessário validar permissões por função Moodle, sem depender do nome do papel institucional.

## Escrita no Moodle

As tools reais de escrita implementadas hoje são:

- publicação em fórum por `mod_forum_add_discussion`;
- resposta em fórum por `mod_forum_add_discussion_post`;
- lançamento individual de nota/feedback por `mod_assign_save_grade`.

Qualquer escrita deve seguir o fluxo:

1. preparar ação;
2. exibir prévia;
3. exigir confirmação humana;
4. executar somente após confirmação válida.

Além das permissões Moodle da função, a conexão cadastrada precisa estar com `CanWrite=true` e o chamador precisa ter escopo `moodle.write` para confirmar ações pendentes.

## Cadastro de conexão Moodle

O portal cadastra credenciais Moodle por usuário/conector e armazena:

- alias da conexão;
- URL base do Moodle;
- usuário Moodle criptografado;
- senha Moodle criptografada;
- flag de conexão padrão;
- permissão `CanWrite`.

O endpoint administrativo `/admin/connector-clients/register` também registra ou rotaciona credenciais de um cliente conector.

## Boas práticas

- Use contas Moodle com o menor privilégio necessário.
- Não compartilhe uma conta administrativa para uso cotidiano do conector.
- Revogue tokens/credenciais se uma API key for exposta.
- Separe aliases por ambiente, por exemplo `goias`, `nacional`, `ctm` ou `default`.
