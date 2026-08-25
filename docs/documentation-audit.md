# Auditoria documental

## Objetivo e critérios

Este inventário registra o estado da documentação no primeiro passe. O objetivo é melhorar localização, autoridade e rastreabilidade sem apagar conhecimento útil. Nenhum código de produção é escopo desta auditoria.

Critérios: autoridade, atualidade, localização, não duplicação e rastreabilidade. Histórico é movido com `git mv`; nenhuma informação é descartada.

## Estrutura alvo

| Pasta | Escopo |
|---|---|
| `architecture` | decisões, limites, integração e autorização |
| `product` | visão, papéis, capacidades e fronteiras |
| `security` | autenticação, escopos e release gates |
| `operations` | deploy, troubleshooting e operação |
| `technical` | contratos, setup e modelos de implementação |
| `specs` | contratos ativos de execução, critérios de aceite e evidências |
| `archive` | relatórios, auditorias e planos históricos |

Os documentos temáticos foram realocados para a taxonomia alvo. Planos e specs de trabalho foram preservados em `archive/superpowers`; arquivar não significa descartar conhecimento.

## Inventário e ação recomendada

| Documento/pasta | Status | Ação |
|---|---|---|
| `README.md`, `DEPLOY.md` | Entradas e operação de raiz | Manter; encoding, links e defaults operacionais verificados. |
| `docs/roadmap.md` | Fonte canônica de prioridades | Manter e atualizar para papéis, equipes e camadas. |
| `docs/architecture/*` | Referência arquitetural | Manter; adicionar papéis/escopos e ADR-0002. |
| `docs/security/*` | Referência de segurança | Manter; comparar divergências com `technical/security-model.md`. |
| `docs/operations/*`, `docs/technical/*` | Runbooks/referência técnica | Manter; relatório de release foi arquivado. |
| `docs/archive/audits/moodle-connector-verification.md` | Auditoria datada | Movido para `docs/archive/audits/` com `git mv`. |
| `docs/archive/release-reports/moodle-connector-release-report.md` | Relatório de release datado | Movido para `docs/archive/release-reports/` com `git mv`. |
| `docs/archive/superpowers/*` | Planos/specs de trabalho históricos | Preservar no arquivo, com referências atualizadas. |
| `docs/product/*`, `docs/security/*`, `docs/technical/*`, `docs/operations/*` | Referências temáticas consolidadas | Manter nas pastas-alvo. |

### Inventário por arquivo

| Arquivo | Status | Ação |
|---|---|---|
| `README.md` | Entrada principal | Encoding UTF-8 e links locais verificados. |
| `DEPLOY.md` | Operação de raiz | Manter como atalho para deploy; rollback canônico no runbook. |
| `docs/architecture/adr-0001-capability-driven-moodle-api.md` | ADR vigente | Manter. |
| `docs/architecture/chatgpt-app-oauth.md` | Arquitetura OAuth | Manter. |
| `docs/architecture/pending-actions.md` | Arquitetura de confirmação | Manter. |
| `docs/architecture/skill-registry-exposure.md` | Arquitetura MCP | Manter. |
| `docs/architecture/tool-risk-levels.md` | Classificação de risco | Manter. |
| `docs/architecture/adr-0002-team-scoped-access.md` | Nova decisão | Manter como ADR de acesso. |
| `docs/architecture/adr-0003-plugin-mcp-portal-boundaries.md` | Decisão de fronteiras | Manter como ADR de produto e integração. |
| `docs/specs/*` | Specs ativas | Manter como fonte de execução e rastreabilidade. |
| `docs/architecture/roles-and-scopes.md` | Novo modelo | Manter como referência de papéis/escopos. |
| `docs/security/auth-and-scopes.md` | Segurança | Manter; alinhar nomenclatura futuramente. |
| `docs/security/release-checklist.md` | Gate operacional | Manter. |
| `docs/operations/deploy-runbook.md` | Runbook | Manter. |
| `docs/operations/release-certification.md` | Certificação de release | Manter como procedimento operacional da SPEC-0010. |
| `docs/operations/troubleshooting-runbook.md` | Runbook | Manter. |
| `docs/technical/audit-model.md` | Modelo técnico | Manter. |
| `docs/technical/local-setup.md` | Setup | Manter. |
| `docs/technical/mcp-tools-catalog.md` | Catálogo | Manter; revisar geração/atualidade futuramente. |
| `docs/technical/moodlebench.md` | Benchmark | Manter. |
| `docs/technical/moodle-webservice-setup.md` | Integração Moodle | Manter. |
| `docs/technical/security-model.md` | Segurança técnica | Manter; reconciliar com `security/`. |
| `docs/product/message-flow.md` | Fluxo funcional | Manter em `product/`. |
| `docs/security/privacy-best-practices.md` | Privacidade funcional | Manter em `security/`. |
| `docs/technical/tool-response-contract.md` | Contrato MCP | Manter em `technical/`. |
| `docs/operations/portal-v2-local.md` | Operação local do portal | Manter em `operations/`. |
| `docs/roadmap.md` | Roadmap canônico | Manter; atualizado neste passe. |
| `docs/product/product-architecture-review.md` | Nova visão consolidada | Manter como referência de produto. |
| `docs/archive/audits/moodle-connector-verification.md` | Auditoria histórica | Preservar no arquivo. |
| `docs/archive/release-reports/moodle-connector-release-report.md` | Relatório histórico | Preservar no arquivo. |
| `docs/archive/superpowers/001-portal-v2-product-spec.md` | Especificação | Preservar no arquivo. |
| `docs/archive/superpowers/002-portal-v2-design-system-and-claris-migration.md` | Plano de design | Preservar no arquivo. |
| `docs/archive/superpowers/003-portal-v2-implementation-plan.md` | Plano de implementação | Preservar no arquivo. |
| `docs/archive/superpowers/004-portal-v2-verification-checklist.md` | Checklist histórico | Preservar no arquivo. |
| `docs/archive/superpowers/005-portal-v2-api-contracts.md` | Contratos | Preservar no arquivo. |
| `docs/archive/superpowers/plans/2026-07-06-classificacao-participantes.md` | Plano datado | Preservar no arquivo. |
| `docs/archive/superpowers/plans/2026-07-07-memoria-e-orientacoes-pedagogicas.md` | Plano datado | Preservar no arquivo. |
| `docs/archive/superpowers/plans/2026-07-07-roadmap-guia-tutor-capabilities.md` | Plano datado | Preservar no arquivo. |
| `docs/archive/superpowers/specs/2026-07-03-classificacao-participantes-design.md` | Spec datada | Preservar no arquivo. |
| `docs/archive/superpowers/specs/2026-07-06-memoria-e-orientacoes-pedagogicas-design.md` | Spec datada | Preservar no arquivo. |
| `docs/archive/superpowers/specs/2026-07-07-roadmap-guia-tutor-capabilities-design.md` | Spec datada | Preservar no arquivo. |

## Divergências preservadas

Documentos de segurança descrevem escopos e defaults com níveis de detalhe diferentes; não foi feita alteração de código nem arbitrada uma nova política. Arquivar não significa considerar achados históricos resolvidos. Itens planejados não são tratados como funcionalidades implementadas.

## Resultado deste passe

Criados os documentos de visão, papéis/escopos e ADRs; roadmap atualizado; auditoria e relatório
de release antigos foram movidos para `archive`. O README foi normalizado para UTF-8, a ADR-0002
recebeu o nome canônico e o verificador `scripts/check-documentation.ps1` cobre links locais,
UTF-8 e padrões conhecidos de mojibake. Nenhum documento histórico foi apagado.
