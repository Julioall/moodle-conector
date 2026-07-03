# Classificacao resiliente de participantes Moodle

## Objetivo

Impedir que alunos sejam omitidos quando o Moodle não retornar papeis (`roles`) para os participantes do curso. A classificação deve priorizar cobertura: participantes sem papel conhecido permanecem elegiveis nos fluxos de aluno, acompanhados de diagnostico explicito sobre a incerteza.

## Causa raiz

O gateway chama `core_enrol_get_enrolled_users` com a opção `userfields`, mas não solicita `roles` nem `groups`. Em seguida, quando `studentsOnly` e verdadeiro, aceita somente participantes cujo array `roles` contem um papel reconhecido como aluno. Se o Moodle devolve `roles: []`, todos os participantes são descartados.

O relatorio de risco recebe apenas o resultado desse filtro. Ele não sabe quantos participantes existiam antes da classificação nem por que a lista ficou vazia, portanto responde com sucesso, dados vazios e nenhum warning.

## Decisão de produto

Em caso de classificação incerta, não omitir possíveis alunos. Falsos positivos controlados são preferíveis a falsos negativos silenciosos. Toda ampliação por fallback deve ser transparente no retorno.

## Arquitetura proposta

### Aquisição de participantes

O gateway continuara usando `core_enrol_get_enrolled_users`, solicitando explicitamente os campos `roles` e `groups` junto aos campos atualmente usados. Esses campos são opcionais no contrato do Moodle e ainda podem vir vazios por permissões ou configuração.

Não serão adicionadas chamadas por participante nesta correção. Isso evita aumento proporcional de latência e risco de rate limit.

### Classificação

A classificação centralizada aplicara estas regras quando `studentsOnly` for `true`:

1. Participante com papel reconhecido como aluno e incluido.
2. Participante sem qualquer papel retornado e incluido por fallback incerto.
3. Participante com ao menos um papel conhecido exclusivamente como perfil de equipe e excluido.
4. Participante com papel não reconhecido e incluido por fallback incerto.
5. Participante com papeis mistos, incluindo papel de aluno, e incluido.

Os nomes reconhecidos devem ser comparados sem diferenciar maiusculas, minusculas ou acentos quando aplicavel. Papeis de aluno incluem inicialmente `student`, `estudante` e `aluno`. Papeis conhecidos de equipe incluem inicialmente `teacher`, `editingteacher`, `instructor`, `tutor`, `coordinator`, `professor`, `instrutor` e `coordenador`.

A exclusão de equipe deve ser conservadora: somente participantes cujos papeis retornados sejam todos reconhecidos como equipe podem ser descartados. Qualquer papel desconhecido preserva o participante.

### Diagnostico

O resultado paginado de participantes passara a carregar diagnostico de classificação suficiente para as camadas superiores produzirem warnings, incluindo:

- quantidade avaliada antes do filtro de aluno;
- quantidade incluida por papel de aluno;
- quantidade incluida por fallback incerto;
- quantidade excluida como equipe conhecida;
- presença de papeis vazios;
- presença de grupos vazios;
- modo de classificação usado: por papel, misto ou fallback.

Esse diagnostico descreve apenas os participantes processados para formar a pagina solicitada. A paginação e o indicador `hasMore` devem continuar corretos após o fallback.

### Warnings das tools

`listar_alunos_curso` e `list_course_students` devem incluir warning quando houver qualquer inclusão incerta. A mensagem deve informar que não foi possível identificar todos os alunos por papel e quantos participantes foram incluidos por fallback.

As listagens de participantes devem avisar quando todos os participantes retornarem sem papeis ou sem grupos. Grupos vazios não devem ser tratados como erro, pois um curso pode legitimamente não usar grupos.

Retornos vazios devem ser diferenciados:

- nenhum participante encontrado: warning informativo;
- participantes encontrados, mas nenhum aluno identificado: warning de classificação;
- resultado vazio por pagina além do intervalo: warning de paginação, sem afirmar que o curso não possui participantes.

O envelope permanece com `status: ok` para consultas executadas com sucesso, mas `warnings` não pode ficar vazio quando o resultado vazio ou degradado for diagnosticavel.

### Relatorio de risco

O relatorio usa a mesma classificação centralizada. Participantes incluidos por fallback são analisados normalmente, evitando o falso vazio.

O retorno da query de risco passara a incluir os relatorios e o diagnostico recebido da listagem. A tool deve emitir warning semelhante a:

> Não foi possível identificar todos os alunos por role. Foram encontrados participantes sem classificação confiável. O relatório foi gerado incluindo esses participantes por fallback.

Se nenhum participante existir, a tool deve avisar que o curso não retornou participantes. Se participantes existirem mas nenhum item de risco for produzido, deve informar que participantes foram analisados e nenhum fator configurado foi detectado, em vez de deixar o vazio sem explicação.

## Grupos

Adicionar `groups` a `userfields` e preservar o endpoint existente `core_group_get_course_groups`. Se os grupos individuais continuarem vazios, a listagem emitira warning; esta correção não fará associação complementar entre grupos e membros, pois isso exigiria chamadas adicionais e uma estratégia própria de paginação e permissões.

## Compatibilidade

Os nomes das tools, argumentos e campos existentes serão preservados. Novos campos de diagnostico serão adicionitivos. Os consumidores que ignoram campos desconhecidos continuarão funcionando.

Fluxos pedagógicos que já usam `studentsOnly: true` receberão automaticamente o fallback inclusivo. Nesta entrega, warnings detalhados serão expostos nas tools de participantes e risco; outras tools manterão seus contratos atuais, mas deixarão de receber listas falsamente vazias.

## Tratamento de erros

Falhas HTTP, autenticação e autorização continuam sendo erros. Ausência de `roles` ou `groups` e um estado degradado, não uma exceção.

Exceções de nota ou conclusão por participante continuam sem abortar o relatório de risco, mas o resultado deve registrar warning agregado quando essas fontes não puderem ser consultadas, sem expor detalhes sensíveis.

## Testes

Os testes serão escritos antes da implementação e devem cobrir:

- requisição de `roles` e `groups` em `userfields`;
- aluno reconhecido por papel;
- participante sem roles incluido por fallback;
- papel desconhecido incluido por fallback;
- perfil exclusivamente de equipe excluido;
- papel misto com aluno incluido;
- paginação correta com participantes incluidos por fallback;
- warning em lista vazia e classificação degradada;
- grupos vazios gerando warning sem erro;
- relatorio de risco analisando participantes sem roles;
- relatorio vazio distinguindo ausência de participantes de ausência de fatores de risco;
- preservação dos contratos bilingues das tools.

## Fora de escopo

- Criar ou alterar papeis no Moodle.
- Consultar roles ou grupos individualmente para cada participante.
- Inferir aluno por nome, e-mail ou outros dados pessoais.
- Introduzir uma capability universal de aluno.
- Refatorar relatórios pedagógicos não relacionados à classificação.

## Critérios de aceite

- Um curso com 37 participantes e `roles: []` não retorna zero alunos apenas por ausência de roles.
- O relatorio de risco não retorna falso vazio causado pelo filtro de papeis.
- Todo fallback de classificação fica explicito em `warnings`.
- `roles` e `groups` são solicitados ao Moodle e preservados quando disponíveis.
- Perfis de equipe conhecidos são excluidos apenas quando a classificação e inequívoca.
- A suíte de testes existente e os novos testes de regressão passam.
