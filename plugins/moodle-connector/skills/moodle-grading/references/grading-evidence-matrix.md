# Matriz de evidencia para correcao

Use esta matriz antes de propor uma nota e exportar o CSV. Ela separa o que foi observado do que ainda exige revisao manual no arquivo.

| Pergunta | Fonte preferencial | Bloqueio ou limite |
| --- | --- | --- |
| Existe uma entrega e ela pertence ao estudante correto? | `prepare_ai_grading_batch`, identidade via `moodle-students` | Identidade ambigua, item fora do escopo ou pagina truncada: nao corrigir automaticamente. |
| O arquivo foi lido integralmente? | `prepare_ai_grading_batch` e `resource/file` | O resource original é a única fonte; falha de registro, leitura, autorização, integridade ou limite exige revisão manual. |
| Ha criterio, rubrica e valor maximo verificaveis? | `prepare_ai_grading_batch`, `get_pedagogical_guidelines` | Sem criterio aplicavel, gerar observacao/rascunho de baixa confianca; nao inventar rubrica. |
| A proposta esta pronta para exportacao? | `prepare_ai_grading_batch`, `save_ai_grading_batch`, `export_grading_corrections_csv` | O CSV e a saida final local. Registrar incertezas, evidencias e situacao por item. |
| A escrita Moodle deve ocorrer? | Nenhuma ferramenta neste fluxo | Nao criar previa nem confirmar; o fluxo termina no CSV e nao altera o Moodle. |

## Estados que devem permanecer visiveis

- `pending`: resource ainda precisa ser registrado no chat.
- `resource_read_failed`, `resource_forbidden`, `resource_expired` ou `resource_hash_mismatch`: não corrigir; registrar a falha e solicitar nova leitura.
- MIME desconhecido ou formato não reconhecido: não é falha por si só; leia os bytes originais pelo resource.

Para limites e formatos atuais, consulte `moodle-assignments/references/submission-inspection.md`.
