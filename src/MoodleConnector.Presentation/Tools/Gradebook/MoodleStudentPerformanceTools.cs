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
public sealed class MoodleStudentPerformanceTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver)
{
    // ── Desempenho por atividade ──────────────────────────────────────────────

    [McpServerTool(
        Name = "get_student_activity_grades",
        Title = "Get Student Activity Grades",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<StudentGradeItemsResult>))]
    [Description("Retorna os itens avaliativos (SAs) do boletim de um estudante com indicação de quais estão abaixo do conceito mínimo. Usado pelo tutor para identificar oportunidades de recuperação paralela.")]
    public Task<CallToolResult> ConsultarDesempenhoEstudantePorAtividadeAsync(
        [Description("Identificador do curso Moodle.")]
        string courseId,
        [Description("Identificador do estudante (ID do Moodle).")]
        string studentId,
        [Description("Nota mínima esperada em porcentagem (0-100). Padrão: 60.")]
        decimal minGradePercent = 60m,
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrão do usuário.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetGradeItemsCoreAsync(courseId, studentId, minGradePercent, moodleAlias, cancellationToken);
    }

    private async Task<CallToolResult> GetGradeItemsCoreAsync(
        string courseId,
        string studentId,
        decimal minGradePercent,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
            return ToolResultHelper.Error<StudentGradeItemsResult>("Informe um identificador de curso válido.");

        if (string.IsNullOrWhiteSpace(studentId))
            return ToolResultHelper.Error<StudentGradeItemsResult>("Informe um identificador de estudante válido.");

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<StudentGradeItemsResult>("Usuário não autenticado.");

        StudentGradeItemsResult data;
        try
        {
            data = await mediator.Send(
                new GetStudentGradeItemsQuery(courseId, studentId, minGradePercent),
                cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return ToolResultHelper.Error<StudentGradeItemsResult>("Não foi possível consultar o desempenho do estudante neste momento.");
        }

        var response = new ToolResponse<StudentGradeItemsResult>("ok", data, [], AuditId: null, DateTimeOffset.UtcNow);
        var narration = $"Desempenho do estudante {studentId} no curso {courseId}: {data.Items.Count} atividade(s) avaliativa(s). " +
                        $"{data.BelowMinimumItems.Count} abaixo do mínimo de {data.MinGradePercent}%.";

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = narration }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    // ── Alunos abaixo do mínimo ───────────────────────────────────────────────

    [McpServerTool(
        Name = "list_students_below_min_grade",
        Title = "List Students Below Min Grade",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GetStudentsBelowMinGradeResult>))]
    [Description("Lista todos os estudantes ativos com pelo menos uma SA/atividade abaixo do conceito mínimo. Retorna público-alvo sugerido para mensagem de recuperação paralela.")]
    public Task<CallToolResult> ListarAlunosAbaixoMinimoAsync(
        [Description("Identificador do curso Moodle.")]
        string courseId,
        [Description("Nota mínima esperada em porcentagem (0-100). Padrão: 60.")]
        decimal minGradePercent = 60m,
        [Description("Máximo de estudantes para analisar. Padrão: 100.")]
        int maxStudentsToAnalyze = 100,
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrão do usuário.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return GetStudentsBelowMinCoreAsync(courseId, minGradePercent, maxStudentsToAnalyze, moodleAlias, cancellationToken);
    }

    private async Task<CallToolResult> GetStudentsBelowMinCoreAsync(
        string courseId,
        decimal minGradePercent,
        int maxStudentsToAnalyze,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
            return ToolResultHelper.Error<GetStudentsBelowMinGradeResult>("Informe um identificador de curso válido.");

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
            return ToolResultHelper.Error<GetStudentsBelowMinGradeResult>("Usuário não autenticado.");

        GetStudentsBelowMinGradeResult data;
        try
        {
            data = await mediator.Send(
                new GetStudentsBelowMinGradeQuery(courseId, minGradePercent, maxStudentsToAnalyze),
                cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return ToolResultHelper.Error<GetStudentsBelowMinGradeResult>("Não foi possível listar os alunos abaixo do mínimo neste momento.");
        }

        var response = new ToolResponse<GetStudentsBelowMinGradeResult>("ok", data, [], AuditId: null, DateTimeOffset.UtcNow);
        var narration = $"Análise do curso {courseId}: {data.TotalStudentsAnalyzed} estudante(s) analisado(s). " +
                        $"{data.Students.Count} com pelo menos uma SA abaixo de {data.MinGradePercent}%.";

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = narration }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }
}
