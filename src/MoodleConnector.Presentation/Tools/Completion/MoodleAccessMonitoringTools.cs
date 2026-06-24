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
public sealed class MoodleAccessMonitoringTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver)
{
    [McpServerTool(
        Name = "listar_alunos_sem_acesso",
        Title = "Listar Alunos Sem Acesso Recente",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GetStudentsWithoutRecentAccessResult>))]
    [Description("Lista estudantes ativos que não acessaram o AVA nos últimos N dias. Retorna público-alvo sugerido para mensagem de cobrança de acesso.")]
    public Task<CallToolResult> ListarAlunosSemAcessoAsync(
        [Description("Identificador do curso Moodle.")]
        string courseId,
        [Description("Número de dias sem acesso para considerar inativo. Padrão: 7.")]
        int daysWithoutAccess = 7,
        [Description("Máximo de estudantes para analisar. Padrão: 100.")]
        int maxStudentsToAnalyze = 100,
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrão do usuário.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetAccessCoreAsync(courseId, daysWithoutAccess, maxStudentsToAnalyze, moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "list_students_without_recent_access",
        Title = "List Students Without Recent Access",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GetStudentsWithoutRecentAccessResult>))]
    [Description("Lists active students who have not accessed the course in the last N days. Returns a suggested recipient list for an access reminder message.")]
    public Task<CallToolResult> ListStudentsWithoutRecentAccessAsync(
        [Description("Moodle course identifier.")]
        string courseId,
        [Description("Number of days without access to be considered inactive. Default: 7.")]
        int daysWithoutAccess = 7,
        [Description("Maximum students to analyze. Default: 100.")]
        int maxStudentsToAnalyze = 100,
        [Description("Moodle connection alias. When omitted, uses the user's default connection.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetAccessCoreAsync(courseId, daysWithoutAccess, maxStudentsToAnalyze, moodleAlias, cancellationToken);
    }

    private async Task<CallToolResult> GetAccessCoreAsync(
        string courseId,
        int daysWithoutAccess,
        int maxStudentsToAnalyze,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
            return ToolResultHelper.Error<GetStudentsWithoutRecentAccessResult>("Informe um identificador de curso válido.");

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<GetStudentsWithoutRecentAccessResult>("Usuário não autenticado.");

        GetStudentsWithoutRecentAccessResult data;
        try
        {
            data = await mediator.Send(
                new GetStudentsWithoutRecentAccessQuery(courseId, daysWithoutAccess, maxStudentsToAnalyze),
                cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return ToolResultHelper.Error<GetStudentsWithoutRecentAccessResult>("Não foi possível listar os alunos sem acesso recente.");
        }

        var response = new ToolResponse<GetStudentsWithoutRecentAccessResult>("ok", data, [], AuditId: null, DateTimeOffset.UtcNow);
        var narration = $"Monitoramento de acesso — curso {courseId}: {data.TotalStudentsAnalyzed} estudante(s) analisado(s). " +
                        $"{data.Students.Count} sem acesso há mais de {data.DaysThreshold} dia(s).";

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = narration }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }
}
