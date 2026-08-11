# Memória e Orientações Pedagógicas Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persistir aprendizados reutilizáveis por usuário e expor busca segura nos guias de `public/pedagogic`, orientando clientes MCP a usar ambos automaticamente.

**Architecture:** A camada Application concentra validação e casos de uso; Infrastructure persiste memórias no PostgreSQL e indexa Markdown localmente; Presentation expõe duas tools MCP e instruções de inicialização. A identidade vem exclusivamente de `ICurrentUserContext.Subject`, e a busca pedagógica não aceita caminhos do cliente.

**Tech Stack:** .NET 10, ASP.NET Core, ModelContextProtocol 1.3, EF Core 10, Npgsql/PostgreSQL, xUnit.

---

## Mapa de arquivos

- Criar `src/MoodleConnector.Domain/UserMemory.cs`: entidade e constantes de categoria/origem.
- Criar `src/MoodleConnector.Application/Abstractions/IUserMemoryRepository.cs`: contrato de persistência filtrado por proprietário.
- Criar `src/MoodleConnector.Application/Memory/UserMemoryService.cs`: validação, normalização, segredo, upsert, listagem e remoção.
- Criar `src/MoodleConnector.Application/Pedagogy/IPedagogicGuidanceSearch.cs`: contrato e DTOs de busca.
- Criar `src/MoodleConnector.Infrastructure/Persistence/UserMemoryRepository.cs`: implementação EF Core.
- Criar `src/MoodleConnector.Infrastructure/Pedagogy/MarkdownPedagogicGuidanceSearch.cs`: índice e ranking determinístico.
- Criar `src/MoodleConnector.Infrastructure/Database/Scripts/005_user_memories.sql`: tabela e índices.
- Criar `src/MoodleConnector.Presentation/Tools/Memory/MoodleMemoryTools.cs`: tool única de gerenciamento.
- Criar `src/MoodleConnector.Presentation/Tools/Pedagogy/MoodlePedagogyTools.cs`: tool de consulta aos guias.
- Modificar DI, `ConnectorDbContext`, inicializador de schema, `Program.cs`, csproj de Presentation, Dockerfile, catálogo MCP e testes.

### Task 1: Modelo, contrato e serviço de memória

**Files:**
- Create: `src/MoodleConnector.Domain/UserMemory.cs`
- Create: `src/MoodleConnector.Application/Abstractions/IUserMemoryRepository.cs`
- Create: `src/MoodleConnector.Application/Memory/UserMemoryService.cs`
- Test: `tests/MoodleConnector.Application.Tests/Memory/UserMemoryServiceTests.cs`
- Modify: `src/MoodleConnector.Application/DependencyInjection.cs`

- [ ] **Step 1: Escrever testes falhando para upsert, escopos e isolamento**

Criar um repositório fake que indexe por `OwnerSubject`, `Category`, `MoodleAlias`, `CourseId` e `NormalizedKey`. Cobrir: salvar global; atualizar a mesma chave; listar global + Moodle + curso; não retornar memória de outro subject; impedir `courseId` sem alias; remover somente memória do usuário atual.

```csharp
[Fact]
public async Task SaveAsync_AtualizaMemoriaEquivalenteSemDuplicar()
{
    var fixture = new Fixture("teacher-1");
    var first = await fixture.Service.SaveAsync(
        new SaveUserMemoryRequest("preferencia", "formato-relatorio", "Use tabelas curtas.", "explicit", null, null), default);
    var second = await fixture.Service.SaveAsync(
        new SaveUserMemoryRequest("preferencia", "formato relatorio", "Use tabelas objetivas.", "inferred", null, null), default);

    Assert.Equal(first.Id, second.Id);
    Assert.Equal("Use tabelas objetivas.", second.Content);
    Assert.Single(fixture.Repository.Items);
}
```

- [ ] **Step 2: Rodar o teste e confirmar RED**

Run: `dotnet test tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj --filter FullyQualifiedName~UserMemoryServiceTests`

Expected: FAIL por tipos `UserMemoryService`/`SaveUserMemoryRequest` inexistentes.

