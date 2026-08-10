using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Courses;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;
using MoodleConnector.Presentation.Configuration;

namespace MoodleConnector.Presentation.Tools;

[McpServerToolType]
public sealed class MoodleCoursesTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver,
    ILogger<MoodleCoursesTools>? logger = null)
{
    [MoodleToolMetadata(Family = "courses", Classification = "R1", Kind = "wrapper", CanonicalOperation = "core_enrol_get_users_courses", Structural = false)]
    [McpServerTool(
        Name = "list_my_courses",
        Title = "List My Courses",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListMyCoursesResponse>))]
    [Description("Lista cursos vinculados ao usuario autenticado no Moodle com suporte a paginacao. Use o parametro pagina para navegar entre as paginas de resultados.")]
    public async Task<CallToolResult> ListarMeusCursosAsync(
        [Description("Quantidade maxima de cursos por pagina (1 a 100). Padrao: 20.")]
        int limite = 20,
        [Description("Numero da pagina a retornar (base 1). Use para paginar e listar todos os cursos. Padrao: 1.")]
        int pagina = 1,
        [Description("Alias do Moodle a consultar. Use quando o usuario mencionar um ambiente especifico, como goias, nacional ou ctm. Quando omitido, usa o Moodle padrao do usuario.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        PagedCourses paged;
        try
        {
            moodleSelection.Alias = moodleAlias;
            var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
            if (moodleUserId is null)
            {
                return ToolResultHelper.Error<ListMyCoursesResponse>(
                    "Usuario nao autenticado para listar cursos.",
                    errorCode: MoodleErrorContract.AuthenticationFailed);
            }

            paged = await mediator.Send(new ListMyCoursesQuery(moodleUserId.Value.ToString(), limite, pagina), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return ToolResultHelper.Error<ListMyCoursesResponse>(
                ex.Message,
                errorCode: MoodleErrorContract.ApiError);
        }
        catch (MoodleApiException ex)
        {
            return ToolResultHelper.Error<ListMyCoursesResponse>(ex);
        }
        catch (Exception ex)
        {
            var failure = MoodleErrorContract.Describe(ex);
            LogUnexpectedFailure(ex, failure, "listar_meus_cursos", moodleAlias);
            return ToolResultHelper.Error<ListMyCoursesResponse>(
                "Nao foi possivel listar os cursos no Moodle neste momento.",
                errorCode: failure.ErrorCode,
                auditId: failure.AuditId);
        }

        var data = new ListMyCoursesResponse(
            paged.TotalCount,
            pagina,
            paged.TotalPages,
            paged.HasNextPage,
            paged.Items.Select(ToCourseItem).ToArray());
        var response = new ToolResponse<ListMyCoursesResponse>(
            "ok",
            data,
            [],
            AuditId: Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            Message: BuildNarration(data));

        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = BuildNarration(data)
                }
            ],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    [MoodleToolMetadata(Family = "courses", Classification = "R1", Kind = "wrapper", CanonicalOperation = "core_course_get_courses_by_field", Structural = false)]
    [McpServerTool(
        Name = "search_courses",
        Title = "Search Courses",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListMyCoursesResponse>))]
    [Description("Busca cursos vinculados ao usuario autenticado por termo, id, nome curto, idnumber, nome completo ou categoria. Pesquisa apenas cursos, nao arquivos internos, atividades ou materiais do curso.")]
    public async Task<CallToolResult> BuscarCursosAsync(
        [Description("Termo de busca. Pode ser courseId, shortName, idnumber, nome do curso ou categoria.")]
        string termo,
        [Description("Quantidade maxima de cursos retornados (1 a 20).")]
        int limite = 10,
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrao do usuario.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return await SearchCoursesCoreAsync(termo, limite, moodleAlias, cancellationToken);
    }

    [MoodleToolMetadata(Family = "courses", Classification = "R6", Kind = "cognitive", CanonicalOperation = "search", Structural = true)]
    [McpServerTool(
        Name = "search",
        Title = "Search Moodle courses",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(SearchResponse))]
    [Description("Use this when ChatGPT needs the standard connector search shape. Searches the authenticated user's Moodle courses and returns citation-ready course URLs. Searches courses only, not internal files, activities, or course materials. To find files inside a course, use list_course_files instead.")]
    public async Task<CallToolResult> SearchAsync(
        [Description("Search query for Moodle courses.")]
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            var empty = new SearchResponse([]);
            return StandardResult(empty);
        }

        moodleSelection.Alias = null;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return StandardError("Usuario nao autenticado para buscar cursos.");
        }

        IReadOnlyList<CourseSummary> courses;
        try
        {
            courses = await mediator.Send(new SearchCoursesQuery(moodleUserId.Value.ToString(), query, 10), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return StandardError("Nao foi possivel buscar cursos no Moodle neste momento.");
        }

        var response = new SearchResponse(courses.Select(course =>
            new SearchResult(
                course.CourseId,
                string.IsNullOrWhiteSpace(course.DisplayName) ? course.FullName : course.DisplayName,
                BuildCourseUrl(course))).ToArray());

        return StandardResult(response);
    }

    [MoodleToolMetadata(Family = "courses", Classification = "R1", Kind = "wrapper", CanonicalOperation = "core_course_get_courses_by_field", Structural = false)]
    [McpServerTool(
        Name = "get_course",
        Title = "Get Course",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<CourseDetailsResponse>))]
    [Description("Consulta dados basicos de um curso vinculado ao usuario autenticado, sem buscar notas, entregas ou risco.")]
    public async Task<CallToolResult> ConsultarCursoAsync(
        [Description("Identificador do curso. Pode ser courseId, shortName ou idnumber.")]
        string courseId,
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrao do usuario.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return await GetCourseCoreAsync(courseId, moodleAlias, cancellationToken);
    }

    [MoodleToolMetadata(Family = "courses", Classification = "R6", Kind = "cognitive", CanonicalOperation = "fetch", Structural = true)]
    [McpServerTool(
        Name = "fetch",
        Title = "Fetch Moodle course",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(FetchResponse))]
    [Description("Use this when ChatGPT needs the standard connector fetch shape. Fetches one authenticated Moodle course document by id, short name, or idnumber.")]
    public async Task<CallToolResult> FetchAsync(
        [Description("Course id returned by search, or another course identifier such as short name or idnumber.")]
        string id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return StandardError("Informe o id do curso para buscar o documento.");
        }

        moodleSelection.Alias = null;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return StandardError("Usuario nao autenticado para consultar curso.");
        }

        CourseSummary? course;
        try
        {
            course = await mediator.Send(new GetCourseQuery(moodleUserId.Value.ToString(), id), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return StandardError("Nao foi possivel consultar o curso no Moodle neste momento.");
        }

        if (course is null)
        {
            return StandardError("Curso nao encontrado entre os cursos vinculados ao usuario.");
        }

        var response = new FetchResponse(
            course.CourseId,
            string.IsNullOrWhiteSpace(course.DisplayName) ? course.FullName : course.DisplayName,
            BuildCourseDocumentText(course),
            BuildCourseUrl(course),
            new Dictionary<string, string?>
            {
                ["shortName"] = course.ShortName,
                ["idNumber"] = course.IdNumber,
                ["categoryName"] = course.CategoryName,
                ["visible"] = course.Visible?.ToString()
            }.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal));

        return StandardResult(response);
    }

    private async Task<CallToolResult> SearchCoursesCoreAsync(
        string query,
        int limit,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolResultHelper.Error<ListMyCoursesResponse>("Informe um termo de busca para localizar cursos.");
        }

        IReadOnlyList<CourseSummary> courses;
        try
        {
            moodleSelection.Alias = moodleAlias;
            var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
            if (moodleUserId is null)
            {
                return ToolResultHelper.Error<ListMyCoursesResponse>(
                    "Usuario nao autenticado para buscar cursos.",
                    errorCode: MoodleErrorContract.AuthenticationFailed);
            }

            courses = await mediator.Send(new SearchCoursesQuery(moodleUserId.Value.ToString(), query, limit), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MoodleApiException ex)
        {
            return ToolResultHelper.Error<ListMyCoursesResponse>(ex);
        }
        catch (Exception ex)
        {
            var failure = MoodleErrorContract.Describe(ex);
            LogUnexpectedFailure(ex, failure, "buscar_cursos", moodleAlias);
            return ToolResultHelper.Error<ListMyCoursesResponse>(
                "Nao foi possivel buscar cursos no Moodle neste momento.",
                errorCode: failure.ErrorCode,
                auditId: failure.AuditId);
        }

        var data = new ListMyCoursesResponse(courses.Count, 1, 1, false, courses.Select(ToCourseItem).ToArray());
        var response = new ToolResponse<ListMyCoursesResponse>(
            "ok",
            data,
            [],
            AuditId: Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            Message: BuildNarration(data));

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private async Task<CallToolResult> GetCourseCoreAsync(
        string courseId,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return ToolResultHelper.Error<CourseDetailsResponse>("Informe um identificador de curso.");
        }

        CourseSummary? course;
        try
        {
            moodleSelection.Alias = moodleAlias;
            var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
            if (moodleUserId is null)
            {
                return ToolResultHelper.Error<CourseDetailsResponse>(
                    "Usuario nao autenticado para consultar curso.",
                    errorCode: MoodleErrorContract.AuthenticationFailed);
            }

            course = await mediator.Send(new GetCourseQuery(moodleUserId.Value.ToString(), courseId), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MoodleApiException ex)
        {
            return ToolResultHelper.Error<CourseDetailsResponse>(ex);
        }
        catch (Exception ex)
        {
            var failure = MoodleErrorContract.Describe(ex);
            LogUnexpectedFailure(ex, failure, "consultar_curso", moodleAlias);
            return ToolResultHelper.Error<CourseDetailsResponse>(
                "Nao foi possivel consultar o curso no Moodle neste momento.",
                errorCode: failure.ErrorCode,
                auditId: failure.AuditId);
        }

        if (course is null)
        {
            return ToolResultHelper.Error<CourseDetailsResponse>(
                "Curso nao encontrado entre os cursos vinculados ao usuario.",
                errorCode: MoodleErrorContract.CourseNotFound);
        }

        var data = new CourseDetailsResponse(ToCourseItem(course));
        var narration = $"Curso encontrado: {course.FullName} (ID: {course.CourseId}).";
        var response = new ToolResponse<CourseDetailsResponse>(
            "ok",
            data,
            [],
            AuditId: Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            Message: narration);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = narration }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private void LogUnexpectedFailure(
        Exception exception,
        MoodleErrorDescriptor failure,
        string toolName,
        string? moodleAlias)
    {
        logger?.LogError(
            exception,
            "Unexpected Moodle tool failure was converted to a structured result. AuditId={AuditId} ErrorCode={ErrorCode} Tool={Tool} Alias={Alias} ExceptionType={ExceptionType} SafeMessage={SafeMessage}",
            failure.AuditId,
            failure.ErrorCode,
            toolName,
            MoodleConnectionAlias.Normalize(moodleAlias),
            exception.GetType().FullName,
            failure.Message);
    }

    private static string BuildNarration(ListMyCoursesResponse response)
    {
        if (response.Total == 0)
        {
            return "O usuario autenticado nao possui cursos vinculados no Moodle no momento.";
        }

        if (response.Courses.Count == 0 && response.Page > 1)
        {
            return $"A pagina {response.Page} nao retornou cursos. O usuario possui apenas {response.TotalPages} pagina(s) de cursos.";
        }

        var lines = response.Courses.Select(course => $"- {course.FullName} (ID: {course.CourseId})");

        string paginationInfo;
        if (response.TotalPages > 1)
        {
            var nextHint = response.HasNextPage ? $", use pagina={response.Page + 1} para ver mais" : string.Empty;
            paginationInfo = $" (Pagina {response.Page} de {response.TotalPages}{nextHint})";
        }
        else
        {
            paginationInfo = string.Empty;
        }

        return $"Encontrei {response.Total} curso(s) no total{paginationInfo}:\n" + string.Join("\n", lines);
    }

    private static CourseItem ToCourseItem(CourseSummary course)
    {
        return new CourseItem(
            course.CourseId,
            course.IdNumber,
            course.ShortName,
            course.FullName,
            course.DisplayName,
            course.CategoryId,
            course.CategoryName,
            course.StartDate,
            course.EndDate,
            course.Visible,
            course.ViewUrl,
            course.CourseImage,
            course.Progress,
            course.HasProgress,
            course.IsFavourite,
            course.LastAccessAt);
    }

    private static string BuildCourseUrl(CourseSummary course)
    {
        if (Uri.TryCreate(course.ViewUrl, UriKind.Absolute, out var viewUri) &&
            (viewUri.Scheme == Uri.UriSchemeHttp || viewUri.Scheme == Uri.UriSchemeHttps))
        {
            return viewUri.ToString();
        }

        return $"https://moodle.local/course/view.php?id={Uri.EscapeDataString(course.CourseId)}";
    }

    private static string BuildCourseDocumentText(CourseSummary course)
    {
        var lines = new List<string>
        {
            $"Curso: {course.FullName}",
            $"ID: {course.CourseId}"
        };

        if (!string.IsNullOrWhiteSpace(course.ShortName))
        {
            lines.Add($"Nome curto: {course.ShortName}");
        }

        if (!string.IsNullOrWhiteSpace(course.IdNumber))
        {
            lines.Add($"ID number: {course.IdNumber}");
        }

        if (!string.IsNullOrWhiteSpace(course.CategoryName))
        {
            lines.Add($"Categoria: {course.CategoryName}");
        }

        if (course.StartDate is not null)
        {
            lines.Add($"Inicio: {course.StartDate:O}");
        }

        if (course.EndDate is not null)
        {
            lines.Add($"Fim: {course.EndDate:O}");
        }

        if (course.Visible is not null)
        {
            lines.Add($"Visivel: {course.Visible}");
        }

        if (course.HasProgress == true && course.Progress is not null)
        {
            lines.Add($"Progresso informado pelo Moodle: {course.Progress}%");
        }

        return string.Join("\n", lines);
    }

    private static CallToolResult StandardResult<T>(T response)
    {
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = JsonSerializer.Serialize(response) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private static CallToolResult StandardError(string message)
    {
        var response = new { error = message };
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = JsonSerializer.Serialize(response) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = true
        };
    }



    public sealed record ListMyCoursesResponse(
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("total_pages")] int TotalPages,
        [property: JsonPropertyName("has_next_page")] bool HasNextPage,
        [property: JsonPropertyName("courses")] IReadOnlyList<CourseItem> Courses);

    public sealed record CourseDetailsResponse(
        [property: JsonPropertyName("course")] CourseItem Course);

    public sealed record SearchResponse(
        [property: JsonPropertyName("results")] IReadOnlyList<SearchResult> Results);

    public sealed record SearchResult(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("url")] string Url);

    public sealed record FetchResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, string>? Metadata);

    public sealed record CourseItem(
        [property: JsonPropertyName("courseId")] string CourseId,
        [property: JsonPropertyName("idNumber")] string? IdNumber,
        [property: JsonPropertyName("shortName")] string? ShortName,
        [property: JsonPropertyName("fullName")] string FullName,
        [property: JsonPropertyName("displayName")] string? DisplayName,
        [property: JsonPropertyName("categoryId")] long? CategoryId,
        [property: JsonPropertyName("categoryName")] string? CategoryName,
        [property: JsonPropertyName("startDate")] DateTimeOffset? StartDate,
        [property: JsonPropertyName("endDate")] DateTimeOffset? EndDate,
        [property: JsonPropertyName("visible")] bool? Visible,
        [property: JsonPropertyName("viewUrl")] string? ViewUrl,
        [property: JsonPropertyName("courseImage")] string? CourseImage,
        [property: JsonPropertyName("progress")] decimal? Progress,
        [property: JsonPropertyName("hasProgress")] bool? HasProgress,
        [property: JsonPropertyName("isFavourite")] bool? IsFavourite,
        [property: JsonPropertyName("lastAccessAt")] DateTimeOffset? LastAccessAt);

}
