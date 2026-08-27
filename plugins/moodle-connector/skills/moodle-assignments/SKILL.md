---
name: moodle-assignments
description: Consultar tarefas Moodle, prazos, entregas, status, pendencias, atrasos, notas existentes e contexto para correcao sem alterar submissao.
---

# moodle-assignments

Use para descobrir tarefas e analisar submissao. Toda intencao de lancar nota ou feedback passa para `moodle-grading`.

## Roteamento

- Definicao, nota maxima e prazos: `list_course_assignments`, `get_assignment`, `list_activity_deadlines` e `mod_assign_get_assignments`.
- Entregas: `list_assignment_submissions` e `get_student_submission`. `get_submission_status` permanece como alias de compatibilidade para clientes legados.
- Pendentes/atrasadas/aguardando correcao: `list_pending_submissions`, `list_late_submissions`, `list_submissions_awaiting_grading` e `list_students_with_pending_submissions`.
- Notas existentes de tarefa: `mod_assign_get_grades`; boletim agregado pertence a `moodle-grading`.
- Identidade, matricula e grupos pertencem a `moodle-students`.

## Contrato

Resolva alias e capabilities antes da leitura. Use `SafeReadExecutor` para uma leitura direta registrada; use o gateway especializado quando houver paginacao, filtros, joins com estudantes ou normalizacao. Preserve `courseid`, `assignmentid`, `userid`, `status`, `before` e `after`.

`assignmentId`/`instanceId` e `cmid` nao sao intercambiaveis. Nao substitua um estudante por nome exibido nem um curso por outro resultado aproximado.

## Completude

`hasMore`, cursor ou pagina parcial significa que a evidencia esta incompleta. So diga 'nao entregou' depois de exaurir o escopo de estudantes, tarefa, paginas e filtros. Se a funcao de submissao faltar, reporte a capability ausente; nunca converta isso em zero entregas.

## Inspecao de arquivos e contexto de correcao

Leituras coletivas nao devem expor o texto integral das entregas. Quando o usuario pedir leitura de anexos, contexto ou correcao, encaminhe a `moodle-grading`:

1. Crie um lote limitado com `create_assisted_grading_batch`, usando `includeSubmissionFiles`, `includeRubric` e `includeCourseMaterials` somente quando necessarios.
2. Consulte `get_grading_batch_status` e `get_grading_item_context` para cobertura e artefatos.
3. Use `prepare_submission_grading` para obter enunciado, texto extraido, nota maxima, instrucoes e status de cada entrega.
4. Trate `succeeded`, `scanned_pdf`, `ocr_extracted`, `unsupported_format`, `file_too_large`, `empty` e `failed` como estados distintos.
5. So pontue criterios cujo conteudo esteja verificavel. `scanned_pdf`, `unsupported_format` ou `failed` bloqueiam a atribuicao automatica de nota.

O extrator atual cobre texto/HTML/JSON/XML/CSV, PDF, DOCX, PPTX, XLSX, OpenDocument e ZIP com entradas suportadas, com limites de tamanho, quantidade e chunking. Ele nao verifica nativamente formulas calculadas, graficos, camadas XCF/PSD ou requisitos de audio/video. Para detalhes e o pre-processador local de bundles, leia [references/submission-inspection.md](references/submission-inspection.md).

Para correcao, entregue contexto de submissao e identidade a `moodle-grading`; nao prepare ou confirme escrita nesta skill.
