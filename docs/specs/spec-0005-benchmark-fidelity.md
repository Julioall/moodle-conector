# SPEC-0005: Fidelidade de benchmark de MCP e skills

## Status

Implementing.

## Objetivo

Medir a diferença real entre usar somente MCP e usar o plugin instalado com suas skills.

## Contexto e evidência atual

O benchmark atual calcula uma `SelectedSkill`, mas o prompt do sistema é fixo e os arquivos de
skill são usados como manifest ou hash. Assim, o relatório não prova que o conteúdo da skill foi
aplicado durante a execução.

## Escopo

- Criar cenários `mcp-only` e `plugin-with-skills`.
- Injetar conteúdo real das skills ou executar contra plugin instalado.
- Persistir versão, hash, prompt efetivo, tools disponíveis e resultado por caso.

## Fora de escopo

- Alterar prompts de produto ou métricas para favorecer artificialmente um cenário.

## Dependências

- Fonte canônica das skills definida na SPEC-0003.
- Credenciais e quota OpenAI apenas para executar benchmarks remotos, nunca para gerar o perfil.

## Critérios de aceite

- [x] Toda seleção de skill tem conteúdo carregado comprovável.
- [x] Cada resultado informa cenário, versão e hash da skill.
- [x] Remover uma skill causa falha detectável no cenário correspondente.
- [ ] Relatórios são reproduzíveis e não incluem PII nem segredos.

## Validação e evidências

- Executar a suíte com os dois cenários e comparar métricas de seleção, tool-call e conclusão.
- Inspecionar amostra de prompts efetivos e resultados redigidos.

## Rollout e rollback

Executar em paralelo ao benchmark atual até validar a comparabilidade; depois tornar o novo
formato obrigatório em CI de benchmark.
