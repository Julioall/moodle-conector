# SPEC-0000: Fronteiras de componentes e vocabulário

## Status

Implementing.

## Objetivo

Estabelecer uma nomenclatura única e contratos claros para plugin, servidor MCP remoto, portal,
UI MCP e submissão.

## Contexto e evidência atual

O servidor ASP.NET expõe `/mcp`, OAuth, APIs do portal, static files e workers a partir de uma
única composição. O portal React/Vite é compilado para o host, enquanto as skills distribuíveis
vivem em `plugins/moodle-connector/skills/`. Há metadados de submissão e pacote inicial do plugin,
mas o vínculo com a conexão MCP registrada ainda depende do identificador técnico externo.

## Decisão e arquitetura-alvo

Aplicar a [ADR-0003](../architecture/adr-0003-plugin-mcp-portal-boundaries.md). O pacote fica em
`plugins/moodle-connector/`; ele referencia, mas não incorpora, a conexão MCP remota. O servidor
permanece a autoridade de execução e o portal permanece uma interface humana separada.
O widget de correção caracteriza apenas essa parte do produto como Apps SDK
`interactive-decoupled`; o portal React não integra a bridge MCP.

## Escopo

- Criar o diretório canônico de specs e seu índice.
- Registrar a decisão arquitetural e o vocabulário obrigatório.
- Criar a estrutura inicial do pacote do plugin, sem conexão MCP fictícia.

## Fora de escopo

- Separar deploys ou renomear assemblies.
- Alterar endpoints, tools ou políticas de autenticação.

## Dependências e decisões em aberto

- A conexão de app remota exige o identificador técnico `plugin_asdk_app...` criado no ambiente
  ChatGPT com Developer mode.

## Critérios de aceite

- [x] A ADR define os cinco artefatos e suas responsabilidades.
- [x] Specs ativas possuem local, estados e índice canônicos.
- [ ] O pacote informa claramente que ainda não possui vínculo `.app.json`.

## Validação e evidências

- Revisar links em `docs/README.md` e `docs/documentation-audit.md`.
- Validar o manifesto do plugin após o pacote inicial ser preenchido.

## Rollout e rollback

As mudanças são documentais e aditivas. A reversão remove somente a nova estrutura sem alterar
runtime, APIs ou dados.
