# Plugin Moodle Connector

Este diretório é o pacote instalável do Moodle Connector para Codex e ChatGPT. Ele distribui
skills e, quando a conexão remota estiver registrada, também apontará para o servidor MCP por
meio de `.app.json`.

## Estado atual

- O manifesto `.codex-plugin/plugin.json` está validado e referencia as onze skills em `skills/`.
- `.app.json` vincula o pacote à conexão MCP remota registrada no ChatGPT.
- Não há `.mcp.json`: o servidor Moodle Connector é remoto e não deve ser empacotado como processo
  local.

## Vincular a conexão registrada no ChatGPT

Depois de registrar `https://novascript.com.br/mcp` em ChatGPT Developer mode, abra a página da
conexão e copie da URL o identificador persistente que começa com `plugin_asdk_app_`. O `code` e o
`state` presentes no callback OAuth são temporários e não identificam o app.

Execute na raiz do repositório:

```powershell
./scripts/link-chatgpt-app.ps1 -AppId "plugin_asdk_app_SEU_ID"
./scripts/validate-plugin.ps1
```

O vinculador cria `plugins/moodle-connector/.app.json` e adiciona `apps: "./.app.json"` ao
manifesto. Ele recusa qualquer valor que não tenha o prefixo técnico esperado. O pacote fica
disponível no marketplace local declarado em `.agents/plugins/marketplace.json`; após instalar ou
atualizar, abra uma conversa nova para que Skills e metadados não sejam reutilizados do cache da
conversa anterior.

Consulte `docs/specs/spec-0002-plugin-package.md` e
`docs/specs/spec-0003-skill-distribution.md` antes de alterar este pacote.
