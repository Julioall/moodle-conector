# SPEC-0007: Fronteiras explícitas de autenticação

## Status

Implementing.

## Objetivo

Aplicar políticas de autenticação e autorização declaradas por grupo de rotas.

## Escopo

- Formalizar políticas para MCP, portal, admin, OAuth e discovery.
- Cobrir JWT, API key aprovada, cookie, CSRF, escopo, audience e papel administrativo.
- Criar matriz de testes positivos e negativos.

## Fora de escopo

- Criar um novo provedor de identidade.

## Dependências

- Ambiente HTTPS e callback OAuth real para a validação no MCP Inspector.
- Conta de teste Moodle vinculada para testar autorização após OAuth.

## Critérios de aceite

- [x] `/mcp`, `/api`, `/admin`, OAuth e discovery têm políticas documentadas e aplicadas.
- [x] Credencial válida para uma superfície não concede acesso implícito a outra.
- [ ] Escritas preservam confirmação humana, escopo e conexão autorizada.
- [ ] Logs preservam correlation ID e redigem PII e segredos.

## Validação e evidências

- Executar testes de JWT, cookie, CSRF, API key, scopes e tentativas cross-route.
- Validar OAuth discovery e fluxo MCP no Inspector.

## Rollout e rollback

Promover por feature flag ou rota de homologação. Manter a política anterior até os clientes
compatíveis serem comprovados.
