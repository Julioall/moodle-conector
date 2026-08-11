# Revisão de produto e arquitetura

## Visão consolidada

O Moodle Connector é um conector MCP que permite a clientes autorizados consultar e, em fluxos controlados, preparar e confirmar ações sobre ambientes Moodle. O produto tem duas superfícies: portal operacional determinístico e endpoint MCP para ChatGPT ou cliente compatível.

## Portal MCP, conector, portal e camada de dados

- **Portal MCP:** `/mcp`; autentica, expõe tools e aplica contexto, escopos, políticas e confirmação.
- **Conector:** o sistema que traduz intenção autorizada em chamadas Moodle, mantendo contratos, auditoria e limites.
- **Portal web:** interface determinística para conta, conexões e visões operacionais; não chama MCP ou Moodle diretamente no navegador e não executa IA.
- **Camada de dados:** PostgreSQL e storage interno para contas, conexões protegidas, ações pendentes, auditoria e estado operacional; Moodle permanece a fonte remota acadêmica.

Fluxo: cliente/portal → Presentation → Application, policies e registry → Infrastructure → Moodle e PostgreSQL.

## Auditoria e consolidação

Auditoria registra quem, conexão/contexto, operação, decisão de autorização, resultado e correlação, com payload sanitizado. Consolidação combina evidências por conexão e curso; não inventa dados, não mistura Moodles e declara cobertura, limitações e revisão humana.

## Papéis

- **Tutor:** acompanha, interpreta evidências, orienta, corrige e decide contatos pedagógicos dentro da autorização.
- **Monitor:** apoia ambientação, acesso e navegação e encaminha ocorrências.
- **Gerente:** coordena equipes, indicadores e governança; não herda acesso acadêmico individual.
- **Administrador:** administra contas, equipes, conexões e políticas; acesso acadêmico exige escopo e contexto explícitos.

Papéis são responsabilidades de produto; autorização efetiva depende de identidade, equipe, escopo, conexão, contexto e capability Moodle. Planejado não significa implementado.
