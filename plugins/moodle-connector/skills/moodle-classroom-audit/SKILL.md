---
name: moodle-classroom-audit
description: Auditar sala virtual Moodle, estrutura, checklist, cobertura de atividades, participacao, conclusao e saude reportavel da turma.
---

# moodle-classroom-audit

Use para auditoria analitica da sala virtual e para preparar insumos de monitoria. Nao altera curso.

## Sequencia

1. Resolva conexao, curso e janela.
2. Leia estrutura com `list_course_contents`/`audit_course_structure` e classifique modulos.
3. Para o checklist institucional, use `audit_virtual_classroom_checklist`; ele pode retornar `ok`, `ausente`, `incompleto` ou `nao_verificavel`.
4. Recolha apenas prazos, atividades, participantes, acesso, forum e conclusao necessarios.
5. Reconcile `courseId`, `cmid`, `instanceId` e `userid` antes de calcular cobertura.
6. Se o resultado for um relatorio composto, encaminhe a `moodle-reports`.

## Evidencia e limites

Declare operations/fontes, timestamp, pagina, `truncated`, capabilities ausentes e denominador. Curso vazio, capability ausente, modulo nao verificavel e modulo confirmado ausente sao estados diferentes. Nunca chame a turma de inativa por um unico endpoint vazio.

`generate_monitor_class_report` e voltado a matricula/acesso e nao inclui notas ou submissao. `generate_course_summary` e um resumo rapido. Nenhum checklist ou relatorio indicativo constitui decisao oficial de aprovacao, reprovacao ou evasao.

Use `references/classroom-evidence-matrix.md` para reconciliar fontes, cobertura, IDs e estados nao verificaveis antes de emitir o diagnostico.