- [ ] **Step 3: Implementar o domínio e contrato mínimos**

`UserMemory` deve ter UUID, `OwnerSubject`, `Category`, `NormalizedKey`, `Content`, `Origin`, `MoodleAlias`, `CourseId`, `CreatedAtUtc` e `UpdatedAtUtc`. Expor `Update(content, origin, now)`. O repositório deve oferecer `FindEquivalentAsync`, `ListAsync`, `FindOwnedAsync`, `AddAsync`, `Remove` e `SaveChangesAsync`; nenhuma consulta recebe proprietário opcional.

- [ ] **Step 4: Implementar `UserMemoryService`**

Usar limites: chave 120, conteúdo 1000, alias 64, course ID 64, listagem padrão 20 e máximo 50. Normalizar chaves removendo diacríticos, convertendo separadores em hífen e usando minúsculas. Validar categorias `preferencia|caminho|correcao|decisao`, origens `explicit|inferred`, identidade não vazia e escopo. Rejeitar padrões case-insensitive `password`, `senha`, `token`, `api[_-]?key`, `secret`, `cookie`, `Bearer `, `sk-` e JWT de três segmentos.

```csharp
public async Task<UserMemoryDto> SaveAsync(SaveUserMemoryRequest request, CancellationToken ct)
{
    var owner = RequireOwner();
    var normalized = ValidateAndNormalize(request);
    var existing = await repository.FindEquivalentAsync(owner, normalized.Category,
        normalized.MoodleAlias, normalized.CourseId, normalized.Key, ct);
    if (existing is null) repository.Add(UserMemory.Create(owner, normalized, clock.GetUtcNow()));
    else existing.Update(normalized.Content, normalized.Origin, clock.GetUtcNow());
    await repository.SaveChangesAsync(ct);
    return Map(existing ?? created);
}
```

- [ ] **Step 5: Registrar o serviço e confirmar GREEN**

Adicionar `services.AddScoped<IUserMemoryService, UserMemoryService>();` em Application DI. Rodar o filtro anterior; esperado: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/MoodleConnector.Domain/UserMemory.cs src/MoodleConnector.Application/Abstractions/IUserMemoryRepository.cs src/MoodleConnector.Application/Memory/UserMemoryService.cs src/MoodleConnector.Application/DependencyInjection.cs tests/MoodleConnector.Application.Tests/Memory/UserMemoryServiceTests.cs
git commit -m "feat: adicionar servico de memoria por usuario"
```

### Task 2: Persistência PostgreSQL

**Files:**
- Create: `src/MoodleConnector.Infrastructure/Persistence/UserMemoryRepository.cs`
- Create: `src/MoodleConnector.Infrastructure/Database/Scripts/005_user_memories.sql`
- Modify: `src/MoodleConnector.Infrastructure/Persistence/ConnectorDbContext.cs`
- Modify: `src/MoodleConnector.Infrastructure/Persistence/ConnectorDbContextSchemaInitializer.cs`
- Modify: `src/MoodleConnector.Infrastructure/DependencyInjection.cs`
- Test: `tests/MoodleConnector.Application.Tests/Infrastructure/SchemaScriptTests.cs`

- [ ] **Step 1: Escrever teste falhando do schema 005**

```csharp
[Fact]
public async Task UserMemorySchema_DeveConterTabelaRestricaoERegistroDeVersao()
{
    var root = Path.GetDirectoryName(typeof(ConnectorDbContext).Assembly.Location)!;
    var sql = await File.ReadAllTextAsync(Path.Combine(root, "Database", "Scripts", "005_user_memories.sql"));
    Assert.Contains("user_memories", sql);
    Assert.Contains("OwnerSubject", sql);
    Assert.Contains("NormalizedKey", sql);
    Assert.Contains("NULLS NOT DISTINCT", sql);
    Assert.Contains("VALUES (5, 'user memories'", sql);
}
```

- [ ] **Step 2: Rodar e confirmar RED**

Run: `dotnet test tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj --filter FullyQualifiedName~UserMemorySchema`

Expected: FAIL porque o script não existe.

- [ ] **Step 3: Criar schema idempotente e mapeamento EF**

Criar tabela `user_memories` com colunas citadas no modelo, checks de categoria/origem, índice de consulta `(OwnerSubject, MoodleAlias, CourseId, UpdatedAtUtc DESC)` e índice único com `NULLS NOT DISTINCT` em proprietário/categoria/alias/curso/chave. Inserir versão 5 com `ON CONFLICT DO NOTHING`. Adicionar `DbSet<UserMemory>` e configuração de comprimentos/índices no DbContext.

- [ ] **Step 4: Implementar repositório e registrar DI/schema**

As consultas devem começar por `OwnerSubject == ownerSubject`; `ListAsync` aplica escopo inclusivo, categoria, termo em chave/conteúdo, ordenação por especificidade e atualização, e `Take(limit)`. Adicionar o script obrigatório ao `SchemaScriptPaths` e `AddScoped<IUserMemoryRepository, UserMemoryRepository>()`.

- [ ] **Step 5: Rodar testes de memória e schema**

Run: `dotnet test tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj --filter "FullyQualifiedName~UserMemory|FullyQualifiedName~SchemaScriptTests"`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/MoodleConnector.Infrastructure tests/MoodleConnector.Application.Tests/Infrastructure/SchemaScriptTests.cs
git commit -m "feat: persistir memorias no postgres"
```

