---
name: moodle-pedagogy
description: Buscar orientacao pedagogica do conector antes de avaliacao, feedback, planejamento, forum, acompanhamento estudantil ou relatorios Moodle.
---

# moodle-pedagogy

Use `get_pedagogical_guidelines` como preflight sempre que a resposta puder influenciar avaliacao, feedback, recuperacao, comunicacao, acompanhamento ou narrativa de relatorio.

## Fluxo

1. Converta a tarefa em conceitos de busca, por exemplo 'avaliacao formativa e feedback', 'acompanhamento de estudante', 'forum e participacao' ou 'conselho de classe'.
2. Consulte `get_pedagogical_guidelines` com uma pergunta objetiva e limite adequado.
3. Leia caminho relativo, titulo, secao, trecho e score retornados; use a orientacao para linguagem, evidencias e limites.
4. Declare quando nao houver resultado suficiente e mantenha a resposta descritiva, sem fabricar norma institucional.

## Limites

Orientacao pedagogica nao autoriza acesso, escrita, envio de mensagem, lancamento de nota ou decisao de aprovacao. Continue exigindo `moodle-core`, capability, policy, pending action e confirmacao humana. Em conselho de classe, risco, recuperacao e pos-execucao, trate classificacoes como indicativas e preserve fontes externas ausentes.

Esta skill complementa, mas nao substitui, `moodle-grading`, `moodle-follow-up`, `moodle-forums`, `moodle-messaging` e `moodle-reports`.
