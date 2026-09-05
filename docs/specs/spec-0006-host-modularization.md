# SPEC-0006: Modularização do host ASP.NET

## Status

Implementing.

## Objetivo

Transformar o host em uma composição pequena de módulos, preservando contratos externos.

## Contexto e evidência atual

`src/MoodleConnector.Presentation/Program.cs` concentra registro de serviços, MCP, OAuth, portal,
administração, static files, workers e acesso direto a dados em vários endpoints.

## Escopo

- Extrair bootstrap, grupos de endpoints, autenticação e workers para módulos.
- Mover regras de casos de uso para Application.
- Criar testes de arquitetura que proíbam acesso direto ao `ConnectorDbContext` nos endpoints.

## Fora de escopo

- Renomear assemblies ou dividir deploys.
- Alterar rotas públicas sem compatibilidade explícita.

## Critérios de aceite

- [ ] `Program.cs` mantém somente composição e chamada de módulos.
- [ ] MCP, portal, OAuth e admin possuem módulos e testes próprios. A borda MCP e a política de sessão do portal já estão em middleware dedicado; status, health checks e OAuth discovery estão em `OperationalEndpoints`, a autorização OAuth em `OAuthAuthorizationEndpoints`, o registro administrativo em `AdminEndpoints`, a compatibilidade SPA em `PortalShellEndpoints`, a sessão local e emissão de CSRF em `PortalAuthenticationEndpoints`, a sessão/conexões autenticadas em `PortalSessionAndConnectionEndpoints`, tarefas em `PortalTaskEndpoints`, conta/equipes/permissões em `PortalAccountEndpoints`, fóruns em `PortalForumEndpoints` e mensagens em `PortalMessagingEndpoints`; a revisão de notas do Portal foi removida em favor do lote MCP unificado.
- [ ] Exceções de acesso direto a dados são inexistentes ou documentadas e temporárias.
- [ ] Testes funcionais preservam contratos atuais.

## Validação e evidências

- Rodar a suíte .NET completa e testes de integração por superfície.
- Revisar `git diff --check` e análise arquitetural em CI.
- A extração de `OperationalEndpoints` preserva os contratos de metadata OAuth e do bootstrap
  público; os testes `McpJwtClaimsIntegrationTests` cobrem essas superfícies.

## Rollout e rollback

Extrair um grupo de endpoints por commit. Cada etapa deve ser revertível sem migração de dados.
