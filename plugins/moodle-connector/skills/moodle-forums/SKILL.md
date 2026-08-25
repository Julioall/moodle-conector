---
name: moodle-forums
description: Ler discussoes e posts de forums Moodle, analisar participacao e preparar ou confirmar publicacoes com escopo, capability e auditoria.
---

# moodle-forums

Use para leitura, participacao e publicacao em forums. Comece por `moodle-core` e consulte `moodle-pedagogy` antes de redigir uma intervencao pedagogica.

## Leitura

- Use `read_forum` para forums, discussoes e posts, apoiado por `mod_forum_get_forums_by_courses`, `mod_forum_get_forum_discussions` e `mod_forum_get_discussion_posts`.
- Use `list_students_without_forum_participation` para comparar estudantes ativos com autores observados em um forum.
- Controle pagina, grupo, discussion id e falhas por discussao. Um forum grande pode tornar a analise lenta e parcial.

## Interpretacao

Diferencie forum sem posts, posts fora do escopo, capability ausente, pagina incompleta e ausencia confirmada de participacao. Nao classifique estudante como desengajado apenas por nao aparecer em uma pagina ou quando roles/last access nao estiverem disponiveis.

## Publicacao controlada

1. Resolva curso, forum, discussao, grupo, autor e conexao.
2. Prepare com `create_forum_post_preview`.
3. Mostre destino, texto, contexto, pending action, expiracao e confirmacao literal.
4. Execute somente com `confirm_forum_post` apos confirmacao explicita.

O fluxo exige `CanWrite`, escopo e capability Moodle (`mod_forum_add_discussion` ou `mod_forum_add_discussion_post`), alem de policy, feature flag quando aplicavel e auditoria. Nao use `moodle_execute_read` nem marque uma pending action como concluida quando a escrita remota falhar.

Forum e follow-up nao enviam mensagens privadas automaticamente; para isso encaminhe a `moodle-messaging`.
