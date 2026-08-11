# Classificacao Resiliente de Participantes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preservar possiveis alunos quando o Moodle omitir roles, enriquecer roles/groups na consulta e explicar resultados vazios ou degradados nas tools de participantes e risco.

**Architecture:** O gateway solicita `roles` e `groups`, classifica participantes por um componente puro e devolve diagnostico junto da pagina. As tools transformam esse diagnostico em warnings; o relatorio de risco propaga o mesmo diagnostico e agrega falhas parciais de nota/conclusao.

**Tech Stack:** .NET 10, C#, MediatR, MCP SDK, xUnit.

---

### Task 1: Modelo e classificador central

**Files:**
- Modify: `src/MoodleConnector.Domain/CourseParticipantSummary.cs`
- Create: `src/MoodleConnector.Infrastructure/ParticipantClassification.cs`
- Test: `tests/MoodleConnector.Application.Tests/Participants/ParticipantClassificationTests.cs`
- Modify: `src/MoodleConnector.Infrastructure/MoodleConnector.Infrastructure.csproj`
- Modify: `tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj`

- [ ] **Step 1: Escrever os testes vermelhos**

Usar `CourseParticipantSummary` real e a API desejada:

```csharp
var result = ParticipantClassification.Classify(participant);
Assert.Equal(ParticipantClassificationKind.UncertainFallback, result);
```

Cobrir: `student` => `Student`; roles vazias e role desconhecida => `UncertainFallback`; somente `editingteacher` => `KnownStaff`; mistura `student`/`teacher` => `Student`.

- [ ] **Step 2: Confirmar RED**

Run: `dotnet test tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj --filter FullyQualifiedName~ParticipantClassificationTests`
Expected: FAIL de compilacao porque o classificador não existe.

- [ ] **Step 3: Implementar o modelo e classificador minimos**

Adicionar ao dominio:

```csharp
public enum ParticipantClassificationMode { NotRequested, RoleBased, Mixed, Fallback }

public sealed record ParticipantClassificationDiagnostics(
    int EvaluatedCount,
    int IncludedByStudentRoleCount,
    int IncludedByFallbackCount,
    int ExcludedKnownStaffCount,
    bool HasEmptyRoles,
    bool HasEmptyGroups,
    ParticipantClassificationMode Mode)
{
    public static ParticipantClassificationDiagnostics Empty { get; } =
        new(0, 0, 0, 0, false, false, ParticipantClassificationMode.NotRequested);
}
```

Acrescentar `ParticipantClassificationDiagnostics? ClassificationDiagnostics = null` ao final de `CourseParticipantsPage`; consumidores devem usar `page.ClassificationDiagnostics ?? ParticipantClassificationDiagnostics.Empty`. Criar classificador interno com os três resultados, normalização case/acento e aliases da spec. Excluir apenas se todos os roles forem de equipe. Expor internals para testes e adicionar referencia do projeto Infrastructure ao projeto de testes.

- [ ] **Step 4: Confirmar GREEN**

Run: o comando do Step 2.
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/MoodleConnector.Domain/CourseParticipantSummary.cs src/MoodleConnector.Infrastructure/ParticipantClassification.cs src/MoodleConnector.Infrastructure/MoodleConnector.Infrastructure.csproj tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj tests/MoodleConnector.Application.Tests/Participants/ParticipantClassificationTests.cs
git commit -m "feat: classificar participantes com fallback inclusivo"
```

### Task 2: Integrar consulta, filtro e diagnostico no gateway

**Files:**
- Modify: `src/MoodleConnector.Infrastructure/MoodleParticipantsGateway.cs`
- Create: `tests/MoodleConnector.Application.Tests/Participants/MoodleParticipantsGatewayTests.cs`

- [ ] **Step 1: Escrever testes vermelhos do gateway**

Usar handler HTTP e providers fake com JSON Moodle real. Verificar URL com `roles,groups`; roles vazias incluidas quando `studentsOnly`; `editingteacher` excluido; grupos preservados; contadores corretos.

- [ ] **Step 2: Confirmar RED**

Run: `dotnet test tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj --filter FullyQualifiedName~MoodleParticipantsGatewayTests`
Expected: FAIL porque os campos são omitidos e roles vazias são descartadas.

- [ ] **Step 3: Implementar integracao minima**

Adicionar sempre `"roles"` e `"groups"` a `BuildUserFields`. No loop, incluir `Student` e `UncertainFallback`, excluir apenas `KnownStaff` e acumular diagnostico. Definir `Fallback` se todos incluidos forem incertos, `Mixed` se houver papeis e fallback, `RoleBased` sem fallback e `NotRequested` sem avaliados.

- [ ] **Step 4: Confirmar GREEN e regressao**

Run: `dotnet test tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj --filter "FullyQualifiedName~MoodleParticipantsGatewayTests|FullyQualifiedName~ParticipantClassificationTests|FullyQualifiedName~Participants"`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/MoodleConnector.Infrastructure/MoodleParticipantsGateway.cs tests/MoodleConnector.Application.Tests/Participants/MoodleParticipantsGatewayTests.cs
git commit -m "fix: preservar alunos quando roles estiverem ausentes"
```

### Task 3: Warnings nas tools de participantes

**Files:**
- Modify: `src/MoodleConnector.Presentation/Tools/Participants/MoodleParticipantsTools.cs`
- Modify: `tests/MoodleConnector.Application.Tests/Tools/Participants/MoodleParticipantsToolsTests.cs`

