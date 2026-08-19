---
name: moodle-grading
description: Ler notas, descobrir capabilities de correcao, preparar revisao assistida, editar rascunhos, gerar previas e confirmar lancamentos de nota com auditoria.
---

# moodle-grading

Separe leitura, preparacao, revisao e escrita. A decisao pedagogica permanece humana.

## Leitura e descoberta

- Use `get_student_gradebook`, `get_student_activity_grades`, `list_students_below_min_grade`, `gradereport_user_get_grade_items`, `gradereport_user_get_grades_table` e `mod_assign_get_grades` conforme o escopo.
- Use `discover_grading_functions`, `execute_grading_discovery`, `get_grading_item_context` e `list_gradable_submissions` para verificar funcoes, arquivos, submissao e contexto.
- Submissao vem de `moodle-assignments`; identidade vem de `moodle-students`; orientacao de avaliacao vem de `moodle-pedagogy`.
- Antes de propor nota, aplique `references/grading-evidence-matrix.md` e mantenha estados de extracao, cobertura e incerteza visiveis.

## Correcao assistida

1. Crie o lote limitado com estudantes, tarefas, criterios e valores propostos.
2. Use `prepare_ai_grading_batch`/`save_ai_grading_batch`, `update_grading_draft` ou `update_grading_drafts_batch` apenas para o estado de revisao definido pelo produto.
3. Revise com `review_batch_feedbacks`, `get_batch_grading_ui_state`, `get_assisted_grading_item` e os auditores do lote.
4. Exporte `export_grading_coordination_report` quando necessario.

## Escrita Moodle

Use `create_batch_grade_launch_preview` ou o fluxo individual `prepare_individual_grade_launch`, depois confirme com `confirm_batch_grade_launch` ou `confirm_individual_grade_launch`. A confirmacao exige pending action vigente, hash/contagem/escopo da previa, texto literal, mesmo usuario/conexao, `CanWrite`, escopo `moodle.write`, capability `mod_assign_save_grade`, feature flag e auditoria.

Nunca execute `mod_assign_save_grade` por `moodle_execute_read`, nunca substitua revisao humana por decisao do modelo e reporte sucesso parcial/falha por item sem declarar lote completo.
