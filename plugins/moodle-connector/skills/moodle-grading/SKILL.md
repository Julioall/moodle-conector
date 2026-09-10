---
name: moodle-grading
description: Corrigir atividades com rascunhos internos e escolher entre publicar no Moodle com confirmacao explicita ou gerar CSV externo.
---

# moodle-grading

Separe leitura, preparacao, previa e confirmacao. A decisao pedagogica permanece humana; o Moodle so e alterado depois de uma confirmacao explicita.

## Leitura e descoberta

- Use `get_student_gradebook`, `get_student_activity_grades`, `list_students_below_min_grade`, `gradereport_user_get_grade_items`, `gradereport_user_get_grades_table` e `mod_assign_get_grades` conforme o escopo.
- Use as ferramentas de leitura de notas e entregas para verificar arquivos, submissao e contexto. Para preparar a correcao, o fluxo unificado inicia o lote e usa `prepare_ai_grading_batch`.
- Submissao vem de `moodle-assignments`; identidade vem de `moodle-students`; orientacao de avaliacao vem de `moodle-pedagogy`.
- Antes de propor nota, aplique `references/grading-evidence-matrix.md` e confirme a leitura dos resources originais, cobertura e incerteza.

## Geracao de correcoes

1. Crie o lote limitado com estudantes, tarefas, criterios e valores propostos.
2. Use `prepare_ai_grading_batch` para obter o contexto e gerar nota/feedback no chat, inclusive quando o lote tiver somente um aluno.
3. Use `save_ai_grading_batch` para persistir as correcoes internas; nao escreva no Moodle nessa etapa.
4. Quando o usuario pedir CSV, use `export_grading_corrections_csv`. O CSV e uma saida final externa no formato `nome;nota;feedback`; nunca e etapa para publicar no Moodle.
5. Quando o usuario pedir correcao normal ou publicacao, use `create_batch_grade_launch_preview`. Mostre integralmente todos os itens retornados: nome do aluno, atividade, nota proposta/maxima, `feedbackText` completo, `reviewStatus`, `situation`, notas/feedbacks existentes e avisos. Nunca reduza a previa a uma tabela somente com aluno e nota, nem omita ou resuma o feedback antes da confirmacao.
6. So chame `confirm_batch_grade_launch` depois que o usuario responder exatamente `CONFIRMAR_PUBLICACAO`. A confirmacao revalida rascunho, submissao e dados atuais do Moodle antes de cada escrita e retorna `authorized` enquanto o worker ainda nao concluiu.
7. Depois de uma confirmacao `authorized`, use `get_assisted_grading_batch_status` com o `batchJobId` da previa/confirmacao. Informe por aluno o nome, nota, feedback e `commitStatus`; somente `Succeeded` significa que o Moodle/ledger confirmou a publicacao efetiva.
8. Para um aluno ou muitas atividades, use o mesmo lote e o mesmo par de previa/confirmacao. Nao use ferramentas de UI ou CSV como rota de publicacao.

Quando o usuario pedir explicitamente uma reavaliacao de correcoes ja publicadas, inicie
`start_pending_grading_run` com `includeAlreadyGraded=true`, restringindo por `courseId` e
`assignmentIds` quando esses dados forem informados. Esse modo cria novos itens com a
submissao original entregue e preserva os itens/correcoes anteriores; ele nao escreve no
Moodle. Depois de gerar e salvar os novos rascunhos, use
`create_batch_grade_launch_preview` com `allowOverwriteExisting=true` somente se o usuario
tambem autorizar substituir a nota/feedback existentes, e aguarde
`CONFIRMAR_PUBLICACAO` antes da escrita.
