# Papéis e escopos

## Equipes gerenciais e autorização

`Team` representa um agrupamento gerencial de tutores e monitores. Serve para acompanhamento, indicadores, relatórios e tomada de decisão; pertencer a uma equipe não deve, por si só, liberar todas as ferramentas.

O acesso às ferramentas usa grupos de permissões e concessões diretas ao usuário. Um grupo pode permitir correção de atividades, enquanto outro permite apenas visualização. A mesma permissão também pode ser concedida ou revogada diretamente para um usuário.

## Papéis e responsabilidades

| Papel | Responsabilidade | Não implica |
|---|---|---|
| Tutor | Acompanhar, corrigir e orientar | acesso institucional amplo |
| Monitor | Apoiar acesso e encaminhamentos | decisão ou alteração de nota |
| Gerente | Coordenar equipe e governança | acesso individual automático |
| Administrador | Configurar usuários, equipes, conexões e políticas | autorização acadêmica irrestrita |

## Escopos e entidades mínimas

O acesso efetivo é a interseção de identidade, permissões diretas ou herdadas de grupo, contexto, conexão Moodle e capability remota. A política local pode restringir, mas nunca ampliar a autorização concedida pelo Moodle. Entidades mínimas: `User`, `Team`, `TeamMembership`, `PermissionGroup`, `PermissionGroupMembership`, `UserPermissionGrant`, `UserPermissionDeny`, `Permission`, `MoodleConnection`, `CourseContext`, `StudentContext`, `ConnectorClient` e `AuditEvent`.

Exemplos de escopo técnico: `moodle.read.courses`, `moodle.read.students`, `moodle.write.messages`, `moodle.write.assignments.feedback`, `moodle.write.assignments.grade` e `moodle.admin`. Exemplos de permissões de ferramenta: `tool.assignments.grade`, `tool.messages.send` e `tool.reports.view`. O legado `moodle.write` existe apenas para compatibilidade documentada.

Cada `MoodleConnection` deve possuir um perfil de capabilities descobertas. Esse perfil é a fronteira superior das permissões locais: papéis e escopos podem selecionar um subconjunto, nunca conceder algo que não esteja disponível no Moodle. Gerentes e administradores consultam esse perfil em modo somente leitura para tomar decisões de configuração.

## Convite e aceite

1. Administrador ou gerente autorizado cria convite para equipe, papel e escopos delimitados.
2. O convite é direcionado, expira e não concede acesso antes do aceite.
3. O destinatário autentica-se, revisa os limites e aceita explicitamente.
4. O sistema cria a associação auditável; revogação remove a associação sem apagar histórico.

## Regra central de autorização

Nenhuma operação é autorizada apenas pelo papel ou pela equipe. Deve verificar identidade, concessões diretas/grupais, revogações explícitas, conexão, contexto e capability Moodle. A capability contextual do Moodle é decisória: ausência, desconhecimento ou falha na verificação bloqueia a operação; não há fallback para um grupo local mais amplo. Escrita exige ainda flag, prévia, confirmação literal, idempotência e auditoria.
