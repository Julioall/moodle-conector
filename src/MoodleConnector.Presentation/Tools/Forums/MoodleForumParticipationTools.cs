using System.ComponentModel;
using System.Text.Json;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Forums.Queries;
using MoodleConnector.Application.Tools;

namespace MoodleConnector.Presentation.Tools.Forums;

[McpServerToolType]
public sealed class MoodleForumParticipationTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver)
{
    [McpServerTool(
        Name = "listar_alunos_sem_participacao_forum",
        Title = "Listar Alunos Sem Participacao no Forum",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GetStudentsWithoutForumParticipationResult>))]
    [Description("Identifica estudantes ativos que ainda não postaram em um fórum específico. Analisa as discussões para encontrar autores e subtrai da lista de estudantes. ATENÇÃO: esta análise pode ser lenta para fóruns com muitas discussões.")]
    public Task<CallToolResult> ListarAlunosSemParticipacaoForumAsync(
        [Description("Identificador do curso Moodle.")]
        string courseId,
        [Description("Identificador do fórum Moodle (instance ID).")]
        string forumId,
        [Description("Máximo de estudantes para analisar. Padrão: 100.")]
        int maxStudentsToAnalyze = 100,
        [Description("Máximo de discussões a escanear para identificar participantes. Padrão: 20.")]
        int maxDiscussionsToScan = 20,
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrão do usuário.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetForumParticipationCoreAsync(
            courseId, forumId, maxStudentsToAnalyze, maxDiscussionsToScan, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "list_students_without_forum_participation",
        Title = "List Students Without Forum Participation",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GetStudentsWithoutForumParticipationResult>))]
    [Description("Identifies active students who have not posted in a specific forum. Scans forum discussions to collect authors and subtracts from the student list. NOTE: may be slow for forums with many discussions.")]
    public Task<CallToolResult> ListStudentsWithoutForumParticipationAsync(
        [Description("Moodle course identifier.")]
        string courseId,
        [Description("Moodle forum identifier (instance ID).")]
        string forumId,
        [Description("Maximum students to analyze. Default: 100.")]
        int maxStudentsToAnalyze = 100,
        [Description("Maximum number of discussions to scan for participants. Default: 20.")]
        int maxDiscussionsToScan = 20,
        [Description("Moodle connection alias. When omitted, uses the user's default connection.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetForumParticipationCoreAsync(
            courseId, forumId, maxStudentsToAnalyze, maxDiscussionsToScan, moodleAlias, cancellationToken);
    }

    private async Task<CallToolResult> GetForumParticipationCoreAsync(
        string courseId,
        string forumId,
        int maxStudentsToAnalyze,
        int maxDiscussionsToScan,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
            return ToolResultHelper.Error<GetStudentsWithoutForumParticipationResult>("Informe um identificador de curso válido.");

        if (string.IsNullOrWhiteSpace(forumId))
            return ToolResultHelper.Error<GetStudentsWithoutForumParticipationResult>("Informe um identificador de fórum válido.");

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<GetStudentsWithoutForumParticipationResult>("Usuário não autenticado.");

        GetStudentsWithoutForumParticipationResult data;
        try
        {
            data = await mediator.Send(
                new GetStudentsWithoutForumParticipationQuery(courseId, forumId, maxStudentsToAnalyze, maxDiscussionsToScan),
                cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return ToolResultHelper.Error<GetStudentsWithoutForumParticipationResult>("Não foi possível analisar a participação no fórum neste momento.");
        }

        var response = new ToolResponse<GetStudentsWithoutForumParticipationResult>("ok", data, [], AuditId: null, DateTimeOffset.UtcNow);
        var narration = $"Participação no fórum {forumId} — curso {courseId}: {data.TotalStudentsAnalyzed} estudante(s) analisado(s). " +
                        $"{data.StudentsWithoutParticipation.Count} ainda não participaram.";

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = narration }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }
}
