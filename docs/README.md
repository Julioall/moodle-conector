# Documentação do Moodle Connector

Este é o ponto de entrada da documentação. Comece pela visão do produto e consulte apenas a área necessária.

## Comece aqui

1. [Visão de produto e arquitetura](product/product-architecture-review.md)
2. [Roadmap](roadmap.md)
3. [Papéis e escopos](architecture/roles-and-scopes.md)
4. [ADR-0002 — acesso delimitado por equipe](architecture/adr-0002-team-scoped-access.md)
5. [Padrão para novos documentos](documentation-standard.md)

## Áreas canônicas

| Necessidade | Documento principal |
|---|---|
| Entender o produto | `product/product-architecture-review.md` |
| Saber prioridades e jornadas | `roadmap.md` |
| Entender arquitetura | `architecture/` |
| Entender segurança e acesso | `security/auth-and-scopes.md` e `architecture/roles-and-scopes.md` |
| Configurar e operar localmente | `operations/` |
| Integrar com Moodle | `technical/moodle-webservice-setup.md` |
| Consultar tools MCP | `technical/mcp-tools-catalog.md` |
| Entender contratos técnicos | `technical/` |
| Consultar histórico | `archive/` |

## Regra de leitura

Documentos em `architecture`, `product`, `security`, `operations` e `technical` são referências ativas. Documentos em `archive` preservam decisões, planos, auditorias e evidências antigas; não devem ser tratados como instruções atuais sem confirmação no conteúdo ativo.

O [inventário da auditoria documental](documentation-audit.md) registra a origem e o status de cada documento.

## Fluxo recomendado

```text
Visão do produto → arquitetura e autorização → operação/técnica → histórico, quando necessário
```
