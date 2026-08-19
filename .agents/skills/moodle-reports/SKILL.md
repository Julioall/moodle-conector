---
name: moodle-reports
description: Gerar relatorios Moodle de desempenho, monitoria, notas, resumo de curso, conselho de classe e pos-execucao com cobertura e limites explicitos.
---

# moodle-reports

Use para relatorios compostos. Eles agregam leituras e nao substituem a validacao humana de notas, risco, aprovacao ou reprovacao.

## Escolha da tool

- `generate_course_summary`: resumo rapido de matricula e acesso.
- `generate_course_grades_report`: notas totais do curso por estudante em conteudo estruturado para analise.
- `export_course_grades_excel`: mesmo relatorio de notas entregue como arquivo Excel anexado ao resultado MCP.
- `generate_weekly_performance_report`: acesso, notas, entregas e classificacoes indicativas.
- `generate_class_council_report`: situacao pedagogica indicativa para discussao colegiada.
- `generate_full_post_execution_report`: situacao provavel ao fim do curso, incluindo desconhecidos.
- `generate_monitor_class_report`: matricula e acesso para o monitor, sem notas ou submissoes.
- `audit_virtual_classroom_checklist`: checklist estrutural da sala; para regras de auditoria, use `moodle-classroom-audit`.

## Preparacao

1. Resolva conexao, curso, periodo, populacao e denominador.
2. Consulte `moodle-pedagogy` antes de relatorios de desempenho, feedback, risco ou conselho.
3. Preserve os limiares informados. Se o usuario nao informar, mostre os defaults usados, como nota minima, dias de inatividade e `maxStudentsToAnalyze`.
4. Para cada resultado, informe `generatedAt`, fontes, capacidades ausentes, `coveredCount`/`total` quando houver, limite por estudante e falhas parciais.
5. Valide o envelope contra `references/report-evidence-contract.md` antes de apresentar o relatorio como completo.

Para notas, prefira `generate_course_grades_report` quando a proxima etapa for analise da IA e `export_course_grades_excel` quando o usuario precisar baixar, compartilhar ou arquivar o relatorio. Ambas usam a nota total do curso retornada pelo Moodle e nao somam atividades localmente.

## Guardrails

Relatorios usam agregacao local e podem ser lentos ou limitados a 60/100 estudantes. 'At risk', 'likely complete', 'recovery needed', 'regular' e 'inactive' sao categorias tecnicas/indicativas, nao decisoes oficiais. Nao invente dados presenciais, SGE, satisfacao, tendencia historica ou presenca fisica.

Relatorio nao envia mensagem nem altera nota. Entregue candidatos a `moodle-follow-up`, `moodle-messaging` ou `moodle-grading` conforme a proxima acao.
