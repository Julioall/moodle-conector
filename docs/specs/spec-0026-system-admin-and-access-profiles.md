# SPEC-0026: Administrador de sistema e perfis globais de acesso

## Status

Draft. Depende das fronteiras de autenticação da SPEC-0007 e complementa a
exposição capability-driven da SPEC-0013. Esta SPEC substitui o modelo de
grupos de permissões por usuário como fonte de autorização de produto; `Team`
continua sendo uma entidade organizacional independente.

## Objetivo

Substituir o rollout permissivo atual por um controle de acesso simples e
auditável:

- um único papel de sistema, `Admin`, resolvido no servidor pelo e-mail de
  bootstrap configurado;
- usuários comuns com no máximo um perfil de acesso global explícito e, na sua
  ausência, o perfil padrão global;
- permissões de aplicação verificadas no servidor tanto ao listar quanto ao
  executar tools;
- permissões locais que restringem, mas nunca ampliam, a conexão, os scopes e
  as capacidades concedidos pelo Moodle.

Ser administrador do Moodle Connector concede todas as permissões da
**aplicação**, mas não concede scopes OAuth, escrita em conexão ou capabilities
remotas que o token Moodle não possua.

## Veredito de viabilidade e correções da hipótese inicial

O recurso é viável como refatoração incremental: catálogo de permissões,
metadata `RequiredPlatformPermission`, claims de sessão/OAuth, mapeamento para
scopes e verificações de conexão já existem.

Há, porém, duas correções importantes que entram no escopo desta SPEC:

1. `CognitiveExposurePolicy` é global. Hoje `tools/list` filtra feature flags,
   exposição cognitiva, scopes OAuth e capabilities Moodle, mas não filtra a
   permission efetiva do usuário para cada tool.
2. O filtro de execução MCP confirma que a metadata tem uma permission e
   valida scopes OAuth, mas não compara diretamente
   `metadata.RequiredPlatformPermission` com o acesso efetivo. Para tornar
   “tool oculta” e “tool executável” consistentes, a autorização de execução
   precisa dessa verificação direta, fail-closed.

Portanto, scopes OAuth não substituem a autorização de produto. Eles continuam
sendo uma camada complementar e necessária.

## Contexto e evidência atual

- `PlatformPermissionService` calcula permissões por união de memberships e
  overrides. O seu `TemporaryRollout` adiciona todo o catálogo exceto
  `tool.teams.manage`; logo inclui `admin.view` e
  `tool.permission_groups.manage` para quase todas as contas.
- `EnsureDefaultPermissionsAsync` cria, por usuário, `Acesso inicial`, `Tutor`
  e `Monitor`. O primeiro recebe `tool.permission_groups.manage`; os dois
  últimos são definições editáveis por conta, não perfis globais.
- `UpdateGroupAsync` permite editar um grupo criado pelo ator **ou** do qual o
  ator é membro. Isso permitiria que um beneficiário editasse o próprio acesso
  quando a permissão de gestão estiver presente.
- `useSession.ts` deriva `isAdmin` de `tool.permission_groups.manage`, e
  `PortalEndpointAuthorization` usa a mesma permissão como atalho para
  `settings.view` e `admin.view`.
- A emissão OAuth já parte das permissões efetivas e remove scopes Moodle sem
  conexão ou de escrita quando `CanWrite=false`. Essa fronteira deve ser
  preservada.
- O schema atual possui `permission_groups`,
  `permission_group_permissions`, `permission_group_memberships` e
  `user_permission_overrides`; as migrações 013–023 registram sucessivos
  backfills temporários. O runtime ainda prevalece sobre esses dados ao aplicar
  `TemporaryRollout`.
- Chaves de API sem `UserAccount` associado ainda recebem `AllRead` e, quando
  aplicável, `AllWrite` a partir de `CanWrite`. Elas são uma rota de bypass que
  deve ser eliminada ou explicitamente isolada antes do rollout.

As evidências acima estão em
`PlatformPermissionService.cs`, `PortalAuthenticationEndpoints.cs`,
`PortalEndpointAuthorization.cs`, `OAuthAuthorizationEndpoints.cs`,
`Program.cs`, `McpRequestSecurityMiddleware.cs` e
`PlatformPermissionServiceTests.cs`.

