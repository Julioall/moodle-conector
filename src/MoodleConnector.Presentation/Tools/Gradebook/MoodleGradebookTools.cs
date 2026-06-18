using System.ComponentModel;
using System.Text.Json;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Gradebook.Queries;
using MoodleConnector.Application.Tools;

namespace MoodleConnector.Presentation.Tools.Gradebook;

[McpServerToolType]
public sealed class MoodleGradebookTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver)
{
    [McpServerTool(
        Name = "consultar_boletim_aluno",
        Title = "Consultar Boletim Aluno",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<CourseGradebook>))]
    [Description("Consulta o boletim (gradebook) de um estudante em um curso. Retorna notas por atividade, categoria e nota final do curso.")]
    public Task<CallToolResult> ConsultarBoletimAlunoAsync(
        [Description("Identificador do curso Moodle.")]
        string courseId,
        [Description("Identificador do estudante (ID do Moodle).")]
        string studentId,
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrao do usuario.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetGradebookCoreAsync(courseId, studentId, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "get_student_gradebook",
        Title = "Get Student Gradebook",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<CourseGradebook>))]
    [Description("Gets the gradebook for a student in a course. Returns grades by activity, category, and final course grade.")]
    public Task<CallToolResult> GetStudentGradebookAsync(
        [Description("Moodle course identifier.")]
        string courseId,
        [Description("Student identifier (Moodle ID).")]
        string studentId,
        [Description("Moodle connection alias to query. When omitted, uses the user's default Moodle connection.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetGradebookCoreAsync(courseId, studentId, moodleAlias, cancellationToken);
    }

    private async Task<CallToolResult> GetGradebookCoreAsync(
        string courseId,
        string studentId,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return Error<CourseGradebook>("Informe um identificador de curso valido.");
        }

        if (string.IsNullOrWhiteSpace(studentId))
        {
            return Error<CourseGradebook>("Informe um identificador de estudante valido.");
        }

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return Error<CourseGradebook>("Usuario nao autenticado para consultar o boletim.");
        }

        CourseGradebook data;
        try
        {
            data = await mediator.Send(
                new GetStudentGradebookQuery(courseId, studentId),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Error<CourseGradebook>($"Nao foi possivel consultar o boletim do aluno neste momento: {ex.Message}");
        }

        var response = new ToolResponse<CourseGradebook>(
            "ok",
            data,
            [],
            AuditId: null,
            DateTimeOffset.UtcNow);

        var narration = $"O boletim do estudante {studentId} no curso {courseId} foi recuperado com sucesso. " +
                        $"Total de {data.Items.Count} itens avaliativos encontrados.";

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
