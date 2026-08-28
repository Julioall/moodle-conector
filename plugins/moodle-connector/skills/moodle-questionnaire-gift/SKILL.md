---
name: moodle-questionnaire-gift
description: Criar, revisar e validar questionários objetivos alinhados às capacidades da UC e exportá-los em Moodle GIFT.
---

# moodle-questionnaire-gift

Use para criar questionários diagnósticos, formativos, somativos, de revisão ou de recuperação para importação no Moodle.

## Processo

1. Resolva curso, UC, conteúdo e modalidade com 'moodle-course-context'; quando o conteúdo real for necessário, consulte 'list_course_contents', 'list_course_activities', recursos autorizados e 'get_pedagogical_guidelines'.
2. Defina antes da redação uma matriz com capacidade, conhecimento, domínio cognitivo, dificuldade, item, resposta correta, distratores e pontuação.
3. Distribua os itens de modo que o questionário não seja apenas memorização quando a capacidade exigir análise ou aplicação.
4. Escreva enunciados claros, alternativas plausíveis e mutuamente exclusivas. Remova pistas por tamanho, gramática, posição ou repetição textual.
5. Produza feedback da resposta correta e das incorretas sem revelar a alternativa em itens de recuperação antes da tentativa.
6. Calcule a soma das notas e declare escala, número de tentativas, embaralhamento, limite de tempo e política de feedback.
7. Gere a versão legível para revisão docente e a versão GIFT.
8. Valide chaves, pontuação, caracteres reservados, escapes, identificadores únicos e correspondência entre matriz e arquivo. Um GIFT não validado não deve ser entregue como pronto para importação.

## Contrato GIFT mínimo

- Cada questão deve ter identificador único e resposta correta inequívoca.
- Alternativas corretas usam '='; incorretas usam '~'.
- Comentários/feedback devem ser escapados quando contiverem caracteres reservados.
- Não misture sintaxes de múltipla escolha, verdadeiro/falso e resposta curta na mesma questão.
- Não inclua HTML, links ou conteúdo não fornecido sem declarar a origem.
- O arquivo deve conter somente questões e metadados solicitados, sem respostas inventadas.

## Limites pedagógicos

Questões objetivas produzem evidência predominantemente cognitiva. Não trate o questionário como substituto de demonstração prática, observação ou produto quando a capacidade exigir desempenho psicomotor ou profissional.

A skill somente gera o artefato. Ela não importa questões, cria categorias, publica questionário nem altera o Moodle.