## Decisão e arquitetura-alvo

### 1. Três conceitos independentes

```text
Usuário autenticado
        │
        ├── SystemRole: User | Admin
        └── Perfil de acesso global (0 ou 1 explícito; padrão como fallback)
                         │
                 Permissões efetivas da aplicação
                         │
          Portal API / UI / tools MCP / scopes OAuth
                         │
        conexão Moodle + CanWrite + token + capabilities remotas
```

`Team` e seus papéis continuam destinados a organização, convites e contexto;
não representam `SystemRole` nem concedem permissões de aplicação.

A decisão autorizativa de uma operação Moodle é:

```text
permissão efetiva da aplicação
AND permission exigida pela tool/endpoint
AND scope OAuth exigido
AND conexão Moodle vinculada e compatível com escrita
AND capability/autorização do token Moodle no contexto remoto
```

Uma negação em qualquer camada bloqueia a operação. A aplicação não emite
`moodle.admin` nem fabrica uma capability remota para um administrador local.

### 2. Papel de sistema e bootstrap

Introduzir:

```csharp
public enum SystemRole { User, Admin }
```

`BootstrapAdminResolver` lê exclusivamente no runtime do servidor
`AccessControl__BootstrapAdminEmail`. Ele normaliza configuração e e-mail da
conta com `Trim().ToLowerInvariant()` e retorna `Admin` somente na igualdade
exata. A variável pode vir de uma GitHub Repository Variable, desde que o
workflow a injete no container/runtime; ela nunca entra em bundle React,
resposta pública ou log.

Na primeira versão o bootstrap é a única forma de ser administrador. O papel
não é persistido nem editável pela interface. Se a configuração estiver ausente
ou vazia, não há administrador de bootstrap. A rotação ou remoção do e-mail
deve produzir efeito na próxima requisição autenticada, autorização OAuth e
chamada MCP: claims são transporte, não fonte de verdade.

`Admin` recebe `PlatformPermissionCatalog.All` no `EffectiveAccessResolver`.
Perfil, memberships e overrides legados não podem diminuir nem ampliar esse
papel. A persistência de administradores adicionais é fora de escopo.

### 3. Perfis de acesso globais

`PermissionGroup` passa a significar **Access Profile**, não um grupo que
usuários criam ou administram para si. O modelo lógico é:

```text
AccessProfile (global)
├── Id, Name, Description
├── IsDefault
├── IsSystem
└── Permissions[]

UserAccount
└── AccessProfileId?    // nulo = perfil padrão global
```

O schema final pode renomear fisicamente as tabelas legadas para
`access_profiles` e `access_profile_permissions`, preservando IDs e permissões
por `ALTER TABLE`; não será criada uma segunda cópia concorrente. A tabela
`permission_group_memberships` deixa de participar da autorização e será
retirada somente após a migração e a janela de rollback. `user_accounts` recebe
`AccessProfileId` anulável, FK para o perfil e índice. Há índice único parcial
para exatamente um `IsDefault=true`.

Não existe atualização em massa ao trocar o padrão:

```text
UserAccount.AccessProfileId ?? DefaultAccessProfileId
```

Trocar o padrão afeta imediatamente todos os usuários sem perfil explícito.
Um perfil marcado como padrão não pode ser removido; para removê-lo, o
administrador deve primeiro selecionar outro padrão. Perfil atribuído a usuários
não pode ser removido sem reatribuição explícita ou sem transferir seus usuários
ao padrão no mesmo comando transacional.

Na instalação são criados uma vez para toda a aplicação:

- `Usuário padrão` (`IsDefault=true`);
- `Tutor` e `Monitor` como pontos de partida editáveis (`IsSystem=false`).

