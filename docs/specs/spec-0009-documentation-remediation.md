# SPEC-0009: Saneamento documental e encoding

## Status

 Validated.

## Objetivo

Corrigir mojibake, links quebrados e documentos concorrentes sem apagar histórico útil.

## Escopo

- Corrigir encoding e texto corrompido, começando pelo README.
- Remover referências ao arquivo de tarefas inexistente do repositório.
- Atualizar índices, inventário documental e links para specs/ADRs.
- Arquivar documentos substituídos conforme o padrão do repositório.

## Fora de escopo

- Reescrever documentos históricos em `docs/archive/` ou arquivos de evidência não canônicos.

## Dependências

- Nenhuma dependência de runtime; o script de validação usa PowerShell e o checkout do repositório.

## Critérios de aceite

- [x] Documentos canônicos possuem UTF-8 válido e leitura humana correta.
- [x] Links locais internos resolvem e o README não referencia arquivo inexistente.
- [x] Histórico permanece em `docs/archive/` sem ser reescrito por este saneamento.

## Validação e evidências

- Executar verificador de links e busca por padrões conhecidos de mojibake.
- Revisar `docs/README.md` e `docs/documentation-audit.md`.
- Evidência automatizada: `scripts/check-documentation.ps1`.

## Rollout e rollback

Correções são textuais e divididas por área. Cada commit pode ser revertido sem alterar runtime.
