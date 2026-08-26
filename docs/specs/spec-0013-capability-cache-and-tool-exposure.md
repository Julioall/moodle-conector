# SPEC-0013: Cache de capabilities e exposição condicional de tools

## Status

Draft.

## Objetivo

Remover segredos e semântica incorreta do cache de capabilities, e impedir que tools desabilitadas ou indisponíveis sejam anunciadas ao modelo.

## Contexto e evidência atual

`CapabilityRegistry` usa `ConnectionId:userToken` como chave e grava token em `CapabilitySnapshot.UserId`. As flags de escrita estão verdadeiras por padrão, enquanto a escrita universal permanece em `AlwaysOn` e só falha na execução.

## Decisão e arquitetura-alvo

- Cache por `ConnectionId` e Moodle user ID; quando necessário, fingerprint HMAC não reversível da credencial.
- `IMemoryCache` com expiração absoluta, tamanho e invalidação substitui TTL manual.
- Defaults e fallbacks de produção para escrita são `false`.
- `tools/list` filtra por feature flag, scope e capability Moodle; escrita falha fechada se a descoberta falhar.

## Escopo

- Migrar contratos, lifetime e testes do registry.
- Mover tools de escrita para containers condicionais.
- Aplicar predicado de feature/capability/scope antes da exposição MCP.

## Critérios de aceite

- [ ] Token não aparece em snapshot, chave, log ou exceção.
- [ ] Rotação invalida/recalcula capabilities sem persistir segredo.
- [ ] Tool de escrita desabilitada não aparece em `tools/list`.
- [ ] Tool indisponível na conexão não é exposta.

## Validação e evidências

```powershell
dotnet test tests/MoodleConnector.Application.Tests --filter "FullyQualifiedName~Capability|FullyQualifiedName~ToolExposure|FullyQualifiedName~Security"
```

## Rollout e rollback

Flags ficam desligadas até habilitação explícita por ambiente e família; rollback restaura somente exposição previamente aprovada.
