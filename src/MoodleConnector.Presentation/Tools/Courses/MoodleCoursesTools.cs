using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Courses;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;

namespace MoodleConnector.Presentation.Tools;

[McpServerToolType]
public sealed class MoodleCoursesTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver)
{
    [McpServerTool(
        Name = "listar_meus_cursos",
        Title = "Listar Meus Cursos",
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
        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return Error<ListMyCoursesResponse>("Usuario nao autenticado para listar cursos.");
        }

        PagedCourses paged;
        try
        {
            paged = await mediator.Send(new ListMyCoursesQuery(moodleUserId.Value.ToString(), limite, pagina), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Error<ListMyCoursesResponse>("Nao foi possivel listar os cursos no Moodle neste momento.");
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
            AuditId: null,
            DateTimeOffset.UtcNow);

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

    [McpServerTool(
        Name = "list_courses",
        Title = "List Courses",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListMyCoursesResponse>))]
    [Description("Lists courses linked to the authenticated Moodle user with pagination support. Use the page parameter to navigate through results.")]
    public Task<CallToolResult> ListCoursesAsync(
        [Description("Maximum number of courses per page (1 to 100). Default: 20.")]
        int limit = 20,
        [Description("Page number to return (1-based). Use to paginate and list all courses. Default: 1.")]
        int page = 1,
        [Description("Moodle connection alias to query, such as goias, nacional, or ctm. When omitted, uses the user's default Moodle connection.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListarMeusCursosAsync(limit, page, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "buscar_cursos",
        Title = "Buscar Cursos",
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

    [McpServerTool(
        Name = "search_courses",
        Title = "Search Courses",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListMyCoursesResponse>))]
    [Description("Searches courses linked to the authenticated Moodle user by id, short name, idnumber, full name, or category. Searches courses only, not internal files, activities, or course materials.")]
    public Task<CallToolResult> SearchCoursesAsync(
        [Description("Search term. Can be courseId, shortName, idnumber, course name, or category.")]
        string query,
        [Description("Maximum number of courses to return (1 to 20).")]
        int limit = 10,
        [Description("Moodle connection alias to query. When omitted, uses the user's default Moodle connection.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return SearchCoursesCoreAsync(query, limit, moodleAlias, cancellationToken);
    }

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return StandardError($"Nao foi possivel buscar cursos no Moodle neste momento ({ex.GetType().Name}).");
        }

        var response = new SearchResponse(courses.Select(course =>
            new SearchResult(
                course.CourseId,
                string.IsNullOrWhiteSpace(course.DisplayName) ? course.FullName : course.DisplayName,
                BuildCourseUrl(course))).ToArray());

        return StandardResult(response);
    }

    [McpServerTool(
        Name = "consultar_curso",
        Title = "Consultar Curso",
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

    [McpServerTool(
        Name = "get_course",
        Title = "Get Course",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<CourseDetailsResponse>))]
    [Description("Gets basic data for a course linked to the authenticated Moodle user without grades, submissions, or risk data.")]
    public Task<CallToolResult> GetCourseAsync(
        [Description("Course identifier. Can be courseId, shortName, or idnumber.")]
        string courseId,
        [Description("Moodle connection alias to query. When omitted, uses the user's default Moodle connection.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetCourseCoreAsync(courseId, moodleAlias, cancellationToken);
    }

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
            return Error<ListMyCoursesResponse>("Informe um termo de busca para localizar cursos.");
        }

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return Error<ListMyCoursesResponse>("Usuario nao autenticado para buscar cursos.");
        }

        IReadOnlyList<CourseSummary> courses;
        try
        {
            courses = await mediator.Send(new SearchCoursesQuery(moodleUserId.Value.ToString(), query, limit), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error<ListMyCoursesResponse>($"Nao foi possivel buscar cursos no Moodle neste momento ({ex.GetType().Name}).");
        }

        var data = new ListMyCoursesResponse(courses.Count, 1, 1, false, courses.Select(ToCourseItem).ToArray());
        var response = new ToolResponse<ListMyCoursesResponse>("ok", data, [], AuditId: null, DateTimeOffset.UtcNow);

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
            return Error<CourseDetailsResponse>("Informe um identificador de curso.");
        }

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return Error<CourseDetailsResponse>("Usuario nao autenticado para consultar curso.");
        }

        CourseSummary? course;
        try
        {
            course = await mediator.Send(new GetCourseQuery(moodleUserId.Value.ToString(), courseId), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Error<CourseDetailsResponse>("Nao foi possivel consultar o curso no Moodle neste momento.");
        }

        if (course is null)
        {
            return Error<CourseDetailsResponse>("Curso nao encontrado entre os cursos vinculados ao usuario.");
        }

        var data = new CourseDetailsResponse(ToCourseItem(course));
        var response = new ToolResponse<CourseDetailsResponse>("ok", data, [], AuditId: null, DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = $"Curso encontrado: {course.FullName} (ID: {course.CourseId})." }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private static string BuildNarration(ListMyCoursesResponse response)
    {
        if (response.Total == 0)
        {
            return "Nao encontrei cursos no momento para este usuario.";
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

    private static CallToolResult Error<T>(string message)
    {
        var response = new ToolResponse<T>(
            "error",
            Data: default,
            Warnings: [message],
            AuditId: null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = message }],
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
