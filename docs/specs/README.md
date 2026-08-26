# Specs ativas do Moodle Connector

## Objetivo

Esta pasta registra contratos de mudança que estão em execução. Ela é a fonte canônica para
escopo, dependências, aceite e evidências; ADRs registram decisões duráveis e documentos em
`docs/archive/` preservam planos substituídos.

## Estados

`Draft` → `Review` → `Approved` → `Implementing` → `Validated` → `Released`.

Uma spec pode ser marcada como `Superseded` somente quando indicar a substituta e preservar suas
evidências.

## Índice

| Spec | Estado | Dependência principal | Resultado |
|---|---|---|---|
| [SPEC-0000](spec-0000-component-boundaries.md) | Implementing | — | Fronteiras e vocabulário canônicos |
| [SPEC-0001](spec-0001-quality-baseline.md) | Implementing | SPEC-0000 | Build e testes reproduzíveis |
| [SPEC-0002](spec-0002-plugin-package.md) | Implementing | SPEC-0000 | Pacote universal instalável |
| [SPEC-0003](spec-0003-skill-distribution.md) | Implementing | SPEC-0002 | Skills distribuídas por fonte única |
| [SPEC-0004](spec-0004-tool-catalog.md) | Implementing | SPEC-0000 | Catálogo e submissão gerados |
| [SPEC-0005](spec-0005-benchmark-fidelity.md) | Implementing | SPEC-0003, SPEC-0004 | Benchmark com skills reais |
| [SPEC-0006](spec-0006-host-modularization.md) | Implementing | SPEC-0001 | Host ASP.NET modular |
| [SPEC-0007](spec-0007-auth-boundaries.md) | Implementing | SPEC-0006 | Políticas explícitas por rota |
| [SPEC-0008](spec-0008-portal-topology.md) | Implementing | SPEC-0000 | Portal e URL canônicos |
| [SPEC-0009](spec-0009-documentation-remediation.md) | Validated | SPEC-0000 | Documentação e encoding corrigidos |
| [SPEC-0010](spec-0010-release-certification.md) | Implementing | Todas as anteriores | Release certificável |
| [SPEC-0011](spec-0011-safe-moodle-writes.md) | Draft | — | Escritas sem retry cego e resultado remoto reconciliável |
| [SPEC-0012](spec-0012-write-preview-risk-and-scopes.md) | Draft | SPEC-0011 | Confirmação semântica, impacto e scopes por domínio |
| [SPEC-0013](spec-0013-capability-cache-and-tool-exposure.md) | Draft | — | Capabilities sem token e tools condicionais |
| [SPEC-0014](spec-0014-canonical-mcp-surface.md) | Draft | SPEC-0013 | Catálogo MCP canônico com migração segura |
| [SPEC-0015](spec-0015-current-state-and-analytical-snapshots.md) | Draft | SPEC-0013 | Base operacional de snapshots, consistente e performática |
| [SPEC-0017](spec-0017-postgresql-concurrency-and-release-gates.md) | Draft | SPEC-0011, SPEC-0015 | Garantias PostgreSQL e gates de release |

## Regra de rastreabilidade

Todo PR deve mencionar a spec aplicável e listar os critérios de aceite atendidos. Evidências
devem apontar para testes, artefatos de CI, commits ou verificações manuais; não devem duplicar
resultados em texto sem referência verificável.
