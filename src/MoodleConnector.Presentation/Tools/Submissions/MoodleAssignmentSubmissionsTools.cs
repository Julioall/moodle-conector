using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Submissions;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;

namespace MoodleConnector.Presentation.Tools;

[McpServerToolType]
public sealed class MoodleAssignmentSubmissionsTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver)
{
    [McpServerTool(
        Name = "list_assignment_submissions",
        Title = "List Assignment Submissions",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListAssignmentSubmissionsResponse>))]
    [Description("Lista entregas de uma tarefa Moodle com paginacao. Nao baixa anexos nem retorna texto integral da submissao.")]
    public Task<CallToolResult> ListarEntregasAtividadeAsync(
        [Description("Identificador do curso. Pode ser courseId, shortName ou idnumber.")]
        string courseId,
        [Description("Identificador da tarefa. Pode ser cmid ou instance id.")]
        string assignmentId,
        [Description("Pagina de resultados, iniciando em 1.")]
        int pagina = 1,
        [Description("Tamanho da pagina, de 1 a 100.")]
        int tamanhoPagina = 20,
        [Description("Filtro: todos, entregues, pendentes, atrasadas ou aguardando_correcao.")]
        string status = "todos",
        [Description("Retorna entregas modificadas a partir desta data, quando informado.")]
        DateTimeOffset? desde = null,
        [Description("Retorna entregas modificadas antes desta data, quando informado.")]
        DateTimeOffset? antes = null,
        [Description("Quando false, remove entregas atrasadas dos relatorios gerais.")]
        bool incluirAtrasadas = true,
        [Description("Quando false, remove entregas aguardando correcao dos relatorios gerais.")]
        bool incluirNaoCorrigidas = true,
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrao do usuario.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseFilter(status, out var filter))
        {
            return Task.FromResult(ToolResultHelper.Error<ListAssignmentSubmissionsResponse>("Filtro de status invalido. Use todos, entregues, pendentes, atrasadas ou aguardando_correcao."));
        }

        return ListSubmissionsCoreAsync(
            courseId,
            assignmentId,
            filter,
            pagina,
            tamanhoPagina,
            desde,
            antes,
            incluirAtrasadas,
            incluirNaoCorrigidas,
            moodleAlias,
            cancellationToken);
    }

    [McpServerTool(
        Name = "get_student_submission",
        Title = "Get Student Submission",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<StudentSubmissionResponse>))]
    [Description("Consulta o status de entrega de um estudante em uma tarefa, sem retornar texto integral ou anexos.")]
    public Task<CallToolResult> ConsultarEntregaAlunoAsync(
        string courseId,
        string assignmentId,
        string studentId,
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetStudentSubmissionCoreAsync(courseId, assignmentId, studentId, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "list_pending_submissions",
        Title = "List Pending Submissions",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListAssignmentSubmissionsResponse>))]
    [Description("Lista estudantes ativos sem submissao entregue para uma tarefa Moodle.")]
    public Task<CallToolResult> ListarEntregasPendentesAsync(
        string courseId,
        string assignmentId,
        int pagina = 1,
        int tamanhoPagina = 20,
        DateTimeOffset? desde = null,
        DateTimeOffset? antes = null,
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListSubmissionsCoreAsync(
            courseId,
            assignmentId,
            AssignmentSubmissionFilter.NotSubmitted,
            pagina,
            tamanhoPagina,
            desde,
            antes,
            includeLate: true,
            includeUngraded: true,
            moodleAlias,
            cancellationToken);
    }

    [McpServerTool(
        Name = "list_late_submissions",
        Title = "List Late Submissions",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListAssignmentSubmissionsResponse>))]
    [Description("Lista entregas enviadas apos o prazo retornado pelo Moodle para a tarefa.")]
    public Task<CallToolResult> ListarEntregasAtrasadasAsync(
        string courseId,
        string assignmentId,
        int pagina = 1,
        int tamanhoPagina = 20,
        DateTimeOffset? desde = null,
        DateTimeOffset? antes = null,
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListSubmissionsCoreAsync(
            courseId,
            assignmentId,
            AssignmentSubmissionFilter.Late,
            pagina,
            tamanhoPagina,
            desde,
            antes,
            includeLate: true,
            includeUngraded: true,
            moodleAlias,
            cancellationToken);
    }

    [McpServerTool(
        Name = "list_submissions_awaiting_grading",
        Title = "List Submissions Awaiting Grading",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListAssignmentSubmissionsResponse>))]
    [Description("Lista entregas enviadas que ainda aguardam correcao conforme status de avaliacao retornado pelo Moodle.")]
    public Task<CallToolResult> ListarEntregasAguardandoCorrecaoAsync(
        string courseId,
        string assignmentId,
        int pagina = 1,
        int tamanhoPagina = 20,
        DateTimeOffset? desde = null,
        DateTimeOffset? antes = null,
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListSubmissionsCoreAsync(
            courseId,
            assignmentId,
            AssignmentSubmissionFilter.NeedsGrading,
            pagina,
            tamanhoPagina,
            desde,
            antes,
            includeLate: true,
            includeUngraded: true,
            moodleAlias,
            cancellationToken);
    }

    [McpServerTool(
        Name = "get_submission_status",
        Title = "Get Submission Status",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<StudentSubmissionResponse>))]
    [Description("Consulta o status de submissao de um estudante em uma tarefa Moodle.")]
    public Task<CallToolResult> ConsultarStatusSubmissaoAsync(
        string courseId,
        string assignmentId,
        string studentId,
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetStudentSubmissionCoreAsync(courseId, assignmentId, studentId, moodleAlias, cancellationToken);
    }

    private async Task<CallToolResult> ListSubmissionsCoreAsync(
        string courseId,
        string assignmentId,
        AssignmentSubmissionFilter filter,
        int page,
        int pageSize,
        DateTimeOffset? since,
        DateTimeOffset? before,
        bool includeLate,
        bool includeUngraded,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return ToolResultHelper.Error<ListAssignmentSubmissionsResponse>("Informe um identificador de curso.");
        }

        if (string.IsNullOrWhiteSpace(assignmentId))
        {
            return ToolResultHelper.Error<ListAssignmentSubmissionsResponse>("Informe um identificador de tarefa.");
        }

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return ToolResultHelper.Error<ListAssignmentSubmissionsResponse>("Usuario nao autenticado para consultar entregas.");
        }

        AssignmentSubmissionsPage? submissionsPage;
        try
        {
            submissionsPage = await mediator.Send(
                new ListAssignmentSubmissionsQuery(
                    moodleUserId.Value.ToString(),
                    courseId,
                    assignmentId,
                    filter,
                    page,
                    pageSize,
                    since,
                    before,
                    includeLate,
                    includeUngraded),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return ToolResultHelper.Error<ListAssignmentSubmissionsResponse>(ex.Message);
        }
        catch
        {
            return ToolResultHelper.Error<ListAssignmentSubmissionsResponse>("Nao foi possivel listar entregas no Moodle neste momento.");
        }

        if (submissionsPage is null)
        {
            return ToolResultHelper.Error<ListAssignmentSubmissionsResponse>("Curso ou tarefa nao encontrados entre os dados autorizados do usuario.");
        }

        var data = ToListResponse(submissionsPage);
        var response = new ToolResponse<ListAssignmentSubmissionsResponse>("ok", data, [], AuditId: null, DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildSubmissionsNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private async Task<CallToolResult> GetStudentSubmissionCoreAsync(
        string courseId,
        string assignmentId,
        string studentId,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return ToolResultHelper.Error<StudentSubmissionResponse>("Informe um identificador de curso.");
        }

        if (string.IsNullOrWhiteSpace(assignmentId))
        {
            return ToolResultHelper.Error<StudentSubmissionResponse>("Informe um identificador de tarefa.");
        }

        if (string.IsNullOrWhiteSpace(studentId))
        {
            return ToolResultHelper.Error<StudentSubmissionResponse>("Informe um identificador de estudante.");
        }

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return ToolResultHelper.Error<StudentSubmissionResponse>("Usuario nao autenticado para consultar entrega.");
        }

        AssignmentSubmissionSummary? submission;
        try
        {
            submission = await mediator.Send(
                new GetStudentSubmissionQuery(
                    moodleUserId.Value.ToString(),
                    courseId,
                    assignmentId,
                    studentId),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ToolResultHelper.Error<StudentSubmissionResponse>("Nao foi possivel consultar a entrega no Moodle neste momento.");
        }

        if (submission is null)
        {
            return ToolResultHelper.Error<StudentSubmissionResponse>("Estudante, curso ou tarefa nao encontrados entre os dados autorizados do usuario.");
        }

        var data = new StudentSubmissionResponse(courseId, assignmentId, ToSubmissionItem(submission));
        var response = new ToolResponse<StudentSubmissionResponse>("ok", data, [], AuditId: null, DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildStudentSubmissionNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private static ListAssignmentSubmissionsResponse ToListResponse(AssignmentSubmissionsPage page)
    {
        return new ListAssignmentSubmissionsResponse(
            page.CourseId,
            page.AssignmentId,
            page.AssignmentModuleId,
            page.AssignmentName,
            page.Page,
            page.PageSize,
            ToFilterText(page.Filter),
            page.IncludeLate,
            page.IncludeUngraded,
            page.Since,
            page.Before,
            page.Total,
            page.HasMore,
            page.Submissions.Count,
            page.Submissions.Select(ToSubmissionItem).ToArray());
    }

    private static SubmissionItem ToSubmissionItem(AssignmentSubmissionSummary submission)
    {
        return new SubmissionItem(
            submission.UserId,
            submission.FullName,
            submission.SubmissionId,
            submission.Status,
            submission.GradingStatus,
            submission.Submitted,
            submission.Late,
            submission.NeedsGrading,
            submission.SubmittedAt,
            submission.ModifiedAt,
            submission.AttemptNumber,
            submission.FileCount,
            submission.HasOnlineText);
    }

    private static string BuildSubmissionsNarration(ListAssignmentSubmissionsResponse response)
    {
        if (response.Count == 0)
        {
            if (response.Page > 1)
            {
                return $"A pagina {response.Page} nao retornou entregas. O numero maximo de paginas pode ter sido ultrapassado.";
            }

            if (response.Filter != "all")
            {
                return $"Nenhuma entrega corresponde ao filtro '{response.Filter}' na tarefa '{response.AssignmentName}'.";
            }

            if (response.Total == 0)
            {
                return $"Nenhum aluno elegivel encontrado para a tarefa '{response.AssignmentName}'. Nao ha entregas possiveis.";
            }

            return $"Nenhuma entrega encontrada para a tarefa '{response.AssignmentName}'.";
        }

        var lines = response.Submissions.Select(submission =>
            $"- {submission.FullName ?? "Usuario " + submission.UserId} (ID: {submission.UserId}): {BuildStatusLabel(submission)}");
        var suffix = response.HasMore ? "\nHa mais resultados. Avance a pagina para continuar." : string.Empty;

        return $"Encontrei {response.Count} registro(s) na pagina {response.Page}, de {response.Total} registro(s) filtrado(s):\n" +
               string.Join("\n", lines) +
               suffix;
    }

    private static string BuildStudentSubmissionNarration(StudentSubmissionResponse response)
    {
        var submission = response.Submission;
        return $"{submission.FullName ?? "Usuario " + submission.UserId} (ID: {submission.UserId}): {BuildStatusLabel(submission)}.";
    }

    private static string BuildStatusLabel(SubmissionItem submission)
    {
        if (!submission.Submitted)
        {
            return "nao entregue";
        }

        var labels = new List<string> { "entregue" };
        if (submission.Late)
        {
            labels.Add("atrasada");
        }

        if (submission.NeedsGrading)
        {
            labels.Add("aguardando correcao");
        }

        return string.Join(", ", labels);
    }

    private static bool TryParseFilter(string? value, out AssignmentSubmissionFilter filter)
    {
        var normalized = (string.IsNullOrWhiteSpace(value) ? "all" : value.Trim()).ToLowerInvariant();
        filter = normalized switch
        {
            "all" or "todos" or "todas" => AssignmentSubmissionFilter.All,
            "submitted" or "entregue" or "entregues" => AssignmentSubmissionFilter.Submitted,
            "pending" or "not_submitted" or "not-submitted" or "pendente" or "pendentes" or "nao_entregue" or "nao_entregues" => AssignmentSubmissionFilter.NotSubmitted,
            "late" or "atrasada" or "atrasadas" => AssignmentSubmissionFilter.Late,
            "awaiting_grading" or "needs_grading" or "needs-grading" or "aguardando_correcao" or "sem_correcao" => AssignmentSubmissionFilter.NeedsGrading,
            _ => AssignmentSubmissionFilter.All
        };

        return normalized is "all" or "todos" or "todas" or
            "submitted" or "entregue" or "entregues" or
            "pending" or "not_submitted" or "not-submitted" or "pendente" or "pendentes" or "nao_entregue" or "nao_entregues" or
            "late" or "atrasada" or "atrasadas" or
            "awaiting_grading" or "needs_grading" or "needs-grading" or "aguardando_correcao" or "sem_correcao";
    }

    private static string ToFilterText(AssignmentSubmissionFilter filter)
    {
        return filter switch
        {
            AssignmentSubmissionFilter.Submitted => "submitted",
            AssignmentSubmissionFilter.NotSubmitted => "not_submitted",
            AssignmentSubmissionFilter.Late => "late",
            AssignmentSubmissionFilter.NeedsGrading => "awaiting_grading",
            AssignmentSubmissionFilter.All => "all",
            _ => "all"
        };
    }



    public sealed record ListAssignmentSubmissionsResponse(
        [property: JsonPropertyName("courseId")] string CourseId,
        [property: JsonPropertyName("assignmentId")] string AssignmentId,
        [property: JsonPropertyName("assignmentModuleId")] string? AssignmentModuleId,
        [property: JsonPropertyName("assignmentName")] string AssignmentName,
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("pageSize")] int PageSize,
        [property: JsonPropertyName("filter")] string Filter,
        [property: JsonPropertyName("includeLate")] bool IncludeLate,
        [property: JsonPropertyName("includeUngraded")] bool IncludeUngraded,
        [property: JsonPropertyName("since")] DateTimeOffset? Since,
        [property: JsonPropertyName("before")] DateTimeOffset? Before,
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("hasMore")] bool HasMore,
        [property: JsonPropertyName("count")] int Count,
        [property: JsonPropertyName("submissions")] IReadOnlyList<SubmissionItem> Submissions);

    public sealed record StudentSubmissionResponse(
        [property: JsonPropertyName("courseId")] string CourseId,
        [property: JsonPropertyName("assignmentId")] string AssignmentId,
        [property: JsonPropertyName("submission")] SubmissionItem Submission);

    public sealed record SubmissionItem(
        [property: JsonPropertyName("userId")] string UserId,
        [property: JsonPropertyName("fullName")] string? FullName,
        [property: JsonPropertyName("submissionId")] string? SubmissionId,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("gradingStatus")] string? GradingStatus,
        [property: JsonPropertyName("submitted")] bool Submitted,
        [property: JsonPropertyName("late")] bool Late,
        [property: JsonPropertyName("needsGrading")] bool NeedsGrading,
        [property: JsonPropertyName("submittedAt")] DateTimeOffset? SubmittedAt,
        [property: JsonPropertyName("modifiedAt")] DateTimeOffset? ModifiedAt,
        [property: JsonPropertyName("attemptNumber")] int? AttemptNumber,
        [property: JsonPropertyName("fileCount")] int FileCount,
        [property: JsonPropertyName("hasOnlineText")] bool HasOnlineText);
}
