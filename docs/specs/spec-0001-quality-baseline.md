# SPEC-0001: Baseline de qualidade e CI determinístico

## Status

Implementing.

## Objetivo

Fazer com que build, testes e evidências tenham resultado reproduzível a partir de uma árvore
limpa, sem depender de binários incrementais existentes.

## Contexto e evidência atual

Uma execução limpa em Release confirmou 748 testes .NET e 57 testes web aprovados. Falhas iniciais
de compilação vieram de artefatos antigos, não do código-fonte atual. A baseline numérica serve como
detector de regressão, não como promessa imutável de contagem.

## Escopo

- Consolidar comandos de build limpo e testes em CI.
- Produzir artefatos para catálogo de tools, plugin e benchmark.
- Proteger as evidências locais existentes em `.moodlebench/evidence/`.

## Fora de escopo

- Alterar comportamento funcional de tools ou portal.

## Plano de execução

1. Inspecionar workflows atuais e remover combinações inseguras de `--no-build` sem etapa prévia
   de build limpo.
2. Criar verificações de plugin e de consistência de catálogo quando as specs dependentes existirem.
3. Registrar os comandos canônicos de validação no checklist de release.

## Critérios de aceite

- [x] CI compila antes de executar testes com `--no-build`.
- [x] A suíte .NET e o portal são testados em configuração limpa.
- [x] Queda inesperada em testes ou tools expostas falha o pipeline.
- [ ] MCP Inspector é exigido para mudanças em contratos MCP.

## Validação e evidências

```powershell
dotnet restore MoodleConnector.slnx
dotnet build MoodleConnector.slnx --configuration Release --no-restore --no-incremental
dotnet test MoodleConnector.slnx --configuration Release --no-build --no-restore

Push-Location src/MoodleConnector.Web
npm ci
npm run typecheck
npm test
npm run build
Pop-Location
```

## Rollout e rollback

Cada alteração de workflow deve manter os gates já existentes e ser revertível em commit isolado.
