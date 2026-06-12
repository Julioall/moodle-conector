# Guia para Professores e Tutores

## Objetivo

Este guia descreve o uso funcional atual do conector para professores e tutores.

## O que está disponível hoje

Tools disponíveis:

- `listar_meus_cursos`
- `list_courses`
- `buscar_cursos`
- `search_courses`
- `consultar_curso`
- `get_course`

Essas tools listam cursos vinculados ao usuário autenticado no Moodle.

## O que a tool retorna

Para cada curso, o conector retorna hoje:

- identificador do curso;
- nome curto;
- nome completo;
- nome de exibição;
- categoria;
- datas de início e fim, quando disponíveis;
- visibilidade;
- URL do curso;
- imagem do curso, quando disponível;
- progresso e favorito, quando o Moodle retornar esses dados;
- último acesso ao curso, quando disponível.

Notas, prazos, pendências, tentativas e dados de alunos devem ficar em tools específicas. Isso evita que a listagem inicial fique lenta em ambientes com muitos cursos e atividades.

## Como pedir no ChatGPT

Exemplos:

```text
Liste meus cursos no Moodle.
```

```text
Liste até 10 cursos da conexão goias.
```

```text
Liste meus cursos.
```

```text
Busque meus cursos com o termo segurança.
```

```text
Consulte o curso CURSO-001.
```

## Limites atuais

- A tool atual é uma listagem leve e usa cache curto por conexão/usuário.
- Leitura específica de turmas e entregas pendentes já está disponível; alunos em risco continuam planejados para fase posterior.
- Escritas reais, mensagens, feedback e notas ainda não estão implementados.

## Segurança

- O conector usa a conta Moodle vinculada ao usuário ou à API key.
- Não compartilhe API key, senha Moodle ou tokens.
- Se aparecer erro de vínculo Moodle, conecte a conta Moodle no portal.
