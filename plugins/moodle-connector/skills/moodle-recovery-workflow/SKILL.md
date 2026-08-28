---
name: moodle-recovery-workflow
description: Identificar evidências de recuperação e preparar um plano Moodle seguro sem publicar ou lançar notas automaticamente.
---

# moodle-recovery-workflow

Use quando o usuário pedir para identificar estudantes em recuperação, criar a proposta correspondente ou organizar a sequência de recuperação de uma UC.

## Sequência

1. Resolva conexão, curso, UC e atividades com 'moodle-course-context'.
2. Audite notas e entregas com 'moodle-grade-audit'.
3. Separe estudantes sem entrega, com arquivo inválido, com entrega aguardando correção e com desempenho insuficiente.
4. Confirme as capacidades não demonstradas usando atividade, critérios, evidências de correção e orientação pedagógica.
5. Encaminhe a criação para 'moodle-recovery-design'.
6. Se houver questionário, encaminhe para 'moodle-questionnaire-gift' e faça a correspondência entre capacidades, itens e critérios.
7. Monte a prévia operacional contendo curso, seção, atividade, estudantes, datas, nota máxima, formato, recursos, alterações e riscos.
8. Só encaminhe para 'moodle-questionnaire-publishing' ou outro fluxo de escrita depois que o usuário selecionar explicitamente o que será publicado e confirmar a prévia.

## Regras de decisão

- Não transforme ausência em reprovação ou nota zero.
- Não inclua estudante cuja elegibilidade não esteja sustentada pelo escopo e pelo denominador informados.
- Não replique a atividade original trocando apenas nomes, empresas, valores ou personagens.
- Uma recuperação deve permitir nova aprendizagem e nova evidência de desempenho.
- Informe estudantes excluídos por falta de dados, capability, correção ou contexto.
- Datas sugeridas são propostas; não altere prazos sem confirmação.

## Saída

Apresente:

- diagnóstico por categoria;
- capacidades a recuperar;
- proposta pedagógica;
- questionário/GIFT quando aplicável;
- lista de alterações pendentes;
- prévia de publicação, sem execução;
- confirmação explícita necessária para cada escrita.

Esta skill não cria, edita ou remove objetos Moodle por conta própria.
