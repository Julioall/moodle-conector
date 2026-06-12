# Boas Práticas de Privacidade

## Dados sensíveis

O conector pode lidar com:

- nomes de cursos;
- dados acadêmicos;
- notas;
- prazos;
- entregas;
- identificadores de usuário;
- credenciais Moodle protegidas.

## Não registrar

Nunca registrar em logs, issues, prints ou documentação:

- senha Moodle;
- token Moodle;
- JWT completo;
- API key;
- client secret;
- connection string com senha;
- dados pessoais de alunos sem necessidade operacional.

## Uso por professores e tutores

- Consulte apenas cursos sob sua responsabilidade.
- Não compartilhe respostas contendo dados acadêmicos em canais não autorizados.
- Revise mensagens e feedbacks antes de confirmar qualquer ação futura de escrita.

## Uso por suporte/DevOps

- Ao diagnosticar problemas, use correlation id e códigos de erro.
- Prefira logs sanitizados.
- Não solicite credenciais reais de professores.
- Rotacione API keys em caso de exposição.

## Uso por coordenação

- Defina claramente quais perfis podem acessar dados agregados.
- Restrinja acesso a dados de alunos conforme política institucional.
- Evite decisões automáticas baseadas apenas em indicadores gerados pelo conector.
