# SPEC-0013: Cache de capabilities e exposição condicional de tools

## Status

Implementing.

## Objetivo

Remover segredos e semântica incorreta do cache de capabilities, e impedir que tools desabilitadas ou indisponíveis sejam anunciadas ao modelo.

## Contexto e evidência atual

`CapabilityRegistry` usa `ConnectionId:userToken` como chave e grava token em `CapabilitySnapshot.UserId`. As flags de escrita estão verdadeiras por padrão, enquanto a escrita universal permanece em `AlwaysOn` e só falha na execução.

## Decisão e arquitetura-alvo

- Cache por `ConnectionId` e Moodle user ID; quando necessário, fingerprint HMAC não reversível da credencial.
- `IMemoryCache` com expiração absoluta, tamanho e invalidação substitui TTL manual.
- Defaults e fallbacks de produção para escrita são `false`.
- `tools/list` filtra por feature flag, scope e capability Moodle. Como a listagem não recebe um alias, considera todas as conexões ativas do cliente autenticado, sem exigir uma conexão padrão.
- Uma tool dependente é disponível se uma mesma conexão satisfaz todas as suas funções obrigatórias. Não combinar funções de credenciais diferentes.
- Em descoberta incompleta, consultas permanecem anunciadas e validam a conexão na execução; escritas com capabilities declaradas exigem ao menos um perfil verificado que satisfaça todas elas.
- Resposta sem array `functions` ou com entradas inválidas é erro de descoberta, não um perfil vazio. Não armazenar essa resposta no cache. Um array explicitamente vazio é um resultado válido.

## Escopo

- Migrar contratos, lifetime e testes do registry.
- Mover tools de escrita para containers condicionais.
- Aplicar predicado de feature/capability/scope antes da exposição MCP.

## Critérios de aceite

- [x] Token não aparece em snapshot, chave, log ou exceção.
- [x] Rotação invalida/recalcula capabilities sem persistir segredo.
- [x] Tool de escrita desabilitada não aparece em `tools/list`.
- [x] Tool dependente é ocultada por indisponibilidade somente quando nenhum dos perfis completos das conexões ativas satisfaz suas funções; descoberta incompleta preserva consultas e mantém escritas dependentes sem perfil verificado ocultas.
- [x] Descoberta restaura o alias da requisição e propaga cancelamento.
- [x] Logs distinguem `capability_discovery_incomplete` de `required_capabilities_unavailable`, sem registrar credenciais.

Implementado nesta onda: cache por conexão e fingerprint de credencial, expiração absoluta,
metadados de capabilities para wrappers Moodle e filtro dinâmico de `tools/list`. A validação
contra Moodle real ainda deve ser executada no ambiente de homologação.

## Validação e evidências

```powershell
dotnet test tests/MoodleConnector.Application.Tests --filter "FullyQualifiedName~Capability|FullyQualifiedName~ToolExposure|FullyQualifiedName~Security"
```

## Rollout e rollback

Flags ficam desligadas até habilitação explícita por ambiente e família; rollback restaura somente exposição previamente aprovada.
