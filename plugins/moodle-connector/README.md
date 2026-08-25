# Plugin Moodle Connector

Este diretório é o pacote instalável do Moodle Connector para Codex e ChatGPT. Ele distribui
skills e, quando a conexão remota estiver registrada, também apontará para o servidor MCP por
meio de `.app.json`.

## Estado atual

- O manifesto `.codex-plugin/plugin.json` está validado e referencia as onze skills em `skills/`.
- Não há `.app.json` enquanto o identificador técnico `plugin_asdk_app...` da conexão MCP não for
  disponibilizado.
- Não há `.mcp.json`: o servidor Moodle Connector é remoto e não deve ser empacotado como processo
  local.

Consulte `docs/specs/spec-0002-plugin-package.md` e
`docs/specs/spec-0003-skill-distribution.md` antes de alterar este pacote.
