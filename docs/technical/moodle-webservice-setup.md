# Configuração do Webservice Moodle

Este documento lista o que o conector espera do Moodle para funcionar. A configuração exata no Moodle pode variar conforme a versão e as permissões da instituição.

## Versão Moodle alvo

O conector foi desenvolvido e validado contra o **Moodle 5.0.1** (lançado em 2025). Todas as funções Web Service listadas abaixo são compatíveis com essa versão.

Versões anteriores do Moodle (4.x) podem funcionar para a maioria das funções, mas não são oficialmente suportadas. Versões futuras (5.1+) devem ser verificadas quanto a possíveis deprecações, especialmente na API de conclusão de atividades.

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

### Leitura — Core

| Função Moodle | Uso no conector | Status |
| --- | --- | --- |
| `core_webservice_get_site_info` | Resolver o `userid` do token atual e listar funções disponíveis no serviço. | Implementado |
| `core_enrol_get_users_courses` | Listar cursos do usuário Moodle atual. | Implementado |
| `core_enrol_get_enrolled_users` | Listar participantes, estudantes e membros de grupo de cursos autorizados. | Implementado |
| `core_group_get_course_groups` | Listar grupos de cursos autorizados. | Implementado |
| `core_course_get_contents` | Ler seções, módulos, recursos, atividades, datas e metadados de arquivos do curso. | Implementado |
| `core_completion_get_activities_completion_status` | Ler status de conclusão das atividades de um aluno em um curso. | Implementado |
| `core_completion_get_course_completion_status` | Ler status de conclusão geral de um aluno em um curso. | Implementado |

> **Nota sobre conclusão:** A API de conclusão de atividades (`core_completion_get_activities_completion_status`) foi reformulada no Moodle 5.0 com a nova página "Activity Overview". A função ainda funciona no Moodle 5.0.1, mas pode ser deprecada em versões futuras. O comportamento atual pode degradar alguns erros para dados vazios; isso é ambíguo e está no backlog P0 para distinguir `nao_configurado`, `sem_permissao`, `funcao_indisponivel` e `falha_parcial` de `zero_observado`.

### Leitura — Fóruns

| Função Moodle | Uso no conector | Status |
| --- | --- | --- |
| `mod_forum_get_forums_by_courses` | Listar fóruns de um curso autorizado. | Implementado |
| `mod_forum_get_forum_discussions` | Ler discussões paginadas de um fórum autorizado. | Implementado |
| `mod_forum_get_discussion_posts` | Ler posts de uma discussão de fórum autorizada. | Implementado |

> **Nota:** A função `mod_forum_get_forum_discussions_paginated` foi deprecada e removida no Moodle 5.0. O conector consulta as funções anunciadas por `core_webservice_get_site_info`, prioriza `mod_forum_get_forum_discussions` e só usa a variante paginada quando ela for a única disponível na conexão.

### Leitura — Tarefas e submissões

| Função Moodle | Uso no conector | Status |
| --- | --- | --- |
| `mod_assign_get_assignments` | Ler configurações de tarefas dos cursos listados, incluindo nota máxima e identificação por cmid/instanceId. | Implementado |
| `mod_assign_get_submissions` | Ler submissões de tarefas para compor resumo de pendências, atrasos, tentativas e correção pendente, sem baixar anexos. | Implementado |
| `mod_assign_get_submission_status` | Ler status detalhado de submissão de um aluno, incluindo tentativa atual e feedback existente. | Implementado |
| `mod_assign_get_grades` | Ler notas existentes de uma tarefa para verificar se o aluno já foi avaliado. | Implementado |

### Leitura — Notas e gradebook

| Função Moodle | Uso no conector | Status |
| --- | --- | --- |
| `gradereport_user_get_grade_items` | Ler itens avaliativos e notas do gradebook do curso por aluno. | Implementado |

### Escrita

| Função Moodle | Uso no conector | Status |
| --- | --- | --- |
| `mod_forum_add_discussion` | Criar nova discussão em fórum autorizado após confirmação humana. | Implementado |
| `mod_forum_add_discussion_post` | Responder a um post de discussão em fórum autorizado após confirmação humana. | Implementado |
| `core_message_send_instant_messages` | Enviar mensagens instantâneas individuais após prévia e confirmação humana. Não oferece broadcast nativo nem agendamento. | Implementado; bloqueado por padrão por feature flag |
| `mod_assign_save_grade` | Lançar nota e feedback individual em tarefa autorizada após confirmação humana. | Implementado |

## Permissões necessárias

O usuário Moodle usado pela conexão precisa ter permissão suficiente para executar as funções implementadas no contexto dos cursos relevantes. A presença de uma função no catálogo retornado por `core_webservice_get_site_info` não prova permissão contextual sobre um curso, grupo, fórum, tarefa ou estudante; a chamada no recurso autorizado ainda precisa ser validada e pode retornar `sem_permissao`.

Para as próximas tools de leitura acadêmica, será necessário validar permissões por função Moodle, sem depender do nome do papel institucional.

## Escrita no Moodle

As tools reais de escrita implementadas hoje são:

- publicação em fórum por `mod_forum_add_discussion`;
- resposta em fórum por `mod_forum_add_discussion_post`;
- mensagens instantâneas individuais por `core_message_send_instant_messages`;
- lançamento individual de nota/feedback por `mod_assign_save_grade`.

Qualquer escrita deve seguir o fluxo:

1. preparar ação;
2. exibir prévia;
3. exigir confirmação humana;
4. executar somente após confirmação válida.

Além das permissões Moodle da função, a conexão cadastrada precisa estar com `CanWrite=true`, o chamador precisa ter o escopo aplicável e a feature flag do domínio precisa estar ativa. O `appsettings.json` versionado mantém as flags de escrita desabilitadas por padrão; cada ambiente deve habilitá-las explicitamente após revisão administrativa. As flags bloqueiam preparação/confirmação quando definidas como `false`.

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
