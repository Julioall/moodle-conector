# SPEC-0002: Pacote universal do plugin

## Status

 Implementing.

## Objetivo

Disponibilizar o Moodle Connector como um pacote instalável em Codex e ChatGPT, conectando skills
ao servidor MCP remoto já operado pelo projeto.

## Contexto e evidência atual

O pacote base existe em `plugins/moodle-connector/` e contém apenas o manifesto e a pasta de
skills. O repositório não contém o identificador `plugin_asdk_app...` necessário para criar uma
referência válida em `.app.json`.

## Escopo

- Completar o manifesto e assets mínimos de distribuição.
- Registrar a conexão MCP remota e criar `.app.json` com seu ID real.
- Documentar a instalação pelo marketplace pessoal, que é estado do perfil do desenvolvedor e não
  deve ser versionado no repositório.
- Validar instalação limpa em Codex e ChatGPT.

## Fora de escopo

- Empacotar o servidor ASP.NET como `.mcp.json`.
- Publicação pública ou no workspace sem aprovação administrativa.

## Dependências e decisões em aberto

- Identificador técnico da conexão MCP criada em ChatGPT Developer mode.
- Decisão de proprietário e metadados públicos antes da publicação.

## Plano de execução

1. Registrar `https://<dominio>/mcp` e copiar o ID `plugin_asdk_app...`.
2. Criar `.app.json` com a referência e adicionar `apps: "./.app.json"` ao manifesto.
3. Atualizar o marketplace pessoal com fonte `./plugins/moodle-connector` sem versionar arquivos
   do perfil do desenvolvedor.
4. Instalar em ambiente limpo e executar fluxos de leitura, escrita preparada e UI MCP.

## Critérios de aceite

- [x] O manifesto e o pacote não contêm segredos, URL local ou cópia do servidor remoto.
- [ ] `.app.json` possui somente o ID de conexão registrado e revisado.
- [ ] O pacote instala pelo marketplace do repositório.
- [ ] Skills, tools MCP e UI MCP ficam disponíveis em uma nova conversa.
- [ ] O pacote não contém segredos, URL local ou cópia do servidor remoto.

## Validação e evidências

```powershell
./scripts/validate-plugin.ps1
```

Revisar também a instalação pela Plugins Directory e a autenticação OAuth contra o ambiente de
homologação.

## Rollout e rollback

Publicar primeiro no marketplace do repositório. Remover a entrada do marketplace ou restaurar a
versão anterior do pacote reverte a distribuição sem modificar o servidor MCP.
