---
name: moodle-grading
description: Ler notas, descobrir capabilities de correcao, gerar notas e feedbacks assistidos e exportar correcoes para CSV sem publicar alteracoes no Moodle.
---

# moodle-grading

Separe leitura, preparacao e exportacao. A decisao pedagogica permanece humana e o Moodle nao e alterado por este fluxo.

## Leitura e descoberta

- Use `get_student_gradebook`, `get_student_activity_grades`, `list_students_below_min_grade`, `gradereport_user_get_grade_items`, `gradereport_user_get_grades_table` e `mod_assign_get_grades` conforme o escopo.
- Use `get_grading_item_context` e `list_all_gradable_submissions` para verificar arquivos, submissao e contexto. `discover_grading_functions` e `execute_grading_discovery` sao diagnosticos tecnicos preservados por nome e no perfil `Full`; use-os somente quando o cliente os oferecer explicitamente ou houver necessidade de suporte.
- Submissao vem de `moodle-assignments`; identidade vem de `moodle-students`; orientacao de avaliacao vem de `moodle-pedagogy`.
- Antes de propor nota, aplique `references/grading-evidence-matrix.md` e mantenha estados de extracao, cobertura e incerteza visiveis.

## Geracao de Correcoes (CSV)

1. Crie o lote limitado com estudantes, tarefas, criterios e valores propostos.
2. Use `prepare_ai_grading_batch` (ou `prepare_submission_grading` para um item) para obter o contexto e gerar nota/feedback no chat.
3. Use `save_ai_grading_batch` para persistir somente os rascunhos locais.
4. Use `export_grading_corrections_csv` para retornar o CSV UTF-8 separado por ponto e virgula com as colunas `nome`, `nota`, `feedback` e `situacao`.
5. Nao utilize `review_batch_feedbacks`, `create_batch_grade_launch_preview`, `confirm_batch_grade_launch` ou qualquer fluxo de envio ao Moodle. O CSV e a saida final deste fluxo.
