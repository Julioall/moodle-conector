# ADR-0002: acesso delimitado por equipe

## Status

Aceito como decisão documental e de direção arquitetural; implementação deve ser avaliada separadamente.

## Contexto

Tutor, monitor, gerente e administrador têm responsabilidades diferentes. Um papel global ou uma API key isolada não expressa equipe, curso, conexão Moodle e contexto de estudante com segurança suficiente.

## Decisão

O acesso será modelado por identidade + associação ativa à equipe + papel + escopo + contexto + conexão Moodle + capability remota. Convites concedem acesso somente após aceite explícito e são auditáveis. Nenhum papel, isoladamente, concede acesso irrestrito.

## Consequências

- Autorizações ficam explicáveis e revogáveis por equipe e contexto.
- Portal e MCP devem consumir a mesma política server-side.
- Persistência e contratos futuros precisam carregar referências de equipe/contexto quando o dado for operacional.
- Migração e implementação exigem revisão própria.
