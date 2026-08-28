---
name: moodle-course-closure
description: Auditar notas, entregas, recuperação e evidências antes do fechamento de uma unidade curricular Moodle.
---

# moodle-course-closure

Use como auditoria final de uma UC. A skill prepara um relatório de fechamento; não altera notas, atividades, prazos ou matrícula.

## Sequência

1. Resolva conexão, curso, UC, período e atividades com 'moodle-course-context'.
2. Execute 'moodle-grade-audit' para cada atividade avaliativa e registre o denominador.
3. Verifique atividades de aprendizagem, presença, avaliativas e recuperação com 'audit_course_structure', 'list_course_activities', 'list_course_assignments', 'list_course_quizzes' e 'list_activity_deadlines'.
4. Cruze pendências de correção, notas e feedbacks com 'list_submissions_awaiting_grading', 'list_students_with_pending_submissions', 'get_grading_audit' e relatórios de notas quando aplicável.
5. Verifique conclusão, participação e acessos somente como evidências complementares; não use um único sinal para classificar situação acadêmica.
6. Identifique estudantes sem evidência suficiente, abaixo do mínimo, em recuperação, com execução desconhecida ou com dados incompletos.
7. Gere relatório separado em: regular, crítico, pendente, alerta, não verificável e ação recomendada.
8. Antes de declarar a UC fechada, confirme que não existem páginas truncadas, capabilities ausentes, lotes não reconciliados ou notas sem justificativa.

## Critérios mínimos

- toda nota tem atividade, máximo e estudante rastreáveis;
- a soma/escala fecha conforme a configuração encontrada;
- entregas e ausências não foram confundidas;
- feedback está presente quando exigido pelo fluxo;
- recuperação está separada da avaliação original;
- prazo e timezone foram interpretados de forma consistente;
- evidências de presença não foram convertidas em desempenho;
- limitações e exclusões estão explícitas.

## Saída

Entregue resumo executivo, matriz por atividade, pendências por estudante/categoria, riscos e recomendação operacional. “Pronta para fechamento” significa apenas que o escopo auditado não revelou pendências; não é uma decisão acadêmica oficial.

Esta skill é somente leitura. Mensagens, correções e lançamentos devem ser preparados e confirmados em suas skills próprias.
