# Papéis e escopos

## Papéis e responsabilidades

| Papel | Responsabilidade | Não implica |
|---|---|---|
| Tutor | Acompanhar, corrigir e orientar | acesso institucional amplo |
| Monitor | Apoiar acesso e encaminhamentos | decisão ou alteração de nota |
| Gerente | Coordenar equipe e governança | acesso individual automático |
| Administrador | Configurar usuários, equipes, conexões e políticas | autorização acadêmica irrestrita |

## Escopos e entidades mínimas

O acesso efetivo é a interseção de identidade, papel, equipe, escopo, conexão Moodle, contexto acadêmico e capability remota. Entidades mínimas: `User`, `Team`, `TeamMembership`, `Role`, `Scope`, `MoodleConnection`, `CourseContext`, `StudentContext`, `ConnectorClient` e `AuditEvent`.

Exemplos de escopo: `moodle.read.courses`, `moodle.read.students`, `moodle.write.messages`, `moodle.write.assignments.feedback`, `moodle.write.assignments.grade` e `moodle.admin`. O legado `moodle.write` existe apenas para compatibilidade documentada.

## Convite e aceite

1. Administrador ou gerente autorizado cria convite para equipe, papel e escopos delimitados.
2. O convite é direcionado, expira e não concede acesso antes do aceite.
3. O destinatário autentica-se, revisa os limites e aceita explicitamente.
4. O sistema cria a associação auditável; revogação remove a associação sem apagar histórico.

## Regra central de autorização

Nenhuma operação é autorizada apenas pelo papel. Deve verificar identidade, associação ativa à equipe, escopo, conexão, contexto e capability Moodle. Escrita exige ainda flag, prévia, confirmação literal, idempotência e auditoria. Falha em qualquer elo bloqueia a operação, sem fallback silencioso para acesso amplo.