O perfil `Usuário padrão` recebe todas as permissões atualmente atribuíveis a
usuários: portal (dashboard, cursos, escolas, alunos, follow-up, tarefas,
agenda, mensagens, correção, relatórios, conexões e configurações) e tools
normais de cursos, estudantes, sala, atividades/correção, mensagens, fóruns,
relatórios, memória, pedagogia e conexões, inclusive as escritas normais já
catalogadas. Ele não recebe `admin.view`,
`tool.permission_groups.manage`, `tool.pending_actions.manage` nem
`tool.teams.manage`.

`PlatformPermissionCatalog` expõe explicitamente `All`,
`ProfileAssignablePermissions` e `AdminOnlyPermissions`, sendo o último o
complemento do catálogo atual. Um perfil nunca pode aceitar uma permission de
`AdminOnlyPermissions`; validação de domínio e API recusam o payload, mesmo que
ele seja enviado manualmente. Novas permissions `admin.*` também entram nessa
classe por padrão.

### 4. Resolução e serviços

Substituir o uso de `IPlatformPermissionService` nas fronteiras por:

```csharp
public sealed record EffectiveAccess(
    Guid UserId,
    SystemRole SystemRole,
    Guid? ProfileId,
    string? ProfileName,
    IReadOnlySet<string> Permissions);

public interface IEffectiveAccessResolver
{
    Task<EffectiveAccess> ResolveAsync(Guid userId, CancellationToken ct);
}
```

`EffectiveAccessResolver` é a única fonte de permissões efetivas. Para `User`,
resolve o perfil explícito ou o padrão; para `Admin`, entrega `All`. Ele é
consultado na sessão de portal, emissão OAuth, enrichment de bearer/API key,
filtros MCP e autorização de endpoints. Nenhuma autorização sensível decide
apenas por uma claim emitida em login.

`IAccessProfileService` concentra listar, criar, editar, remover, definir
padrão, listar usuários e atribuir/remover perfil. Todas as suas mutações
exigem `SystemRole.Admin`. Não há equivalente de “membro do perfil pode editá-
lo”, nem CRUD de perfil para usuários comuns.

Concessões e negações diretas por usuário ficam fora do modelo v1. As tabelas e
endpoints legados não recebem novas escritas. A migração preserva exceções em
um perfil migrado quando necessário; a remoção física de
`user_permission_overrides` só acontece em release posterior, depois de
inventário sem pendências.

### 5. Sessão, portal e contratos HTTP

`GET /api/session` passa a retornar, de forma aditiva:

```json
{
  "user": {
    "id": "...",
    "name": "...",
    "roles": ["team-role-legado"],
    "systemRole": "admin",
    "profile": { "id": "...", "name": "Usuário padrão" },
    "permissions": ["..."]
  }
}
```

`roles` permanece somente para compatibilidade com `TeamMembership`; não é o
papel de sistema. Para administrador, `profile` é informativo (o perfil que
seria resolvido para um usuário comum) e não restringe `permissions`.

O frontend troca `isAdmin = canManagePermissionGroups` por
`user.systemRole === 'admin'`. `can(permission)` continua sendo usado para
funcionalidades normais. Menu e rota de administração usam `isAdmin`; a rota
deve ter `AdminRoute`, mas esconder UI é apenas UX. Endpoints administrativos
validam `SystemRole.Admin` no servidor e devolvem `403` para chamada manual.

Substituir os endpoints de grupo por endpoints administrativos versionados ou
nomes explícitos:

- `GET /api/admin/users` e `PUT /api/admin/users/{id}/access-profile`;
- `GET|POST /api/admin/access-profiles`;
- `PUT|DELETE /api/admin/access-profiles/{id}`;
- `PUT /api/admin/access-profiles/default`;
- `GET /api/admin/access-profiles/catalog`.

Durante uma release de compatibilidade, os endpoints
`/api/permission-groups` e `/api/users/{id}/platform-permissions` retornam
`410 access_control_migrated` para evitar que clientes antigos interpretem uma
alteração como sucesso. Os scripts de smoke/performance e o frontend são
migrados no mesmo release.

### 6. MCP, OAuth e serviços API

`CognitiveExposurePolicy` permanece responsável apenas pela exposição global
(produção, deprecated, internal e perfis cognitivos). Adicionar
`UserToolVisibilityPolicy`, aplicada depois dela, que verifica a permission
exigida da metadata contra `EffectiveAccess.Permissions` (ou claims recém-
enriquecidas derivadas dele):

