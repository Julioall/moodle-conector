# SPEC-0004: Catálogo único de tools e submissão

## Status

Implementing.

## Objetivo

Derivar inventário, schemas, hints e metadados de submissão a partir da mesma fonte de runtime.

## Contexto e evidência atual

O registro de código, o teste de exposição e `chatgpt-app-submission.json` têm contagens
divergentes. As duas tools de nota individual precisam de decisão explícita sobre exposição no
perfil de produção.

## Escopo

- Definir perfis de exposição por feature flag e ambiente.
- Gerar ou validar catálogo técnico, snapshots de `tools/list` e submission.
- Criar teste que falha em divergência de nome, schema, hints ou contagem.

## Fora de escopo

- Adicionar novas tools de negócio.

## Critérios de aceite

- [x] O perfil de produção declara a exposição das tools de nota individual de forma inequívoca.
- [x] Runtime, submission e testes possuem o mesmo conjunto de nomes, hints e output schemas por perfil de produção.
- [x] O bloco `tools` da submission é gerado dos contratos `McpServerToolAttribute`; os metadados editoriais permanecem revisados separadamente.
- [x] Uma mudança de tool exige atualização verificável no CI por `scripts/generate-chatgpt-app-submission.ps1 -Check`.

## Validação e evidências

- Executar `tools/list` contra a configuração de produção simulada.
- Rodar testes `ToolMetadataRegistryTests` e `ToolExposureValidationTests`.
- Comparar JSON gerado com `chatgpt-app-submission.json`.

## Rollout e rollback

Introduzir primeiro em modo somente validação. Depois promover a geração como fonte do artefato de
submissão em uma alteração isolada.
