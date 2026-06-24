using MediatR;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Application.Gradebook.Queries;

/// <summary>
/// Retorna os itens avaliativos (SAs/atividades) do boletim de um estudante,
/// com indicação de quais estão abaixo do mínimo esperado.
/// </summary>
public sealed record StudentGradeItem(
    string ItemId,
    string ItemName,
    string ItemType,
    string ItemModule,
    decimal? GradeRaw,
    decimal? GradeMax,
    decimal? PercentageFormatted,
    bool BelowMinimum,
    string? Feedback);

public sealed record StudentGradeItemsResult(
    string CourseId,
    string StudentId,
    decimal MinGradePercent,
    IReadOnlyList<StudentGradeItem> Items,
    IReadOnlyList<StudentGradeItem> BelowMinimumItems,
    string? Warning);

public sealed record GetStudentGradeItemsQuery(
    string CourseId,
    string StudentId,
    decimal MinGradePercent = 60m) : IRequest<StudentGradeItemsResult>;

public sealed class GetStudentGradeItemsQueryHandler(IMoodleGradebookGateway gradebookGateway)
    : IRequestHandler<GetStudentGradeItemsQuery, StudentGradeItemsResult>
{
    public async Task<StudentGradeItemsResult> Handle(
        GetStudentGradeItemsQuery request,
        CancellationToken cancellationToken)
    {
        var gradebook = await gradebookGateway.GetStudentGradebookAsync(
            request.CourseId,
            request.StudentId,
            cancellationToken);

        // Filter to only assignment-type items (SAs), excluding the overall course item
        var activityItems = gradebook.Items
            .Where(i => i.ItemType != "course" && i.ItemType != "category")
            .ToList();

        var gradeItems = activityItems.Select(i =>
        {
            var belowMinimum = i.PercentageFormatted.HasValue
                && i.PercentageFormatted.Value < request.MinGradePercent;

            return new StudentGradeItem(
                ItemId: i.Id,
                ItemName: i.ItemName,
                ItemType: i.ItemType,
                ItemModule: i.ItemModule,
                GradeRaw: i.GradeRaw,
                GradeMax: i.GradeMax,
                PercentageFormatted: i.PercentageFormatted,
                BelowMinimum: belowMinimum,
                Feedback: i.Feedback);
        }).ToList();

        var belowMinimum = gradeItems.Where(i => i.BelowMinimum).ToList();

        string? warning = null;
        if (activityItems.Count == 0)
        {
            warning = "Nenhum item avaliativo encontrado. O gradebook pode estar desabilitado ou o estudante ainda não possui atividades avaliadas.";
        }

        return new StudentGradeItemsResult(
            CourseId: request.CourseId,
            StudentId: request.StudentId,
            MinGradePercent: request.MinGradePercent,
            Items: gradeItems,
            BelowMinimumItems: belowMinimum,
            Warning: warning);
    }
}
