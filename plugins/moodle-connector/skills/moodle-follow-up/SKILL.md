---
name: moodle-follow-up
description: Identificar estudantes e atividades que exigem acompanhamento a partir de entregas, acesso, conclusao, forum e notas observaveis no Moodle.
---

# moodle-follow-up

Use para produzir uma lista priorizada de acompanhamento. A skill gera evidencia e proximos passos; nao envia mensagens automaticamente.

## Fluxo

1. Execute `moodle-core` e fixe conexao, curso, janela e populacao.
2. Consulte apenas as evidencias necessarias: `list_pending_submissions`/`list_late_submissions`, `list_students_without_recent_access`, `list_students_without_forum_participation`, gradebook ou `report_students_at_risk` quando solicitados.
3. Antes de interpretar avaliacao, feedback, risco ou recomendacao pedagogica, use `moodle-pedagogy` para buscar orientacao pertinente.
4. Aplique criterios explicitos, como atraso ou 'sem acesso em 14 dias'; nao invente limiar.
5. Retorne estudante, curso/atividade, evidencia, timestamp, fonte, cobertura e confianca.
6. Para outreach, entregue candidatos a `moodle-messaging` para prepare/confirm.

## Regras de interpretacao

'Nao entregou' exige escopo completo de tarefa e estudantes. 'Inativo' exige janela definida e last access observavel. Completion detalhado nao esta exposto como tool independente; use apenas evidencias de relatorios que o fornecam. Ausencia de uma capability e desconhecido, nao negativo. Misture sinais somente depois de reconciliar `userid`, curso e atividade.

Se houver truncamento, pagina parcial ou falha de uma fonte, marque o candidato como cobertura parcial e nao apresente a lista como censo.
