using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Activities;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;

namespace MoodleConnector.Presentation.Tools;

[McpServerToolType]
public sealed class MoodleCourseActivitiesTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver)
{
    [McpServerTool(
        Name = "listar_atividades_curso",
        Title = "Listar Atividades Curso",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListCourseActivitiesResponse>))]
    [Description("Lista atividades de um curso Moodle sem consultar submissões ou notas.")]
    public Task<CallToolResult> ListarAtividadesCursoAsync(
        [Description("Identificador do curso. Pode ser courseId, shortName ou idnumber.")]
        string courseId,
        [Description("Quando true, inclui atividades ocultas que o Moodle retornar para o usuario.")]
        bool incluirOcultas = false,
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrao do usuario.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListActivitiesCoreAsync(courseId, CourseActivityModuleTypes.All, incluirOcultas, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "list_course_activities",
        Title = "List Course Activities",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListCourseActivitiesResponse>))]
    [Description("Lists Moodle course activities without fetching submissions or grades.")]
    public Task<CallToolResult> ListCourseActivitiesAsync(
        [Description("Course identifier. Can be courseId, shortName, or idnumber.")]
        string courseId,
        [Description("When true, includes hidden activities Moodle returns for the user.")]
        bool includeHidden = false,
        [Description("Moodle connection alias to query. When omitted, uses the user's default Moodle connection.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListActivitiesCoreAsync(courseId, CourseActivityModuleTypes.All, includeHidden, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "consultar_atividade",
        Title = "Consultar Atividade",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<CourseActivityDetailsResponse>))]
    [Description("Consulta uma atividade por cmid ou instance id sem consultar submissões ou notas.")]
    public Task<CallToolResult> ConsultarAtividadeAsync(
        [Description("Identificador do curso. Pode ser courseId, shortName ou idnumber.")]
        string courseId,
        [Description("Identificador da atividade. Pode ser cmid ou instance id.")]
        string activityId,
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrao do usuario.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetActivityCoreAsync(courseId, activityId, CourseActivityModuleTypes.All, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "get_course_activity",
        Title = "Get Course Activity",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<CourseActivityDetailsResponse>))]
    [Description("Gets a course activity by cmid or instance id without fetching submissions or grades.")]
    public Task<CallToolResult> GetCourseActivityAsync(
        [Description("Course identifier. Can be courseId, shortName, or idnumber.")]
        string courseId,
        [Description("Activity identifier. Can be cmid or instance id.")]
        string activityId,
        [Description("Moodle connection alias to query. When omitted, uses the user's default Moodle connection.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetActivityCoreAsync(courseId, activityId, CourseActivityModuleTypes.All, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "listar_tarefas_curso",
        Title = "Listar Tarefas Curso",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListCourseActivitiesResponse>))]
    [Description("Lista tarefas do curso sem consultar entregas, submissões ou notas.")]
    public Task<CallToolResult> ListarTarefasCursoAsync(
        string courseId,
        bool incluirOcultas = false,
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListActivitiesCoreAsync(courseId, CourseActivityModuleTypes.Assignments, incluirOcultas, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "list_course_assignments",
        Title = "List Course Assignments",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListCourseActivitiesResponse>))]
    [Description("Lists course assignments without fetching submissions or grades.")]
    public Task<CallToolResult> ListCourseAssignmentsAsync(
        string courseId,
        bool includeHidden = false,
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListActivitiesCoreAsync(courseId, CourseActivityModuleTypes.Assignments, includeHidden, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "consultar_tarefa",
        Title = "Consultar Tarefa",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<CourseActivityDetailsResponse>))]
    [Description("Consulta uma tarefa por cmid ou instance id sem consultar entregas ou notas.")]
    public Task<CallToolResult> ConsultarTarefaAsync(
        string courseId,
        string assignmentId,
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetActivityCoreAsync(courseId, assignmentId, CourseActivityModuleTypes.Assignments, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "get_assignment",
        Title = "Get Assignment",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<CourseActivityDetailsResponse>))]
    [Description("Gets an assignment by cmid or instance id without fetching submissions or grades.")]
    public Task<CallToolResult> GetAssignmentAsync(
        string courseId,
        string assignmentId,
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetActivityCoreAsync(courseId, assignmentId, CourseActivityModuleTypes.Assignments, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "listar_quizzes_curso",
        Title = "Listar Quizzes Curso",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListCourseActivitiesResponse>))]
    [Description("Lista quizzes do curso sem consultar tentativas ou notas.")]
    public Task<CallToolResult> ListarQuizzesCursoAsync(
        string courseId,
        bool incluirOcultas = false,
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListActivitiesCoreAsync(courseId, CourseActivityModuleTypes.Quizzes, incluirOcultas, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "list_course_quizzes",
        Title = "List Course Quizzes",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListCourseActivitiesResponse>))]
    [Description("Lists course quizzes without fetching attempts or grades.")]
    public Task<CallToolResult> ListCourseQuizzesAsync(
        string courseId,
        bool includeHidden = false,
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListActivitiesCoreAsync(courseId, CourseActivityModuleTypes.Quizzes, includeHidden, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "consultar_quiz",
        Title = "Consultar Quiz",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<CourseActivityDetailsResponse>))]
    [Description("Consulta um quiz por cmid ou instance id sem consultar tentativas ou notas.")]
    public Task<CallToolResult> ConsultarQuizAsync(
        string courseId,
        string quizId,
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetActivityCoreAsync(courseId, quizId, CourseActivityModuleTypes.Quizzes, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "get_quiz",
        Title = "Get Quiz",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<CourseActivityDetailsResponse>))]
    [Description("Gets a quiz by cmid or instance id without fetching attempts or grades.")]
    public Task<CallToolResult> GetQuizAsync(
        string courseId,
        string quizId,
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetActivityCoreAsync(courseId, quizId, CourseActivityModuleTypes.Quizzes, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "listar_scorms_curso",
        Title = "Listar SCORMs Curso",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListCourseActivitiesResponse>))]
    [Description("Lista SCORMs do curso sem consultar tentativas ou notas.")]
    public Task<CallToolResult> ListarScormsCursoAsync(
        string courseId,
        bool incluirOcultas = false,
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListActivitiesCoreAsync(courseId, CourseActivityModuleTypes.Scorms, incluirOcultas, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "list_course_scorms",
        Title = "List Course SCORMs",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListCourseActivitiesResponse>))]
    [Description("Lists course SCORMs without fetching attempts or grades.")]
    public Task<CallToolResult> ListCourseScormsAsync(
        string courseId,
        bool includeHidden = false,
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListActivitiesCoreAsync(courseId, CourseActivityModuleTypes.Scorms, includeHidden, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "consultar_prazos_atividades",
        Title = "Consultar Prazos Atividades",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListActivityDeadlinesResponse>))]
    [Description("Consulta datas e prazos de atividades do curso sem consultar submissões, tentativas ou notas.")]
    public Task<CallToolResult> ConsultarPrazosAtividadesAsync(
        string courseId,
        bool incluirOcultas = false,
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListDeadlinesCoreAsync(courseId, CourseActivityModuleTypes.All, incluirOcultas, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "list_activity_deadlines",
        Title = "List Activity Deadlines",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListActivityDeadlinesResponse>))]
    [Description("Lists course activity dates and deadlines without fetching submissions, attempts, or grades.")]
    public Task<CallToolResult> ListActivityDeadlinesAsync(
        string courseId,
        bool includeHidden = false,
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListDeadlinesCoreAsync(courseId, CourseActivityModuleTypes.All, includeHidden, moodleAlias, cancellationToken);
    }

    private async Task<CallToolResult> ListActivitiesCoreAsync(
        string courseId,
        IReadOnlyCollection<string> activityTypes,
        bool includeHidden,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return Error<ListCourseActivitiesResponse>("Informe um identificador de curso.");
        }

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return Error<ListCourseActivitiesResponse>("Usuario nao autenticado para consultar atividades.");
        }

        CourseActivitiesSummary? activities;
        try
        {
            activities = await mediator.Send(
                new ListCourseActivitiesQuery(moodleUserId.Value.ToString(), courseId, activityTypes, includeHidden),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Error<ListCourseActivitiesResponse>("Nao foi possivel listar atividades no Moodle neste momento.");
        }

        if (activities is null)
        {
            return Error<ListCourseActivitiesResponse>("Curso nao encontrado entre os cursos vinculados ao usuario.");
        }

        var data = ToActivitiesResponse(activities);
        var response = new ToolResponse<ListCourseActivitiesResponse>("ok", data, [], AuditId: null, DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildActivitiesNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private async Task<CallToolResult> GetActivityCoreAsync(
        string courseId,
        string activityId,
        IReadOnlyCollection<string> allowedActivityTypes,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return Error<CourseActivityDetailsResponse>("Informe um identificador de curso.");
        }

        if (string.IsNullOrWhiteSpace(activityId))
        {
            return Error<CourseActivityDetailsResponse>("Informe um identificador de atividade.");
        }

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return Error<CourseActivityDetailsResponse>("Usuario nao autenticado para consultar atividade.");
        }

        CourseActivitySummary? activity;
        try
        {
            activity = await mediator.Send(
                new GetCourseActivityQuery(moodleUserId.Value.ToString(), courseId, activityId, allowedActivityTypes),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Error<CourseActivityDetailsResponse>("Nao foi possivel consultar a atividade no Moodle neste momento.");
        }

        if (activity is null)
        {
            return Error<CourseActivityDetailsResponse>("Atividade nao encontrada no curso informado.");
        }

        var data = new CourseActivityDetailsResponse(ToActivityItem(activity));
        var response = new ToolResponse<CourseActivityDetailsResponse>("ok", data, [], AuditId: null, DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = $"Atividade encontrada: {activity.Name} ({activity.ActivityType}, ID: {activity.ActivityId})." }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private async Task<CallToolResult> ListDeadlinesCoreAsync(
        string courseId,
        IReadOnlyCollection<string> activityTypes,
        bool includeHidden,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return Error<ListActivityDeadlinesResponse>("Informe um identificador de curso.");
        }

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return Error<ListActivityDeadlinesResponse>("Usuario nao autenticado para consultar prazos de atividades.");
        }

        CourseActivityDeadlinesSummary? deadlines;
        try
        {
            deadlines = await mediator.Send(
                new ListActivityDeadlinesQuery(moodleUserId.Value.ToString(), courseId, activityTypes, includeHidden),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Error<ListActivityDeadlinesResponse>("Nao foi possivel consultar prazos de atividades no Moodle neste momento.");
        }

        if (deadlines is null)
        {
            return Error<ListActivityDeadlinesResponse>("Curso nao encontrado entre os cursos vinculados ao usuario.");
        }

        var data = ToDeadlinesResponse(deadlines);
        var response = new ToolResponse<ListActivityDeadlinesResponse>("ok", data, [], AuditId: null, DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildDeadlinesNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private static string BuildActivitiesNarration(ListCourseActivitiesResponse response)
    {
        if (response.Total == 0)
        {
            return "Nao encontrei atividades para os filtros informados.";
        }

        return $"Encontrei {response.Total} atividade(s). {response.WithoutDeadlineCount} sem prazo de fechamento/entrega retornado pelo Moodle.";
    }

    private static string BuildDeadlinesNarration(ListActivityDeadlinesResponse response)
    {
        return $"Consultei prazos de {response.Total} atividade(s). {response.WithoutDatesCount} sem datas e {response.WithoutDeadlineCount} sem prazo de fechamento/entrega retornado.";
    }

    private static ListCourseActivitiesResponse ToActivitiesResponse(CourseActivitiesSummary summary)
    {
        return new ListCourseActivitiesResponse(
            summary.CourseId,
            summary.ActivityTypeFilters,
            summary.IncludeHidden,
            summary.Total,
            summary.WithoutDatesCount,
            summary.WithoutDeadlineCount,
            summary.Activities.Select(ToActivityItem).ToArray());
    }

    private static ListActivityDeadlinesResponse ToDeadlinesResponse(CourseActivityDeadlinesSummary summary)
    {
        return new ListActivityDeadlinesResponse(
            summary.CourseId,
            summary.ActivityTypeFilters,
            summary.IncludeHidden,
            summary.Total,
            summary.WithoutDatesCount,
            summary.WithoutDeadlineCount,
            summary.Deadlines.Select(deadline => new ActivityDeadlineItem(
                deadline.ActivityId,
                deadline.InstanceId,
                deadline.ActivityType,
                deadline.Name,
                deadline.Visible,
                deadline.UserVisible,
                deadline.HasDates,
                deadline.HasDeadline,
                deadline.OpenAt,
                deadline.DueAt,
                deadline.CloseAt,
                deadline.Dates.Select(date => new ActivityDateItem(date.Label, date.Date)).ToArray())).ToArray());
    }

    private static ActivityItem ToActivityItem(CourseActivitySummary activity)
    {
        return new ActivityItem(
            activity.ActivityId,
            activity.InstanceId,
            activity.ActivityType,
            activity.Name,
            activity.Url,
            activity.Visible,
            activity.UserVisible,
            activity.Description,
            activity.AvailabilityInfo,
            activity.HasDates,
            activity.HasDeadline,
            activity.OpenAt,
            activity.DueAt,
            activity.CloseAt,
            activity.Dates.Select(date => new ActivityDateItem(date.Label, date.Date)).ToArray(),
            activity.FileCount);
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

    public sealed record ListCourseActivitiesResponse(
        [property: JsonPropertyName("courseId")] string CourseId,
        [property: JsonPropertyName("activityTypeFilters")] IReadOnlyCollection<string> ActivityTypeFilters,
        [property: JsonPropertyName("includeHidden")] bool IncludeHidden,
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("withoutDatesCount")] int WithoutDatesCount,
        [property: JsonPropertyName("withoutDeadlineCount")] int WithoutDeadlineCount,
        [property: JsonPropertyName("activities")] IReadOnlyList<ActivityItem> Activities);

    public sealed record CourseActivityDetailsResponse(
        [property: JsonPropertyName("activity")] ActivityItem Activity);

    public sealed record ActivityItem(
        [property: JsonPropertyName("activityId")] string ActivityId,
        [property: JsonPropertyName("instanceId")] string? InstanceId,
        [property: JsonPropertyName("activityType")] string ActivityType,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("visible")] bool? Visible,
        [property: JsonPropertyName("userVisible")] bool? UserVisible,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("availabilityInfo")] string? AvailabilityInfo,
        [property: JsonPropertyName("hasDates")] bool HasDates,
        [property: JsonPropertyName("hasDeadline")] bool HasDeadline,
        [property: JsonPropertyName("openAt")] DateTimeOffset? OpenAt,
        [property: JsonPropertyName("dueAt")] DateTimeOffset? DueAt,
        [property: JsonPropertyName("closeAt")] DateTimeOffset? CloseAt,
        [property: JsonPropertyName("dates")] IReadOnlyList<ActivityDateItem> Dates,
        [property: JsonPropertyName("fileCount")] int FileCount);

    public sealed record ActivityDateItem(
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("date")] DateTimeOffset Date);

    public sealed record ListActivityDeadlinesResponse(
        [property: JsonPropertyName("courseId")] string CourseId,
        [property: JsonPropertyName("activityTypeFilters")] IReadOnlyCollection<string> ActivityTypeFilters,
        [property: JsonPropertyName("includeHidden")] bool IncludeHidden,
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("withoutDatesCount")] int WithoutDatesCount,
        [property: JsonPropertyName("withoutDeadlineCount")] int WithoutDeadlineCount,
        [property: JsonPropertyName("deadlines")] IReadOnlyList<ActivityDeadlineItem> Deadlines);

    public sealed record ActivityDeadlineItem(
        [property: JsonPropertyName("activityId")] string ActivityId,
        [property: JsonPropertyName("instanceId")] string? InstanceId,
        [property: JsonPropertyName("activityType")] string ActivityType,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("visible")] bool? Visible,
        [property: JsonPropertyName("userVisible")] bool? UserVisible,
        [property: JsonPropertyName("hasDates")] bool HasDates,
        [property: JsonPropertyName("hasDeadline")] bool HasDeadline,
        [property: JsonPropertyName("openAt")] DateTimeOffset? OpenAt,
        [property: JsonPropertyName("dueAt")] DateTimeOffset? DueAt,
        [property: JsonPropertyName("closeAt")] DateTimeOffset? CloseAt,
        [property: JsonPropertyName("dates")] IReadOnlyList<ActivityDateItem> Dates);
}
