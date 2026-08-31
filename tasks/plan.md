# Plano de implementação: workflows Moodle/SENAI

## Objetivo

Adicionar ao pacote distribuível do Moodle Connector skills de workflow que componham as
tools MCP existentes para apoiar contexto de curso, correção assistida, auditoria de notas,
recuperação, questionários GIFT e fechamento de unidade curricular.

## Decisões

- As skills orientam intenção e sequência; não concedem permissões.
- Consultas são o padrão.
- Toda escrita passa por prévia, revisão e confirmação humana.
- IDs, conexões, capabilities, páginas e estados devem ser preservados conforme retornados pelas tools.
- A skill não duplica autenticação, policy, registry, normalização ou confirmação implementadas no servidor.

## Fatias

1. Contexto, correção assistida e auditoria de notas.
2. Recuperação original, organização da recuperação e questionário GIFT.
3. Publicação segura de questionário e fechamento de unidade curricular.
4. Validação do pacote e revisão da documentação.

## Critérios de aceite

- Cada skill possui frontmatter válido com `name` e `description`.
- Cada skill aponta para as tools existentes e para as skills de domínio relacionadas.
- Nenhuma skill promete execução automática de escrita.
- Fluxos de correção e notas preservam evidência, incerteza e confirmação.
- O plugin passa no validador estrutural.
- O diff fica em branch própria e é entregue em PR.
