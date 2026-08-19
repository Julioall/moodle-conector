# Matriz de evidencia para correcao

Use esta matriz antes de propor ou lancar uma nota. Ela separa o que foi observado do que ainda exige revisao humana.

| Pergunta | Fonte preferencial | Bloqueio ou limite |
| --- | --- | --- |
| Existe uma entrega e ela pertence ao estudante correto? | `list_gradable_submissions`, `get_grading_item_context`, identidade via `moodle-students` | Identidade ambigua, item fora do escopo ou pagina truncada: nao corrigir automaticamente. |
| O arquivo foi lido integralmente? | `prepare_submission_grading` e status de extracao | `failed`, `unsupported_format`, `file_too_large`, `empty` ou `scanned_pdf` sem OCR: marcar revisao manual. |
| Ha criterio, rubrica e valor maximo verificaveis? | `get_grading_item_context`, `get_pedagogical_guidelines` | Sem criterio aplicavel, gerar observacao/rascunho de baixa confianca; nao inventar rubrica. |
| A proposta esta pronta para revisao? | `prepare_ai_grading_batch`, `save_ai_grading_batch`, `get_assisted_grading_item` | Rascunho nao e nota publicada. Registrar incertezas e evidencias por criterio. |
| A escrita esta autorizada? | `create_batch_grade_launch_preview` ou `prepare_individual_grade_launch` | So confirmar com pending action vigente, hash/escopo preservados, `CanWrite`, capability, feature flag e auditoria. |
| O resultado foi publicado? | `confirm_batch_grade_launch`/`confirm_individual_grade_launch` e auditoria | Reportar sucesso parcial por item; nunca assumir que o lote inteiro foi aplicado. |

## Estados que devem permanecer visiveis

- `succeeded`: o extrator retornou conteudo dentro dos limites configurados; ainda pode exigir leitura pedagogica.
- `scanned_pdf` ou `ocr_extracted`: texto pode estar incompleto; citar a limitacao.
- `unsupported_format`, `file_too_large`, `empty`, `failed`: nao inferir ausencia de conteudo nem atribuir nota por fallback.
- `truncated` ou capability ausente: resultado parcial; recalcular cobertura antes de concluir.

Para limites e formatos atuais, consulte `moodle-assignments/references/submission-inspection.md`.
