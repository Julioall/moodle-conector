using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Participants;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;

namespace MoodleConnector.Presentation.Tools;

[McpServerToolType]
public sealed class MoodleParticipantsTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver)
{
    [McpServerTool(
        Name = "listar_participantes_curso",
        Title = "Listar Participantes Curso",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListCourseParticipantsResponse>))]
    [Description("Lista participantes de um curso Moodle com paginacao e filtro de status. E-mail nao e retornado por padrao.")]
    public Task<CallToolResult> ListarParticipantesCursoAsync(
        [Description("Identificador do curso. Pode ser courseId, shortName ou idnumber.")]
        string courseId,
        [Description("Pagina de resultados, iniciando em 1.")]
        int pagina = 1,
        [Description("Tamanho da pagina, de 1 a 50.")]
        int tamanhoPagina = 20,
        [Description("Filtro de status: ativos, suspensos ou todos.")]
        string status = "ativos",
        [Description("Quando true, inclui e-mail caso o Moodle permita. Use somente quando necessario.")]
        bool incluirEmail = false,
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrao do usuario.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListParticipantsCoreAsync(
            courseId,
            pagina,
            tamanhoPagina,
            status,
            incluirEmail,
            moodleAlias,
            studentsOnly: false,
            cancellationToken);
    }

    [McpServerTool(
        Name = "list_course_participants",
        Title = "List Course Participants",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListCourseParticipantsResponse>))]
    [Description("Lists Moodle course participants with pagination and status filtering. Email is not returned by default.")]
    public Task<CallToolResult> ListCourseParticipantsAsync(
        [Description("Course identifier. Can be courseId, shortName, or idnumber.")]
        string courseId,
        [Description("Result page, starting at 1.")]
        int page = 1,
        [Description("Page size, from 1 to 50.")]
        int pageSize = 20,
        [Description("Status filter: active, suspended, or all.")]
        string status = "active",
        [Description("When true, includes email if Moodle permits it. Use only when necessary.")]
        bool includeEmail = false,
        [Description("Moodle connection alias to query. When omitted, uses the user's default Moodle connection.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListParticipantsCoreAsync(
            courseId,
            page,
            pageSize,
            status,
            includeEmail,
            moodleAlias,
            studentsOnly: false,
            cancellationToken);
    }

    [McpServerTool(
        Name = "listar_alunos_curso",
        Title = "Listar Alunos Curso",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListCourseParticipantsResponse>))]
    [Description("Lista estudantes de um curso Moodle com paginacao e filtro de status. E-mail nao e retornado por padrao.")]
    public Task<CallToolResult> ListarAlunosCursoAsync(
        [Description("Identificador do curso. Pode ser courseId, shortName ou idnumber.")]
        string courseId,
        [Description("Pagina de resultados, iniciando em 1.")]
        int pagina = 1,
        [Description("Tamanho da pagina, de 1 a 50.")]
        int tamanhoPagina = 20,
        [Description("Filtro de status: ativos, suspensos ou todos.")]
        string status = "ativos",
        [Description("Quando true, inclui e-mail caso o Moodle permita. Use somente quando necessario.")]
        bool incluirEmail = false,
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrao do usuario.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListParticipantsCoreAsync(
            courseId,
            pagina,
            tamanhoPagina,
            status,
            incluirEmail,
            moodleAlias,
            studentsOnly: true,
            cancellationToken);
    }

    [McpServerTool(
        Name = "list_course_students",
        Title = "List Course Students",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListCourseParticipantsResponse>))]
    [Description("Lists Moodle course students with pagination and status filtering. Email is not returned by default.")]
    public Task<CallToolResult> ListCourseStudentsAsync(
        [Description("Course identifier. Can be courseId, shortName, or idnumber.")]
        string courseId,
        [Description("Result page, starting at 1.")]
        int page = 1,
        [Description("Page size, from 1 to 50.")]
        int pageSize = 20,
        [Description("Status filter: active, suspended, or all.")]
        string status = "active",
        [Description("When true, includes email if Moodle permits it. Use only when necessary.")]
        bool includeEmail = false,
        [Description("Moodle connection alias to query. When omitted, uses the user's default Moodle connection.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListParticipantsCoreAsync(
            courseId,
            page,
            pageSize,
            status,
            includeEmail,
            moodleAlias,
            studentsOnly: true,
            cancellationToken);
    }

    [McpServerTool(
        Name = "listar_grupos_curso",
        Title = "Listar Grupos Curso",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListCourseGroupsResponse>))]
    [Description("Lista grupos de um curso Moodle vinculado ao usuario autenticado.")]
    public async Task<CallToolResult> ListarGruposCursoAsync(
        [Description("Identificador do curso. Pode ser courseId, shortName ou idnumber.")]
        string courseId,
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrao do usuario.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return await ListGroupsCoreAsync(courseId, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "list_course_groups",
        Title = "List Course Groups",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListCourseGroupsResponse>))]
    [Description("Lists groups for a Moodle course linked to the authenticated user.")]
    public Task<CallToolResult> ListCourseGroupsAsync(
        [Description("Course identifier. Can be courseId, shortName, or idnumber.")]
        string courseId,
        [Description("Moodle connection alias to query. When omitted, uses the user's default Moodle connection.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListGroupsCoreAsync(courseId, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "consultar_membros_grupo",
        Title = "Consultar Membros Grupo",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListCourseParticipantsResponse>))]
    [Description("Lista membros de um grupo Moodle com paginacao. E-mail nao e retornado por padrao.")]
    public Task<CallToolResult> ConsultarMembrosGrupoAsync(
        [Description("Identificador do curso. Pode ser courseId, shortName ou idnumber.")]
        string courseId,
        [Description("Identificador numerico do grupo Moodle.")]
        string groupId,
        [Description("Pagina de resultados, iniciando em 1.")]
        int pagina = 1,
        [Description("Tamanho da pagina, de 1 a 50.")]
        int tamanhoPagina = 20,
        [Description("Filtro de status: ativos, suspensos ou todos.")]
        string status = "ativos",
        [Description("Quando true, inclui e-mail caso o Moodle permita. Use somente quando necessario.")]
        bool incluirEmail = false,
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrao do usuario.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListGroupMembersCoreAsync(
            courseId,
            groupId,
            pagina,
            tamanhoPagina,
            status,
            incluirEmail,
            moodleAlias,
            cancellationToken);
    }

    [McpServerTool(
        Name = "get_group_members",
        Title = "Get Group Members",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListCourseParticipantsResponse>))]
    [Description("Lists Moodle group members with pagination. Email is not returned by default.")]
    public Task<CallToolResult> GetGroupMembersAsync(
        [Description("Course identifier. Can be courseId, shortName, or idnumber.")]
        string courseId,
        [Description("Numeric Moodle group identifier.")]
        string groupId,
        [Description("Result page, starting at 1.")]
        int page = 1,
        [Description("Page size, from 1 to 50.")]
        int pageSize = 20,
        [Description("Status filter: active, suspended, or all.")]
        string status = "active",
        [Description("When true, includes email if Moodle permits it. Use only when necessary.")]
        bool includeEmail = false,
        [Description("Moodle connection alias to query. When omitted, uses the user's default Moodle connection.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListGroupMembersCoreAsync(
            courseId,
            groupId,
            page,
            pageSize,
            status,
            includeEmail,
            moodleAlias,
            cancellationToken);
    }

    private async Task<CallToolResult> ListParticipantsCoreAsync(
        string courseId,
        int page,
        int pageSize,
        string status,
        bool includeEmail,
        string? moodleAlias,
        bool studentsOnly,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return ToolResultHelper.Error<ListCourseParticipantsResponse>("Informe um identificador de curso.");
        }

        if (!TryParseStatus(status, out var statusFilter))
        {
            return ToolResultHelper.Error<ListCourseParticipantsResponse>("Filtro de status invalido. Use ativos, suspensos ou todos.");
        }

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return ToolResultHelper.Error<ListCourseParticipantsResponse>("Usuario nao autenticado para consultar participantes.");
        }

        CourseParticipantsPage? participantsPage;
        try
        {
            participantsPage = await mediator.Send(
                new ListCourseParticipantsQuery(
                    moodleUserId.Value.ToString(),
                    courseId,
                    statusFilter,
                    page,
                    pageSize,
                    studentsOnly,
                    includeEmail),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ToolResultHelper.Error<ListCourseParticipantsResponse>("Nao foi possivel listar participantes no Moodle neste momento.");
        }

        if (participantsPage is null)
        {
            return ToolResultHelper.Error<ListCourseParticipantsResponse>("Curso nao encontrado entre os cursos vinculados ao usuario.");
        }

        return ParticipantsSuccess(participantsPage);
    }

    private async Task<CallToolResult> ListGroupMembersCoreAsync(
        string courseId,
        string groupId,
        int page,
        int pageSize,
        string status,
        bool includeEmail,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return ToolResultHelper.Error<ListCourseParticipantsResponse>("Informe um identificador de curso.");
        }

        if (string.IsNullOrWhiteSpace(groupId))
        {
            return ToolResultHelper.Error<ListCourseParticipantsResponse>("Informe um identificador de grupo.");
        }

        if (!TryParseStatus(status, out var statusFilter))
        {
            return ToolResultHelper.Error<ListCourseParticipantsResponse>("Filtro de status invalido. Use ativos, suspensos ou todos.");
        }

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return ToolResultHelper.Error<ListCourseParticipantsResponse>("Usuario nao autenticado para consultar membros do grupo.");
        }

        CourseParticipantsPage? participantsPage;
        try
        {
            participantsPage = await mediator.Send(
                new ListGroupMembersQuery(
                    moodleUserId.Value.ToString(),
                    courseId,
                    groupId,
                    statusFilter,
                    page,
                    pageSize,
                    includeEmail),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ToolResultHelper.Error<ListCourseParticipantsResponse>("Nao foi possivel consultar membros do grupo no Moodle neste momento.");
        }

        if (participantsPage is null)
        {
            return ToolResultHelper.Error<ListCourseParticipantsResponse>("Curso nao encontrado entre os cursos vinculados ao usuario.");
        }

        return ParticipantsSuccess(participantsPage);
    }

    private async Task<CallToolResult> ListGroupsCoreAsync(
        string courseId,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return ToolResultHelper.Error<ListCourseGroupsResponse>("Informe um identificador de curso.");
        }

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return ToolResultHelper.Error<ListCourseGroupsResponse>("Usuario nao autenticado para consultar grupos.");
        }

        IReadOnlyList<CourseGroupSummary>? groups;
        try
        {
            groups = await mediator.Send(
                new ListCourseGroupsQuery(moodleUserId.Value.ToString(), courseId),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ToolResultHelper.Error<ListCourseGroupsResponse>("Nao foi possivel listar grupos no Moodle neste momento.");
        }

        if (groups is null)
        {
            return ToolResultHelper.Error<ListCourseGroupsResponse>("Curso nao encontrado entre os cursos vinculados ao usuario.");
        }

        var data = new ListCourseGroupsResponse(
            courseId,
            groups.Count,
            groups.Select(ToGroupItem).ToArray());
        var response = new ToolResponse<ListCourseGroupsResponse>("ok", data, [], AuditId: null, DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildGroupsNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private static CallToolResult ParticipantsSuccess(CourseParticipantsPage participantsPage)
    {
        var data = new ListCourseParticipantsResponse(
            participantsPage.CourseId,
            participantsPage.Page,
            participantsPage.PageSize,
            ToStatusText(participantsPage.StatusFilter),
            participantsPage.StudentsOnly,
            participantsPage.IncludeEmail,
            participantsPage.HasMore,
            participantsPage.Participants.Count,
            participantsPage.Participants.Select(ToParticipantItem).ToArray());
        var response = new ToolResponse<ListCourseParticipantsResponse>(
            "ok",
            data,
            BuildParticipantWarnings(participantsPage),
            AuditId: null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildParticipantsNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private static IReadOnlyList<string> BuildParticipantWarnings(CourseParticipantsPage page)
    {
        var warnings = new List<string>();
        var diagnostics = page.ClassificationDiagnostics ?? ParticipantClassificationDiagnostics.Empty;

        if (page.Participants.Count == 0)
        {
            warnings.Add(page.Page > 1
                ? "A pagina solicitada nao retornou participantes. Ela pode estar fora do intervalo disponivel."
                : "Nenhum participante foi encontrado para os filtros informados.");
        }

        if (diagnostics.IncludedByFallbackCount > 0)
        {
            warnings.Add(
                $"Nao foi possivel identificar todos os alunos por role. " +
                $"{diagnostics.IncludedByFallbackCount} participante(s) foram incluidos por fallback.");
        }

        if (diagnostics.HasEmptyRoles)
        {
            warnings.Add("O Moodle retornou participantes sem roles; a classificacao pode estar incompleta.");
        }

        if (diagnostics.HasEmptyGroups)
        {
            warnings.Add("O Moodle retornou participantes sem grupos; o curso pode nao usar grupos ou a informacao pode estar indisponivel.");
        }

        return warnings;
    }

    private static string BuildParticipantsNarration(ListCourseParticipantsResponse response)
    {
        if (response.Count == 0)
        {
            return "Nao encontrei participantes para os filtros informados.";
        }

        var label = response.StudentsOnly ? "aluno(s)" : "participante(s)";
        var lines = response.Participants.Select(participant => $"- {participant.FullName} (ID: {participant.UserId})");
        var suffix = response.HasMore ? "\nHa mais resultados. Avance a pagina para continuar." : string.Empty;

        return $"Encontrei {response.Count} {label} na pagina {response.Page}:\n" + string.Join("\n", lines) + suffix;
    }

    private static string BuildGroupsNarration(ListCourseGroupsResponse response)
    {
        if (response.Count == 0)
        {
            return "Nao encontrei grupos para este curso.";
        }

        var lines = response.Groups.Select(group => $"- {group.Name} (ID: {group.GroupId})");
        return $"Encontrei {response.Count} grupo(s):\n" + string.Join("\n", lines);
    }

    private static bool TryParseStatus(string value, out ParticipantStatusFilter statusFilter)
    {
        var normalized = value.Trim().ToLowerInvariant();
        statusFilter = normalized switch
        {
            "active" or "ativo" or "ativos" => ParticipantStatusFilter.Active,
            "suspended" or "suspenso" or "suspensos" or "inactive" or "inativo" or "inativos" => ParticipantStatusFilter.Suspended,
            "all" or "todos" or "todas" => ParticipantStatusFilter.All,
            _ => ParticipantStatusFilter.Active
        };

        return normalized is "active" or "ativo" or "ativos" or
            "suspended" or "suspenso" or "suspensos" or "inactive" or "inativo" or "inativos" or
            "all" or "todos" or "todas";
    }

    private static string ToStatusText(ParticipantStatusFilter statusFilter)
    {
        return statusFilter switch
        {
            ParticipantStatusFilter.Active => "active",
            ParticipantStatusFilter.Suspended => "suspended",
            ParticipantStatusFilter.All => "all",
            _ => "active"
        };
    }

    private static ParticipantItem ToParticipantItem(CourseParticipantSummary participant)
    {
        return new ParticipantItem(
            participant.UserId,
            participant.FullName,
            participant.Email,
            participant.Suspended,
            participant.FirstAccessAt,
            participant.LastAccessAt,
            participant.LastCourseAccessAt,
            participant.Roles.Select(role => new ParticipantRoleItem(role.RoleId, role.ShortName, role.Name)).ToArray(),
            participant.Groups.Select(group => new ParticipantGroupItem(group.GroupId, group.Name)).ToArray());
    }

    private static GroupItem ToGroupItem(CourseGroupSummary group)
    {
        return new GroupItem(
            group.GroupId,
            group.CourseId,
            group.Name,
            group.IdNumber);
    }



    public sealed record ListCourseParticipantsResponse(
        [property: JsonPropertyName("courseId")] string CourseId,
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("pageSize")] int PageSize,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("studentsOnly")] bool StudentsOnly,
        [property: JsonPropertyName("includeEmail")] bool IncludeEmail,
        [property: JsonPropertyName("hasMore")] bool HasMore,
        [property: JsonPropertyName("count")] int Count,
        [property: JsonPropertyName("participants")] IReadOnlyList<ParticipantItem> Participants);

    public sealed record ParticipantItem(
        [property: JsonPropertyName("userId")] string UserId,
        [property: JsonPropertyName("fullName")] string FullName,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("suspended")] bool? Suspended,
        [property: JsonPropertyName("firstAccessAt")] DateTimeOffset? FirstAccessAt,
        [property: JsonPropertyName("lastAccessAt")] DateTimeOffset? LastAccessAt,
        [property: JsonPropertyName("lastCourseAccessAt")] DateTimeOffset? LastCourseAccessAt,
        [property: JsonPropertyName("roles")] IReadOnlyList<ParticipantRoleItem> Roles,
        [property: JsonPropertyName("groups")] IReadOnlyList<ParticipantGroupItem> Groups);

    public sealed record ParticipantRoleItem(
        [property: JsonPropertyName("roleId")] string RoleId,
        [property: JsonPropertyName("shortName")] string? ShortName,
        [property: JsonPropertyName("name")] string Name);

    public sealed record ParticipantGroupItem(
        [property: JsonPropertyName("groupId")] string GroupId,
        [property: JsonPropertyName("name")] string Name);

    public sealed record ListCourseGroupsResponse(
        [property: JsonPropertyName("courseId")] string CourseId,
        [property: JsonPropertyName("count")] int Count,
        [property: JsonPropertyName("groups")] IReadOnlyList<GroupItem> Groups);

    public sealed record GroupItem(
        [property: JsonPropertyName("groupId")] string GroupId,
        [property: JsonPropertyName("courseId")] string CourseId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("idNumber")] string? IdNumber);
}
