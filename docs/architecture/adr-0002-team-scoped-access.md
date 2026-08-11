# ADR-0002: acesso delimitado por equipe

## Status

Aceito como decisão documental e de direção arquitetural; implementação deve ser avaliada separadamente.

## Contexto

Tutor, monitor, gerente e administrador têm responsabilidades diferentes. Um papel global ou uma API key isolada não expressa equipe, curso, conexão Moodle e contexto de estudante com segurança suficiente.

## Decisão

O modelo separa duas necessidades:

- **Equipe:** agrupamento gerencial de tutores e monitores para acompanhamento, indicadores, relatórios e tomada de decisão.
- **Autorização da plataforma:** permissões atribuídas a um usuário ou a um grupo de permissões para controlar quais ferramentas e funções ele pode usar.

O acesso a uma ferramenta será modelado por identidade + permissões diretas e herdadas de grupos + contexto + conexão Moodle + capability remota. Convites continuam concedendo associação a equipes ou grupos somente após aceite explícito e são auditáveis. Nenhum papel ou grupo, isoladamente, concede acesso irrestrito.

A autorização efetiva é sempre a interseção entre a política local e as permissões/capabilities do Moodle:

```text
acesso efetivo = identidade
               ∩ equipe e contexto
               ∩ permissões/escopos locais
               ∩ capability e capability contextual do Moodle
```

Permissões locais podem restringir o que um usuário faz, mas nunca podem conceder uma operação que o token, a role ou a capability contextual do Moodle não permitam. Se o Moodle não autorizar a operação, ela deve ser bloqueada mesmo que o papel local possua o escopo correspondente.

## Descoberta de capabilities da conexão

Ao adicionar ou validar uma conexão, o Connector deve descobrir e registrar o perfil de capabilities que o token Moodle disponibiliza. Esse perfil representa o conjunto de operações possíveis naquela conexão, não uma concessão automática a todos os usuários da equipe.

O fluxo de decisão é:

1. a conexão Moodle fornece as capabilities observáveis;
2. o Connector normaliza esse perfil por operação e risco;
3. gerente/administrador consulta o perfil em modo somente leitura;
4. o gerente/administrador atribui permissões de ferramentas a usuários ou grupos apenas dentro do conjunto possível;
5. na execução, o Connector verifica novamente a capability contextual do Moodle.

Gerentes e administradores não podem usar esse fluxo para alterar roles, capabilities ou permissões no Moodle. A decisão local apenas restringe e organiza o uso do que a conexão já permite.

## Autorização de ferramentas da plataforma

Cada ferramenta ou função da aplicação deve declarar uma permissão estável, por exemplo:

- `tool.assignments.view` — visualizar atividades e submissões;
- `tool.assignments.grade` — preparar/revisar correção;
- `tool.messages.view` — visualizar histórico de mensagens;
- `tool.messages.send` — preparar e enviar mensagens;
- `tool.reports.view` — consultar relatórios e indicadores.

Essas permissões podem ser atribuídas a um usuário específico ou a um grupo de permissões. O usuário pode pertencer a vários grupos. Uma revogação explícita de usuário deve prevalecer sobre uma concessão herdada de grupo. A permissão efetiva da aplicação é calculada antes da verificação da capability Moodle.

## Consequências

- Autorizações ficam explicáveis e revogáveis por equipe e contexto.
- Portal e MCP devem consumir a mesma política server-side.
- Persistência e contratos futuros precisam carregar referências de equipe/contexto quando o dado for operacional.
- Migração e implementação exigem revisão própria.

## Implementação inicial

O primeiro slice está implementado: tabelas de equipes, associações e convites; equipe pessoal criada para contas novas e migradas; papéis e escopos persistidos; convite com token armazenado somente como hash; aceite condicionado ao e-mail autenticado; endpoints `/api/teams` e `/api/team-invitations/accept`; e propagação de papel/equipe/escopos para a identidade do portal e do MCP. A separação entre equipe gerencial e grupos de permissões de ferramentas é a próxima evolução do modelo. Os gateways e registries Moodle continuam sendo a autoridade final sobre capabilities remotas.

Ainda não é objetivo deste slice migrar todas as consultas acadêmicas para receber `team_id` explícito. As próximas ondas devem aplicar o contexto da equipe às conexões Moodle, cursos e entidades operacionais antes de ampliar acesso entre equipes.