```text
tools/list = feature habilitada
           AND exposição cognitiva global
           AND permission do usuário
           AND scope OAuth disponível
           AND capability Moodle disponível
```

O `CallToolFilter` repete a verificação de permission antes de delegar à tool.
Ausência de metadata, permission vazia, acesso não resolvido ou permission
ausente retornam erro estruturado `platform_permission_denied`; não há fallback
para tool escondida. O filtro continua validando scopes, conexão, capacidade e
fluxos de confirmação/escrita já existentes.

Na emissão OAuth, `ToolAuthorizationMapping.ScopesForPermissions` recebe as
permissões de `EffectiveAccess`, e os limites por conexão/`CanWrite` são
mantidos. Em enriquecimento de bearer, resolver novamente substitui claims
antigas. Uma API key vinculada a `UserAccount` usa o mesmo resolvedor.

Uma API key sem conta vinculada não pode continuar a receber `AllRead`/
`AllWrite` implicitamente de `CanWrite`: antes da ativação, cada chave deve ser
vinculada a uma conta ou classificada como identidade técnica com política
própria. Na v1, a alternativa segura é negar as tools regidas por perfil a
chaves não vinculadas e manter apenas endpoints explicitamente de infraestrutura
que tenham contrato separado. Isso exige inventário e comunicação de breaking
change no rollout.

### 7. Administração

A área única **Administração** contém:

- **Usuários**: nome, e-mail, perfil efetivo, perfil explícito/“padrão” e
  alteração de perfil;
- **Perfis**: lista, criação, edição, exclusão e marcação do padrão;
- **Configuração**: o perfil padrão ativo.

O editor mostra somente `ProfileAssignablePermissions`, agrupadas em Portal e
MCP. Permissions administrativas não são renderizadas como checkbox. A área
não administra o e-mail bootstrap nem cria novos administradores nesta versão.

## Escopo

- Remover `TemporaryRollout` e o bootstrap de grupos por usuário.
- Modelar `SystemRole`, `BootstrapAdminResolver`, `EffectiveAccess` e perfis
  globais com um único perfil explícito por conta.
- Criar/migrar o perfil padrão e os pontos de partida Tutor/Monitor globais.
- Restringir catálogo, APIs, UI, OAuth, `tools/list` e `tools/call` ao acesso
  efetivo.
- Corrigir o atalho atual que infere administrador de
  `tool.permission_groups.manage`.
- Migrar testes, smoke scripts e documentação de autorização.
- Inventariar e remover o bypass de API key não vinculada.

## Fora de escopo

- Persistir ou administrar múltiplos administradores pela interface.
- Atribuir vários perfis, somar permissões de perfis ou criar permissões por
  usuário na v1.
- Alterar os privilégios nativos Moodle, emitir `moodle.admin` ou compartilhar
  conexões/tokens Moodle entre usuários.
- Mudar a semântica organizacional de equipes, convites ou papéis Moodle.
- Redesenhar outros menus e páginas além da área de Administração e seus guards.

## Dependências e decisões em aberto

- SPEC-0007: sessão humana, OAuth MCP e portas de autenticação continuam
  separadas.
- SPEC-0013 e SPEC-0014: metadata determinística e exposição MCP devem
  permanecer canônicas; a filtragem por usuário é dinâmica e não altera o
  catálogo estático de submissão ChatGPT.
- ADR-0002 e `docs/architecture/roles-and-scopes.md` precisam ser atualizados
  quando a implementação for aprovada, pois ainda descrevem memberships e
  permissões diretas como fonte primária.
- Confirmar antes de implementar o destino de cada API key sem
  `UserAccount`: vincular, converter em identidade técnica explicitamente
  autorizada ou revogar. Não há rollout seguro sem essa decisão operacional.
- A implantação deve prover `AccessControl__BootstrapAdminEmail`; caso não
  exista, alguém com acesso operacional deve configurar o e-mail antes de usar
  Administração.

## Contratos, compatibilidade e migração

### Migração de dados