### Task 3: Índice e busca pedagógica

**Files:**
- Create: `src/MoodleConnector.Application/Pedagogy/IPedagogicGuidanceSearch.cs`
- Create: `src/MoodleConnector.Infrastructure/Pedagogy/MarkdownPedagogicGuidanceSearch.cs`
- Modify: `src/MoodleConnector.Infrastructure/DependencyInjection.cs`
- Test: `tests/MoodleConnector.Application.Tests/Infrastructure/MarkdownPedagogicGuidanceSearchTests.cs`

- [ ] **Step 1: Escrever testes falhando para segmentação, ranking e ausência**

Usar pasta temporária com dois `.md`. Confirmar que busca por `avaliação formativa` prioriza seção que contém ambos os termos, preserva título/seção/caminho relativo, ignora arquivo fora da raiz e retorna lista vazia quando a pasta não existe.

```csharp
[Fact]
public async Task SearchAsync_PriorizaSecaoComMaisTermosDaConsulta()
{
    await fixture.WriteAsync("guia.md", "# Guia\n## Avaliação\nA avaliação formativa acompanha a aprendizagem.\n## Plano\nPlanejamento semanal.");
    var result = await fixture.CreateSearch().SearchAsync("avaliação formativa", 5, default);
    Assert.Equal("Avaliação", Assert.Single(result).Section);
    Assert.Equal("guia.md", result[0].RelativePath);
}
```

- [ ] **Step 2: Rodar e confirmar RED**

Run: `dotnet test tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj --filter FullyQualifiedName~MarkdownPedagogicGuidanceSearchTests`

Expected: FAIL por implementação inexistente.

- [ ] **Step 3: Implementar índice determinístico**

O construtor recebe a raiz fixa configurada pelo host. Enumerar apenas `*.md` com `SearchOption.TopDirectoryOnly`; dividir por cabeçalhos `#`; agrupar texto em blocos de até 1600 caracteres; normalizar diacríticos e caixa. Pontuar `+4` por termo no título/seção, `+1` por ocorrência no corpo e `+3` quando todos os termos aparecem. Ordenar por score desc, caminho e seção. Limitar entrada a 300 caracteres e resultados a 1..10.

- [ ] **Step 4: Registrar singleton usando `AppContext.BaseDirectory/public/pedagogic`**

```csharp
services.AddSingleton<IPedagogicGuidanceSearch>(_ =>
    new MarkdownPedagogicGuidanceSearch(Path.Combine(AppContext.BaseDirectory, "public", "pedagogic")));
```

- [ ] **Step 5: Rodar testes e confirmar GREEN**

