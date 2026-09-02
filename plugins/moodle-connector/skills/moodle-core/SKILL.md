---
name: moodle-core
description: "Base para qualquer operacao no Moodle Connector: resolver conexao, diagnosticar identidade e capabilities, escolher fluxos registrados, executar leituras seguras e encaminhar escritas para confirmacao humana."
---

# moodle-core

Use esta skill como fundamento de toda tarefa que consulta ou altera um ambiente Moodle.

## Resolucao e capabilities

1. Preserve o alias informado pelo usuario. Sem alias, use a conexao padrao configurada; nao assuma FIEG quando houver mais de uma conexao possivel.
2. Use `moodle_list_available_flows` para descobrir estrategias e fallbacks registrados antes de escolher uma rota composta; ela permanece na superficie `Production`.
3. `moodle_diagnose_connection`, `moodle_list_functions` e `moodle_check_function` sao diagnosticos tecnicos preservados por nome e no perfil `Full`; use-os somente quando o cliente os oferecer explicitamente ou houver necessidade de suporte.
4. Consulte capabilities por meio das tools especializadas e dos fluxos registrados antes de chamar uma primitive universal.
5. Solicite `forceRefresh=true` somente quando nao houver snapshot valido, ele estiver obsoleto, a conexao/credencial mudar ou uma funcao esperada desaparecer.

O registro de capabilities informa disponibilidade tecnica, nao prova permissao contextual em curso, atividade, grupo ou estudante. Se a funcao estiver ausente, retorne `funcao_indisponivel` ou a falha equivalente; nunca trate isso como resultado vazio.

## Execucao

- Para uma leitura generica, use `moodle_execute_read` somente com uma funcao descoberta como disponivel para o token atual e classificada como consulta pelo nome canonico. Se a funcao nao for claramente uma consulta, use o fluxo de escrita confirmado.
- Prefira tools especializadas quando elas fazem paginacao, joins, normalizacao ou regras de dominio. Elas continuam sujeitas a conexao, capability e autorizacao.
- Funcoes de escrita, remocao e qualquer funcao que nao seja claramente uma consulta nao passam pelo executor generico; todas seguem o fluxo de confirmacao.
- A normalizacao padrao e `Agent`; respostas grandes podem conter `truncated=true`. Preserve essa informacao e nao afirme censo completo sem exaurir paginas ou usar uma estrategia que controle a continuacao.

## Escritas

Nunca execute escrita por `moodle_execute_read`. Use `moodle_prepare_write`/`moodle_confirm_write` ou o par especializado do dominio, conforme o fluxo. A confirmacao exige a mesma identidade e conexao, escopo aplicavel (incluindo `moodle.write` para funcoes sem familia especifica), `CanWrite`, capability Moodle, feature flag, pending action vigente, texto literal e auditoria.

Os pares especializados atuais incluem mensagens, publicacao em forum e notas individuais/em lote. Uma funcao listada no Moodle nao autoriza por si so a escrita.

## Fluxos registrados

O registry atual inclui estrategias para `listar_cursos_ativos`, `consultar_curso`, `buscar_cursos`, `listar_cursos_categoria` e `listar_entregas_aguardando_correcao`. Sempre informe a estrategia selecionada e as funcoes ausentes quando o fluxo nao estiver disponivel.

Para uma matriz detalhada de familias, operations, handoffs e estados de cobertura, leia [references/connector-surface.md](references/connector-surface.md). Ao alterar tools ou skills, valide a superficie com `scripts/check_skill_catalog.py`.

## Limites

Nao exponha tokens, senhas ou URLs privadas. Nao confunda dados do portal local com dados do Moodle. Nao crie fallback por nome de funcao inventado e nao converta erro de capability em 'nao ha alunos', 'nao ha entregas' ou 'nao ha forum'.