1. Criar o schema de perfis, `UserAccount.AccessProfileId`, FK e índice único
   parcial do padrão; nunca apagar tabelas legadas nesta etapa.
2. Criar o perfil padrão com `ProfileAssignablePermissions` e os perfis globais
   Tutor/Monitor. A criação é idempotente e não depende do primeiro login.
3. A associação automática `Acesso inicial` é sempre interpretada como
   baseline legado e converte para `AccessProfileId = null`, nunca como um
   perfil administrativo.
4. Migrar memberships explícitas de `Tutor`/`Monitor` para os perfis globais
   equivalentes quando houver exatamente uma escolha. Mais de um perfil, grupo
   customizado ou override direto gera registro de inventário auditável e um
   perfil global migrado deduplicado pela mesma combinação de permissions; a
   migration não concede nenhuma permission administrativa.
5. Negações legadas são traduzidas para o perfil migrado correspondente, para
   que uma restrição de segurança não desapareça. Concessões administrativas
   legadas são descartadas e registradas como removidas, pois a única fonte de
   administração passa a ser o bootstrap.
6. Só depois de reconciliação e backup verificado, desativar os endpoints e o
   código de memberships/overrides; a remoção de tabelas exige uma SPEC/etapa de
   cleanup posterior.

Os nomes de endpoints antigos não recebem alias silencioso. A resposta `410`
orienta a atualização do cliente e previne reintroduzir grants diretos.

### Revogação e cache

Sessões autenticadas e bearers são enriquecidos em cada entrada protegida com
o resolvedor atual. Mudanças de perfil, padrão ou e-mail bootstrap devem
invalidar o cache de sessão React e ter efeito em no máximo uma requisição no
servidor. Access tokens já emitidos podem conter claims antigas, mas a fronteira
MCP as revalida antes de listar/executar tool; não se espera expiração de token
para revogar uma permission local.

## Segurança, privacidade e observabilidade

- Só o servidor lê o e-mail bootstrap. Logs registram IDs de usuário e de
  perfil, nunca e-mail de bootstrap, tokens, senha ou conteúdo Moodle.
- Toda mutação administrativa é auditada com ator, alvo, perfil anterior/novo,
  conjunto de permissions por hash, data, resultado e correlation ID.
- Medir decisões de acesso por superfície (`portal`, `oauth`, `mcp-list`,
  `mcp-call`), `systemRole`, perfil, permission requerida e resultado; não
  registrar PII ou payload de tool.
- Alertar para: ausência de administrador bootstrap em produção, tentativa de
  permission administrativa em perfil, API key sem conta vinculada, falha de
  migração/reconciliação e aumento de `platform_permission_denied`.
- Toda permission de perfil tem validação server-side; a UI e scopes são
  defesas adicionais, nunca autoridade exclusiva.

## Plano de execução

1. Inventariar grupos, memberships, overrides e API keys não vinculadas em
   produção; aprovar o relatório de conversão e backup antes da migration.
2. Criar catálogo particionado, opções `AccessControl`, papel/resolvedor e
   testes unitários puros para bootstrap e acesso efetivo.
3. Criar migration versionada, seed global e rotina de conversão idempotente;
   validar em cópia sanitizada da base de produção.
4. Migrar autenticação de portal, OAuth e enrichment MCP para o resolvedor;
   remover `TemporaryRollout` e o atalho por `PermissionGroupsManage`.
5. Adicionar guard administrativo e endpoints `api/admin`; migrar tipos de
   sessão, gateway e UI de Administração.
6. Adicionar política de visibilidade por usuário e autorização direta no
   `CallToolFilter`; tratar API keys sem conta conforme a decisão aprovada.
7. Atualizar smoke/performance scripts, arquitetura, matriz de autorização e
   testes de integração; executar canário com auditoria reforçada.
8. Após a janela de estabilidade, tornar endpoints legados `410` e planejar a
   remoção física dos dados legados separadamente.

## Critérios de aceite

- [ ] Sem `AccessControl__BootstrapAdminEmail`, nenhuma conta tem
  `SystemRole.Admin`; igualdade normalizada concede o papel somente ao e-mail
  configurado e a alteração da configuração revoga/concede na próxima chamada.