Executar o filtro anterior; esperado: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/MoodleConnector.Application/Pedagogy src/MoodleConnector.Infrastructure/Pedagogy src/MoodleConnector.Infrastructure/DependencyInjection.cs tests/MoodleConnector.Application.Tests/Infrastructure/MarkdownPedagogicGuidanceSearchTests.cs
git commit -m "feat: indexar orientacoes pedagogicas locais"
```

### Task 4: Tools MCP e instruções automáticas

**Files:**
- Create: `src/MoodleConnector.Presentation/Tools/Memory/MoodleMemoryTools.cs`
- Create: `src/MoodleConnector.Presentation/Tools/Pedagogy/MoodlePedagogyTools.cs`
- Modify: `src/MoodleConnector.Presentation/Program.cs`
- Modify: `tests/MoodleConnector.Application.Tests/Tools/McpToolMetadataTests.cs`
- Create: `tests/MoodleConnector.Application.Tests/Tools/Memory/MoodleMemoryToolsTests.cs`
- Create: `tests/MoodleConnector.Application.Tests/Tools/Pedagogy/MoodlePedagogyToolsTests.cs`

- [ ] **Step 1: Escrever testes falhando de delegação e metadados**

Cobrir ações `salvar`, `listar`, `remover`, ação inválida, resposta estruturada padrão e busca pedagógica. Incluir os tipos na enumeração de metadados. Esperar memória `ReadOnly=false`, `Destructive=false`, `Idempotent=true`, `OpenWorld=false`; pedagogia `ReadOnly=true`, `Destructive=false`, `Idempotent=true`, `OpenWorld=false`.

- [ ] **Step 2: Rodar e confirmar RED**

Run: `dotnet test tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj --filter "FullyQualifiedName~MoodleMemoryToolsTests|FullyQualifiedName~MoodlePedagogyToolsTests|FullyQualifiedName~McpToolMetadataTests"`

Expected: FAIL pelos tipos de tool inexistentes.

- [ ] **Step 3: Implementar `gerenciar_memoria_usuario`**

Usar um método MCP com argumentos opcionais e descrição inequívoca das ações. Para `salvar`, exigir categoria/chave/conteúdo/origem; para `listar`, aceitar consulta/categoria/alias/curso/limite; para `remover`, exigir UUID. Capturar `ArgumentException` e retornar `ToolResultHelper.Error<T>` sem stack trace.

```csharp
[McpServerTool(Name = "gerenciar_memoria_usuario", ReadOnly = false,
    Destructive = false, Idempotent = true, OpenWorld = false)]
public Task<CallToolResult> ManageAsync(string action, string? category = null,
    string? key = null, string? content = null, string? origin = null,
    string? query = null, string? moodleAlias = null, string? courseId = null,
    Guid? memoryId = null, int limit = 20, CancellationToken cancellationToken = default)
```

- [ ] **Step 4: Implementar `consultar_orientacoes_pedagogicas`**

Retornar `ToolResponse<PedagogicGuidanceResponse>` com resultados contendo `relativePath`, `title`, `section`, `excerpt` e `score`. A descrição deve ordenar uso em avaliação, feedback, planejamento, fóruns, acompanhamento e relatórios.

- [ ] **Step 5: Registrar tools e ServerInstructions**

Alterar para `.AddMcpServer(options => options.ServerInstructions = MoodleConnectorInstructions.Text)` e registrar `.WithTools<MoodleMemoryTools>().WithTools<MoodlePedagogyTools>()`. O texto instrui: consultar memória antes de tarefas; consultar guias em tarefas pedagógicas; salvar automaticamente preferências/caminhos/correções/decisões duráveis; nunca salvar segredos ou dados pessoais de alunos; marcar `explicit` versus `inferred`.

- [ ] **Step 6: Rodar testes e confirmar GREEN**

Executar o filtro do Step 2; esperado: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src/MoodleConnector.Presentation/Tools src/MoodleConnector.Presentation/Program.cs tests/MoodleConnector.Application.Tests/Tools
git commit -m "feat: expor memoria e guias como tools mcp"
```

### Task 5: Publicação dos guias e documentação

