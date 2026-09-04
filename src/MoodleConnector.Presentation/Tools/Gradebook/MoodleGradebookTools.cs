using System.ComponentModel;
using System.Text.Json;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Gradebook.Queries;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;
using MoodleConnector.Presentation.Tools;

namespace MoodleConnector.Presentation.Tools.Gradebook;

[McpServerToolType]
public sealed class MoodleGradebookTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver,
    MoodleSnapshotToolContext? snapshotContext = null)
{
    [McpServerTool(
        Name = "get_student_gradebook",
        Title = "Get Student Gradebook",
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

    private async Task<CallToolResult> GetGradebookCoreAsync(
        string courseId,
        string studentId,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return ToolResultHelper.Error<CourseGradebook>("Informe um identificador de curso valido.");
        }

        if (string.IsNullOrWhiteSpace(studentId))
        {
            return ToolResultHelper.Error<CourseGradebook>("Informe um identificador de estudante valido.");
        }

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return ToolResultHelper.Error<CourseGradebook>("Usuario nao autenticado para consultar o boletim.");
        }

        var effectiveCourseId = courseId;
        CourseGradebookSnapshot? prefetchedGradebook = null;
        ToolFreshness? freshness = null;
        var snapshotContainsStudent = false;
        if (snapshotContext is not null)
        {
            try
            {
                var courseRead = await snapshotContext.ReadAsync(
                    new CourseReadSnapshotRequest(
                        courseId,
                        moodleAlias,
                        moodleUserId.Value.ToString(),
                        CourseReadSnapshotRequirements.Gradebook),
                    cancellationToken);
                if (courseRead is not null)
                {
                    effectiveCourseId = courseRead.CourseId;
                    prefetchedGradebook = courseRead.Gradebook?.Data;
                    snapshotContainsStudent = prefetchedGradebook?.TryGetForStudent(studentId, out _) == true;
                    if (snapshotContainsStudent && courseRead.Gradebook is not null)
                    {
                        var updatedAt = courseRead.Gradebook.UpdatedAt;
                        freshness = new ToolFreshness(
                            "snapshot",
                            updatedAt,
                            Math.Max(0, (long)(DateTimeOffset.UtcNow - updatedAt).TotalSeconds),
                            courseRead.Gradebook.IsStale,
                            courseRead.Metadata.RefreshQueued,
                            courseRead.Gradebook.IsComplete && courseRead.Gradebook.Data.Coverage.IsComplete,
                            courseRead.Gradebook.RecordCount);
                    }
                    else
                    {
                        // The coordinator may queue a refresh while the
                        // authoritative individual read still comes from
                        // Moodle. Do not label that live response as an empty
                        // snapshot (or expose a null snapshot timestamp).
                        freshness = new ToolFreshness(
                            "live",
                            null,
                            null,
                            false,
                            courseRead.Metadata.RefreshQueued,
                            false,
                            0);
                    }
                }
            }
            catch
            {
                // Snapshot lookup is an optimization. Keep the live path.
            }
        }

        CourseGradebook data;
        try
        {
            data = await mediator.Send(
                new GetStudentGradebookQuery(effectiveCourseId, studentId, prefetchedGradebook),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ToolResultHelper.Error<CourseGradebook>("Nao foi possivel consultar o boletim do aluno neste momento.");
        }

        var response = new ToolResponse<CourseGradebook>(
            "ok",
            data,
            [],
            AuditId: null,
            DateTimeOffset.UtcNow,
            Freshness: freshness);

        var narration = $"O boletim do estudante {studentId} no curso {courseId} foi recuperado com sucesso. " +
                        $"Total de {data.Items.Count} itens avaliativos encontrados.";

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = narration }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }


}