- [ ] Um `Admin` possui `All` localmente, mas uma tool de escrita continua
  bloqueada quando scope, `CanWrite`, token ou capability Moodle não permitem a
  operação.
- [ ] Um usuário comum resolve exatamente um perfil efetivo; ausência de
  `AccessProfileId` usa o perfil padrão sem atualizar linhas de usuários.
- [ ] Nenhum perfil, endpoint de perfil ou payload manual consegue conceder
  permission administrativa; somente `SystemRole.Admin` pode administrar
  usuários e perfis.
- [ ] `TemporaryRollout`, a criação de `Acesso inicial` por login e a regra
  “criador ou membro pode editar” não participam mais de decisões de acesso.
- [ ] `GET /api/session` expõe `systemRole` e `profile`; `roles` de equipe não
  é usado para inferir administração no frontend ou backend.
- [ ] Endpoint administrativo chamado por usuário comum devolve `403`; rota e
  menu são inacessíveis visualmente para ele.
- [ ] `tools/list` omite tool sem a permission efetiva; `tools/call` manual à
  mesma tool devolve `platform_permission_denied` antes da execução.
- [ ] OAuth só delega scopes derivados do acesso efetivo e mantém os bloqueios
  de conexão inexistente/read-only e capability Moodle.
- [ ] API key não vinculada não obtém tools governadas por perfil apenas de
  `CanWrite`.
- [ ] Migration é repetível, preserva negações legadas, não migra privilégios
  administrativos e produz inventário auditável de conflitos.

## Validação e evidências

Testes automatizados mínimos:

```powershell
dotnet test tests/MoodleConnector.Application.Tests --filter "FullyQualifiedName~EffectiveAccess|FullyQualifiedName~BootstrapAdmin|FullyQualifiedName~AccessProfile|FullyQualifiedName~PlatformPermission"
dotnet test tests/MoodleConnector.Application.Tests --filter "FullyQualifiedName~McpJwtClaims|FullyQualifiedName~ToolExposure|FullyQualifiedName~OAuth"
npm --prefix src/MoodleConnector.Web test -- --runInBand
dotnet build MoodleConnector.sln --no-restore
```

Matriz obrigatória de integração:

| Cenário | Admin API/UI | Tool admin | Tool permitida | Operação além do token Moodle |
| --- | --- | --- | --- | --- |
| Bootstrap Admin | permite | permite | permite | bloqueia |
| User + padrão | 403/oculta | 403/oculta | permite conforme perfil | bloqueia |
| User + Tutor | 403/oculta | 403/oculta | somente o perfil | bloqueia |
| User + perfil restrito | 403/oculta | 403/oculta | somente o perfil | bloqueia |
| Chamada manual de tool oculta | — | 403 estruturado | — | — |
| API key sem conta | — | 403 estruturado | 403 estruturado, salvo contrato técnico explícito | — |

Evidências de rollout devem incluir backup/migração em cópia sanitizada,
contagem antes/depois de perfis e atribuições, inventário resolvido de conflitos,
comparação de `tools/list` por perfil, tentativa manual de `tools/call`, e
autorização/negação Moodle real para ao menos uma conexão read-only e uma com
capability ausente.

## Rollout e rollback

Publicar primeiro o resolvedor, auditoria e migration em modo de observação;
comparar permissões legadas e alvo sem bloquear chamadas. Depois da aprovação do
inventário, configurar o e-mail bootstrap, ativar autorização direta de tools e
por último a UI/rotas administrativas. A ativação que remove
`TemporaryRollout` só ocorre quando o perfil padrão, todas as contas e API keys
possuírem classificação válida.

Rollback antes do cleanup restaura a versão anterior e usa as tabelas legadas
intactas; a migration não apaga memberships nem overrides. Não se restaura
automaticamente `TemporaryRollout` em produção: em falha, o modo de contingência
mantém o perfil padrão de menor privilégio e exige decisão operacional para
qualquer concessão excepcional. Isso evita reabrir administração para todas as
contas como efeito colateral de rollback.