**Files:**
- Modify: `src/MoodleConnector.Presentation/MoodleConnector.Presentation.csproj`
- Modify: `Dockerfile`
- Modify: `docs/technical/mcp-tools-catalog.md`
- Modify: `docs/security/privacy-best-practices.md`
- Modify: `README.md`
- Test: `tests/MoodleConnector.Application.Tests/Infrastructure/PedagogicContentPublishTests.cs`

- [ ] **Step 1: Escrever teste falhando de conteúdo publicável**

O teste abre o csproj e verifica um item `Content Include="..\..\public\pedagogic\**\*.md"` com `Link="public\pedagogic\%(RecursiveDir)%(Filename)%(Extension)"` e `CopyToOutputDirectory="PreserveNewest"`.

- [ ] **Step 2: Rodar e confirmar RED**

Run: `dotnet test tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj --filter FullyQualifiedName~PedagogicContentPublishTests`

Expected: FAIL porque o item de conteúdo não existe.

- [ ] **Step 3: Incluir os Markdown na publicação**

Adicionar ao csproj:

```xml
<Content Include="..\..\public\pedagogic\**\*.md"
         Link="public\pedagogic\%(RecursiveDir)%(Filename)%(Extension)"
         CopyToOutputDirectory="PreserveNewest"
         CopyToPublishDirectory="PreserveNewest" />
```

No Dockerfile, executar `COPY public/pedagogic/ public/pedagogic/` antes do publish para que os arquivos externos a `src` estejam no contexto de build.

- [ ] **Step 4: Documentar tools, automação e limites**

Adicionar catálogo com argumentos, respostas e hints; atualizar privacidade com proibições de segredo/dados de aluno e capacidade de listar/remover; atualizar README com exemplos “lembre que...” e “use os guias para...”, deixando explícita a dependência de chamada da IA.

- [ ] **Step 5: Rodar teste e publicação**

Run: `dotnet test tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj --filter FullyQualifiedName~PedagogicContentPublishTests`

Run: `dotnet publish src/MoodleConnector.Presentation/MoodleConnector.Presentation.csproj -c Release -o .artifacts/memory-publish`

Expected: teste PASS, publish exit 0 e arquivos em `.artifacts/memory-publish/public/pedagogic`.

- [ ] **Step 6: Commit**

Adicionar somente a movimentação já presente dos guias e os arquivos desta tarefa; não incluir `.artifacts`.

```powershell
git add Dockerfile README.md docs/technical/mcp-tools-catalog.md docs/security/privacy-best-practices.md src/MoodleConnector.Presentation/MoodleConnector.Presentation.csproj tests/MoodleConnector.Application.Tests/Infrastructure/PedagogicContentPublishTests.cs public/pedagogic docs/pedagogic
git commit -m "docs: publicar guias e documentar memoria"
```

### Task 6: Verificação integrada

**Files:**
- Modify if required: `chatgpt-app-submission.json`
- Verify: all changed files

- [ ] **Step 1: Validar contrato de submissão**

Se o arquivo enumera tools/testes, acrescentar `gerenciar_memoria_usuario` e `consultar_orientacoes_pedagogicas`, justificando hints e casos negativos de segredo, outro usuário e path traversal. Validar JSON com `Get-Content -Raw chatgpt-app-submission.json | ConvertFrom-Json`.

- [ ] **Step 2: Rodar suíte completa**

Run: `dotnet test MoodleConnector.slnx --configuration Release`

Expected: exit 0, zero testes falhando.

- [ ] **Step 3: Rodar build e inspeções finais**

Run: `dotnet build MoodleConnector.slnx --configuration Release --no-restore`

Run: `git diff --check`

Expected: ambos exit 0; sem warnings novos causados pela feature nem erros de whitespace.

- [ ] **Step 4: Conferir critérios da especificação**

Verificar no diff: isolamento por `Subject`; quatro categorias; duas origens; três escopos; upsert; remoção por UUID; filtro de segredos; índice restrito; instruções MCP; documentação e conteúdo publicado.

- [ ] **Step 5: Commit final somente se houver ajustes**

```powershell
git add chatgpt-app-submission.json
git commit -m "docs: atualizar contrato de submissao das tools"
```

