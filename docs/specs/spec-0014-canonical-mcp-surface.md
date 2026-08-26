# SPEC-0014: Superfície MCP canônica e migração de wrappers

## Status

Draft. Depende de SPEC-0013.

## Objetivo

Reduzir a carga cognitiva do catálogo MCP sem remover intenções pedagógicas úteis ou quebrar consumidores existentes.

## Contexto e evidência atual

O projeto expõe cerca de 110 tools em 27 containers `AlwaysOn`. Há primitives universais e wrappers de leitura; `CognitiveExposurePolicy` não filtra por feature ou capability.

## Decisão e arquitetura-alvo

Manter primitives estruturais seguras e tools especializadas que agregam, normalizam ou aplicam regra acadêmica. Wrappers pass-through tornam-se candidatos a deprecação somente depois de telemetria e migração das skills.

## Escopo

- Inventário de tool, intenção, consumidor, feature, scope, capability, risco e decisão.
- Nomes/descritivos canônicos e curtos.
- Deprecação compatível, telemetria e período de transição.

## Fora de escopo

- Reduzir funções disponíveis no Moodle remoto.

## Critérios de aceite

- [ ] Cada tool pública tem justificativa de permanência, sucessora ou data de retirada.
- [ ] Nenhum wrapper é removido sem telemetria de uso e release de compatibilidade.
- [ ] Manifesto de produção exclui tools ocultas pela SPEC-0013.

## Validação e evidências

```powershell
dotnet test tests/MoodleConnector.Application.Tests --filter "FullyQualifiedName~ToolMetadata|FullyQualifiedName~CognitiveExposure"
```

## Rollout e rollback

Marcar como deprecated antes de remover. Restaurar apenas aliases ainda usados, com evidência de telemetria.
