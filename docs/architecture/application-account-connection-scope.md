# Arquitetura de contas, gerentes e conexões Moodle

**Status:** decisão arquitetural registrada para implementação futura.

## Objetivo

O Portal será uma aplicação multiusuário em que cada professor ou monitor cadastra e utiliza suas próprias conexões Moodle. A administração acompanha os dados produzidos por esses usuários por meio de gerentes e equipes, sem transformar a conexão Moodle no tenant ou na única regra de autorização.

## Hierarquia de domínio

```text
Administrador
└── visualiza Gerentes e suas equipes
    └── Gerente
        └── Equipe
            └── Professores e monitores
                └── Conexões Moodle próprias
                    └── Cursos, alunos e dados de acompanhamento
```

Os papéis são responsabilidades da aplicação:

- **Professor/Monitor:** cadastra e usa as próprias conexões Moodle;
- **Gerente:** acompanha as equipes sob sua responsabilidade;
- **Administrador:** visualiza gerentes, equipes e dados autorizados da plataforma.

Nenhum desses papéis deve ser modelado como uma conexão Moodle.

## Fronteiras de dados

`UserAccount` é o proprietário inicial da conexão e dos dados gerados por ela. `MoodleConnection` representa um ambiente externo pertencente a essa conta.

Os dados dependentes de Moodle devem preservar a origem:

```text
OwnerUserId
ConnectionId
ExternalResourceId
```

Exemplos de dados com escopo de conexão:

- cursos e alunos;
- pendências e tarefas Moodle;
- agenda relacionada ao ambiente;
- mensagens;
- indicadores e resumos;
- memórias e documentos associados ao Moodle.

IDs externos nunca devem ser usados isoladamente. A identidade de um curso, por exemplo, é composta por `ConnectionId + ExternalCourseId`.

## Autorização

`ConnectionId` identifica o ambiente, mas não concede acesso. A autorização deve validar:

```text
usuário autenticado
  + conexão solicitada
  + propriedade ou concessão de acesso
  + permissão de leitura/escrita
```

Para professor ou monitor, a regra inicial será:

```text
Connection.OwnerUserId == CurrentUserId
```

Para gerentes e administradores, o acesso será concedido por escopo explícito, equipe ou papel administrativo e deverá ser auditado. O backend nunca deve confiar somente no `connectionId` recebido pela URL.

## Seleção de ambiente no Portal

O usuário pode trocar a conexão selecionada. Essa seleção define o ambiente de todas as telas operacionais:

- Cursos;
- Alunos;
- Pendências;
- Tarefas;
- Agenda;
- Mensagens;
- Resumo da semana;
- Relatórios.

As telas não devem agregar conexões implicitamente. Quando uma visão agregada for necessária para a administração, ela deverá declarar explicitamente o conjunto de conexões autorizado.

## Administração e coleta

O Portal deve preferir dados coletados e armazenados com a origem do usuário e da conexão. Assim, administradores podem consultar o acompanhamento de gerentes e equipes sem reutilizar credenciais Moodle de terceiros de forma implícita.

Uma consulta em tempo real usando a conexão de outro usuário exigirá concessão explícita, auditoria e controle separado de leitura e escrita.

## Migração planejada

Antes de substituir `connectionRef` por `connectionId` nas rotas e contratos:

1. manter `UserAccount` como proprietário da conexão;
2. adicionar/verificar `OwnerUserId` nos vínculos de conexão;
3. usar `ConnectionId` como identificador permanente do ambiente;
4. separar alias e URL da identidade técnica;
5. adicionar entidades de equipes, associações de usuários e concessões de acesso;
6. incluir `ConnectionId` nos dados operacionais que ainda não possuem escopo;
7. migrar as telas para transportar `connectionId` e validar autorização no backend;
8. preservar auditoria de acessos administrativos.

Essa decisão registra o modelo e não implementa ainda gerentes, equipes ou concessões administrativas.
