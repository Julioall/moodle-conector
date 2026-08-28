# SPEC-0003: Distribuição e fonte única das skills

## Status

Implementing.

## Objetivo

Garantir que as skills de cursos, tarefas, fóruns, notas, mensagens, relatórios e workflows
pedagógico-operacionais sejam entregues pelo plugin e possam ser reproduzidas fora do checkout
deste repositório.

## Contexto e evidência atual

As onze skills de domínio foram movidas de `.agents/skills/` para o pacote distribuível em
`plugins/moodle-connector/skills/`. O pacote agora também contém oito skills de workflow para
contexto, correção, auditoria, recuperação, questionários e fechamento de unidade curricular.
Benchmark e validação de catálogo precisam consumir esse mesmo local para evitar que uma cópia local
não publicada mascare regressões.

## Decisão proposta

`plugins/moodle-connector/skills/` será a fonte canônica. O desenvolvimento local deverá usar o
plugin do repositório, e não uma cópia editável paralela em `.agents/skills/`.

## Escopo

- Manter as onze skills de domínio e adicionar os oito workflows preservando frontmatter, referências e limites de segurança.
- Atualizar referências internas e documentação de desenvolvimento.
- Validar hashes e descoberta em instalação limpa.

## Fora de escopo

- Reescrever o conteúdo pedagógico das skills sem uma spec funcional específica.

## Critérios de aceite

- [x] As skills existem somente em uma fonte canônica editável no pacote.
- [x] Cada `SKILL.md` passa pelo validador do pacote e pelo catálogo de referências.
- [x] Referências relativas resolvem dentro do pacote ou apontam para documentação pública estável.
- [ ] Uma conversa nova consegue descobrir os fluxos relevantes após instalação.

## Validação e evidências

- Executar o validador de plugin.
- Instalar o pacote em checkout limpo e acionar ao menos uma skill de cada domínio.
- Comparar manifest e hashes gerados em CI.

## Rollout e rollback

A origem antiga foi removida por `git mv`. O rollback restaura essa origem e as referências de
desenvolvimento no mesmo commit, sem alterar o servidor remoto ou dados persistidos.
