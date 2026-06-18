using System.ComponentModel;
using System.Text.Json;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Completion.Queries;
using MoodleConnector.Application.Tools;

namespace MoodleConnector.Presentation.Tools.Completion;

[McpServerToolType]
public sealed class MoodleCompletionTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver)
{
    [McpServerTool(
        Name = "consultar_progresso_aluno",
        Title = "Consultar Progresso Aluno",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<CourseCompletionStatus>))]
    [Description("Consulta o progresso e conclusao de um aluno em um curso, incluindo atividades concluidas e pendentes.")]
    public Task<CallToolResult> ConsultarProgressoAlunoAsync(
        [Description("Identificador do curso Moodle.")]
        string courseId,
        [Description("Identificador do estudante (ID do Moodle).")]
        string studentId,
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrao do usuario.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetCompletionCoreAsync(courseId, studentId, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "get_student_completion",
        Title = "Get Student Completion",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<CourseCompletionStatus>))]
    [Description("Gets the completion progress for a student in a course, including completed and pending activities.")]
    public Task<CallToolResult> GetStudentCompletionAsync(
        [Description("Moodle course identifier.")]
        string courseId,
        [Description("Student identifier (Moodle ID).")]
        string studentId,
        [Description("Moodle connection alias to query. When omitted, uses the user's default Moodle connection.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetCompletionCoreAsync(courseId, studentId, moodleAlias, cancellationToken);
    }

    private async Task<CallToolResult> GetCompletionCoreAsync(
        string courseId,
        string studentId,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return Error<CourseCompletionStatus>("Informe um identificador de curso valido.");
        }

        if (string.IsNullOrWhiteSpace(studentId))
        {
            return Error<CourseCompletionStatus>("Informe um identificador de estudante valido.");
        }

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return Error<CourseCompletionStatus>("Usuario nao autenticado para consultar o progresso.");
        }

        CourseCompletionStatus data;
        try
        {
            data = await mediator.Send(
                new GetStudentCompletionQuery(courseId, studentId),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Error<CourseCompletionStatus>($"Nao foi possivel consultar o progresso do aluno neste momento: {ex.Message}");
        }

        var response = new ToolResponse<CourseCompletionStatus>(
            "ok",
            data,
            [],
            AuditId: null,
            DateTimeOffset.UtcNow);

        var completas = data.Activities.Count(a => a.State == 1 || a.State == 2);
        var narration = $"O progresso do estudante {studentId} no curso {courseId} foi recuperado com sucesso. " +
                        $"Curso {(data.Completed ? "concluido" : "em andamento")}. " +
                        $"{completas}/{data.Activities.Count} atividades concluidas.";

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = narration }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private static CallToolResult Error<T>(string message)
    {
        var response = new ToolResponse<T>(
            "error",
            default!,
            [message],
            null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = message }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = true
        };
    }
}
