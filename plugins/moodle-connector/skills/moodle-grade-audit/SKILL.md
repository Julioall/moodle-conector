---
name: moodle-grade-audit
description: Auditar notas, entregas pendentes, feedbacks, pontuação e recuperação de uma turma Moodle sem alterar o livro de notas.
---

# moodle-grade-audit

Use antes do fechamento de uma atividade ou unidade curricular, ou quando o usuário perguntar quem está sem nota, precisa de correção ou pode precisar de recuperação.

## Sequência

1. Resolva conexão, curso, UC e atividades com `moodle-course-context`.
2. Recolha a orientação pertinente com `get_pedagogical_guidelines` quando a conclusão envolver avaliação, recuperação ou comunicação.
3. Consulte `get_student_gradebook`, `get_student_activity_grades` ou `generate_course_grades_report` conforme o escopo.
4. Cruze o resultado com `list_assignment_submissions`, `list_submissions_awaiting_grading`, `list_students_with_pending_submissions` e `list_late_submissions`.
5. Use `list_students_below_min_grade` somente com média mínima e denominador explicitados.
6. Quando houver correção assistida, compare o estado remoto com `get_grading_batch_audit`, `get_grading_audit` e a prévia do lote.
7. Separe ausência, entrega pendente, nota ausente, nota abaixo do mínimo, feedback ausente, nota acima do máximo, atividade de presença, recuperação e falha técnica.
8. Informe fonte, horário, conexão, curso, atividade, paginação, capability ausente e qualquer dado parcial.

## Classificação do resultado

- **Crítico:** inconsistência que pode alterar o fechamento, como nota acima do máximo, divergência entre prévia e Moodle ou lançamento não reconciliado.
- **Pendente:** ação objetiva ainda necessária, como entrega aguardando correção ou nota sem feedback.
- **Alerta:** evidência incompleta, prazo vencido, atividade sem data, ausência de roster completo ou capability indisponível.
- **Regular:** item conferido dentro do escopo e sem divergência encontrada.

## Regras

- “Sem nota” não significa automaticamente “não entregou”.
- “Não entregou” só pode ser afirmado após exaurir estudantes, páginas, filtros e status aplicáveis.
- Não use nota zero como substituto para ausência ou falha de leitura.
- Não conclua aprovação, reprovação, evasão ou necessidade oficial de recuperação apenas por um indicador.
- Declare o denominador e as exclusões, incluindo estudantes suspensos quando essa informação estiver disponível.
- Esta skill é somente leitura e não corrige, lança notas, publica atividades, envia mensagens ou fecha a UC.
