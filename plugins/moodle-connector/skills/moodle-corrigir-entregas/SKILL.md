---
name: moodle-corrigir-entregas
description: Orquestrar leitura de entregas e correção assistida no Moodle, preservando evidências, incertezas, revisão humana e confirmação antes do lançamento.
---

# moodle-corrigir-entregas

Use quando o usuário pedir para corrigir, avaliar, dar nota ou preparar feedback para entregas Moodle.

## Preflight obrigatório

1. Resolva conexão, curso, UC e atividade com `moodle-localizar-contexto-curso`.
2. Consulte `get_pedagogical_guidelines` para buscar orientação de avaliação e feedback.
3. Use `moodle-assignments` para confirmar definição da tarefa, nota máxima, prazo, participantes e estado das submissões.
4. Use `moodle-students` para resolver identidade e matrícula; nunca associe nota apenas por nome aproximado.
5. Use `moodle-grading` para contexto, extração e revisão da correção.

## Fluxo seguro

1. Liste o escopo com `list_assignment_submissions`, `list_submissions_awaiting_grading` ou `list_all_gradable_submissions`, informando filtros e paginação.
2. Crie um lote limitado com `create_assisted_grading_batch` ou prepare a análise com `prepare_ai_grading_batch`. Inclua arquivos, rubrica e materiais somente quando necessários.
3. Acompanhe `get_grading_batch_status`; examine cada item por `get_grading_item_context` e `prepare_submission_grading`.
4. Trate estados de evidência separadamente: entrega ausente, vazia, formato não suportado, arquivo grande, PDF escaneado, OCR, falha de extração e conteúdo verificável.
5. Gere proposta de nota e feedback somente para critérios cuja evidência esteja disponível. Marque itens duvidosos para revisão, sem preencher lacunas por inferência.
6. Revise e ajuste por `review_batch_feedbacks`, `get_batch_grading_ui_state`, `update_grading_draft` ou `update_grading_drafts_batch`.
7. Exiba uma prévia com alunos, notas, feedbacks, critérios, escopo, contagem, hash/identificador do lote e avisos antes de lançar.
8. Para lançar, use `create_batch_grade_launch_preview` e aguarde confirmação literal do usuário; somente então use `confirm_batch_grade_launch`.
9. Relate sucesso, falha, itens ignorados e estado desconhecido por item. Não declare o lote concluído se houver resultado parcial.

## Regras de avaliação

- A rubrica e o enunciado fornecidos pelo Moodle são a referência principal.
- Não invente entrega, conteúdo, critério, nota, prazo ou estudante.
- Diferencie ausência de submissão, arquivo inválido e desempenho insuficiente.
- Preserve o nome exibido e o identificador retornado pelo Moodle.
- Não transforme atividade de presença em nota acadêmica.
- Não lance nota ou feedback automaticamente, mesmo que a proposta pareça confiável.
- Se a evidência não permitir avaliar, explique o bloqueio e solicite ação humana.

## Limite

Esta skill pode preparar rascunhos e prévias, mas escrita no Moodle exige pending action vigente, capability, escopo, feature flag e confirmação humana conforme `moodle-grading`. Nunca use `moodle_execute_read` para executar escrita.
