# SPEC-0010: Certificação de release do plugin

## Status

 Implementing.

## Objetivo

Transformar as validações de plugin, MCP, portal, segurança e documentação em um processo de
release repetível e auditável.

## Dependências e decisões em aberto

Requer as evidências internas das SPECS-0001 a 0009, aprovação do proprietário do plugin, licença
MediatR configurada como secret, conexão MCP registrada no ChatGPT e ambiente HTTPS de homologação
para as etapas externas.

## Fora de escopo

- Publicar em marketplace público ou fazer deploy em produção sem aprovação explícita.

## Escopo

- Consolidar checklist, versão, submission, artefatos e plano de rollback.
- Validar instalação limpa, OAuth, tools, UI MCP, skills e fluxos de escrita confirmada.
- Registrar evidências de CI e revisão humana.

## Critérios de aceite

- [x] O verificador pós-deploy valida health, headers, versão, OAuth discovery, JWKS e inicialização MCP.
- [ ] Plugin instala em ambiente limpo pelo marketplace aprovado.
- [x] Catálogo, submission e runtime coincidem no perfil publicado.
- [ ] MCP Inspector, testes .NET, portal e benchmark passam.
- [ ] Fluxos de escrita exigem confirmação e autorização corretas.
- [ ] Rollback do pacote e do servidor foi ensaiado ou documentado com evidência.

## Validação e evidências

Executar [a certificação operacional](../operations/release-certification.md), anexar artefatos de
CI e registrar a versão do pacote, da conexão MCP e do servidor.

A paridade local é verificada por `scripts/generate-chatgpt-app-submission.ps1 -Check` e pelos
testes de exposição de tools; a paridade do perfil publicado será reexecutada pelo gate pós-deploy.

## Rollout e rollback

Publicar primeiro em homologação ou marketplace de equipe; promover após aprovação. Reverter para
a versão anterior do pacote e do servidor, preservando compatibilidade de schemas.
