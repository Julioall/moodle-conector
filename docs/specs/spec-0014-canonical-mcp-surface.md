# SPEC-0014: Superfície MCP canônica e migração de wrappers

## Status

Draft. Depende de SPEC-0013.

## Objetivo

Reduzir a carga cognitiva do catálogo MCP sem remover intenções pedagógicas úteis ou quebrar consumidores existentes.

## Contexto e evidência atual

O catálogo atual possui 111 entradas registradas em 30 containers (`AlwaysOn` e
condicionais). Com as flags de escrita de catálogo habilitadas e as tools de demo
desligadas, a superfície `Production` contém 109 tools. O `tools/list` aplica
feature flags, OAuth scopes, capabilities Moodle e `CognitiveExposurePolicy`
antes da serialização.

Na revisão dirigida da superfície, a equivalência técnica inequívoca encontrada
foi `get_submission_status` → `get_student_submission`: ambos chamam o mesmo
fluxo de aplicação, retornam o mesmo contrato e aceitam a mesma assinatura. O
primeiro permanece registrado como alias de compatibilidade e passa a declarar a
mesma `CanonicalOperation` (`assignments.submissions.get_student`) e capability
real (`mod_assign_get_submissions`). Sem telemetria de consumidores externos, ele
continua exposto em `Production` nesta release; a ocultação será reavaliada após
período de migração.

Os pares `search`/`search_courses`, `fetch`/`get_course`, participantes,
conteúdos, atividades, submissões filtradas, relatórios e revisão de lote foram
classificados como semanticamente distintos por diferença de contrato, filtro,
paginação, fonte, normalização, artefato, segurança ou workflow.

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
- [x] Cada alias técnico identificado declara a operação canônica e a sucessora
      preferida sem remover o nome registrado.
- [x] Nenhuma intent tool foi ocultada apenas por compartilhar um gateway ou
      função Moodle.

## Validação e evidências

```powershell
dotnet test tests/MoodleConnector.Application.Tests --filter "FullyQualifiedName~ToolMetadata|FullyQualifiedName~CognitiveExposure"
```

## Rollout e rollback

Marcar como deprecated antes de remover. Restaurar apenas aliases ainda usados, com evidência de telemetria.