- [ ] **Step 1: Escrever testes vermelhos**

Desserializar `StructuredContent`. Cobrir fallback, primeira pagina vazia, pagina posterior vazia, roles ausentes e groups ausentes. `status` continua `ok` e `warnings` não fica vazio.

- [ ] **Step 2: Confirmar RED**

Run: `dotnet test tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj --filter FullyQualifiedName~MoodleParticipantsToolsTests`
Expected: FAIL porque warnings permanece vazio.

- [ ] **Step 3: Implementar `BuildParticipantWarnings`**

Regras: vazio na primeira pagina informa ausência para filtros; pagina posterior informa pagina fora do intervalo; fallback informa quantidade incluida; flags de roles/groups informam indisponibilidade sem erro. Passar a lista ao `ToolResponse`.

- [ ] **Step 4: Confirmar GREEN**

Run: comando do Step 2.
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/MoodleConnector.Presentation/Tools/Participants/MoodleParticipantsTools.cs tests/MoodleConnector.Application.Tests/Tools/Participants/MoodleParticipantsToolsTests.cs
git commit -m "feat: diagnosticar listagens de participantes degradadas"
```

### Task 4: Propagar diagnostico no relatorio de risco

**Files:**
- Modify: `src/MoodleConnector.Application/Risk/Queries/GetStudentsAtRiskReportQuery.cs`
- Modify: `src/MoodleConnector.Application/Risk/Queries/GetStudentsAtRiskReportQueryHandler.cs`
- Modify: `src/MoodleConnector.Presentation/Tools/Risk/MoodleRiskAnalysisTools.cs`
- Modify: `tests/MoodleConnector.Application.Tests/Risk/Queries/GetStudentsAtRiskReportQueryHandlerTests.cs`
- Create: `tests/MoodleConnector.Application.Tests/Tools/Risk/MoodleRiskAnalysisToolsTests.cs`

- [ ] **Step 1: Escrever testes vermelhos da query**

Esperar:

```csharp
public sealed record StudentsAtRiskReportResult(
    IReadOnlyList<StudentRiskReport> Reports,
    int ParticipantsAnalyzedCount,
    ParticipantClassificationDiagnostics ClassificationDiagnostics,
    int GradebookFailureCount,
    int CompletionFailureCount);
```

Cobrir participante sem role analisado, zero participantes e falhas parciais agregadas.

- [ ] **Step 2: Confirmar RED**

Run: `dotnet test tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj --filter FullyQualifiedName~GetStudentsAtRiskReportQueryHandlerTests`
Expected: FAIL porque o novo resultado não existe.

- [ ] **Step 3: Implementar resultado e agregacao**

Mudar `IRequest` e handler para o novo tipo, incrementar contadores nos catches existentes e retornar relatórios ordenados com diagnostico.

- [ ] **Step 4: Confirmar GREEN da query**

Run: comando do Step 2.
Expected: PASS.

- [ ] **Step 5: Escrever testes vermelhos da tool**

Cobrir fallback, nenhum participante, participantes sem fatores e falhas parciais. Todo vazio diagnosticavel tem warning e `status: ok`.

- [ ] **Step 6: Confirmar RED da tool**

Run: `dotnet test tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj --filter FullyQualifiedName~MoodleRiskAnalysisToolsTests`
Expected: FAIL porque a tool ainda usa lista sem diagnostico.

- [ ] **Step 7: Implementar warnings mantendo `data` como lista**

Usar `result.Reports` como `data` para compatibilidade. Gerar warnings para fallback, nenhum participante, nenhum risco após análise e falhas agregadas. Narrar `ParticipantsAnalyzedCount`.

- [ ] **Step 8: Confirmar GREEN de risco**

Run: `dotnet test tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj --filter "FullyQualifiedName~Risk|FullyQualifiedName~MoodleRiskAnalysisToolsTests"`
Expected: PASS.

- [ ] **Step 9: Commit**

```powershell
git add src/MoodleConnector.Application/Risk src/MoodleConnector.Presentation/Tools/Risk/MoodleRiskAnalysisTools.cs tests/MoodleConnector.Application.Tests/Risk tests/MoodleConnector.Application.Tests/Tools/Risk
git commit -m "fix: evitar relatorio de risco falsamente vazio"
```

### Task 5: Verificacao final e documentacao

**Files:**
- Modify: `docs/technical/mcp-tools-catalog.md`

- [ ] **Step 1: Atualizar catalogo**

Documentar fallback inclusivo com warning, dependência das permissões para roles/groups e diagnostico de vazios no relatório.

- [ ] **Step 2: Verificar formatacao e build**

Run: `dotnet format MoodleConnector.slnx --verify-no-changes`
Expected: exit 0. Se houver diferenças, executar `dotnet format MoodleConnector.slnx`, revisar e repetir.

Run: `dotnet build MoodleConnector.slnx --no-restore`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Rodar suite completa**

Run: `dotnet test MoodleConnector.slnx --no-build`
Expected: 0 falhas.

- [ ] **Step 4: Revisar diff**

Run: `git diff --check` e `git status --short`.
Expected: sem whitespace inválido; somente arquivos desta correção.

- [ ] **Step 5: Commit**

```powershell
git add docs/technical/mcp-tools-catalog.md
git commit -m "docs: documentar fallback de participantes"
```
