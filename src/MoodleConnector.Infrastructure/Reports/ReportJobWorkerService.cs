using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Reports.Queries;
using MoodleConnector.Domain;

namespace MoodleConnector.Infrastructure.Reports;

internal sealed class ReportJobWorkerService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReportJobWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ReportJobWorkerService iniciado.");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<ReportJobProcessor>();
                await processor.ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falha no ciclo de processamento de relatórios.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));

        logger.LogInformation("ReportJobWorkerService encerrado.");
    }
}

internal sealed class ReportJobProcessor(
    ConnectorDbContext dbContext,
    IMoodleCoursesGateway coursesGateway,
    IMediator mediator,
    IConnectorExecutionContext executionContext,
    IMoodleConnectionSelection connectionSelection,
    ILogger<ReportJobProcessor> logger)
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var job = await dbContext.ReportJobs
            .Where(item => item.Status == "queued")
            .OrderBy(item => item.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        job.Status = "running";
        job.StartedAt = now;
        job.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            executionContext.Enter(job.ClientId, job.OwnerId.ToString(), null);
            connectionSelection.Alias = job.ConnectionAlias;

            var courses = await ResolveCoursesAsync(job, cancellationToken);
            if (courses.Count == 0)
            {
                throw new InvalidOperationException("Nenhum curso foi encontrado para gerar o relatório.");
            }

            job.CourseNamesJson = JsonSerializer.Serialize(courses.Select(course => new ReportCourseMetadata(
                course.DisplayName ?? course.FullName,
                course.CategoryName)).ToArray());
            job.TotalCourses = courses.Count;
            job.ProgressPercent = 0;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            var unitsByTurma = new Dictionary<string, List<ExcelGradeUnit>>(StringComparer.OrdinalIgnoreCase);
            foreach (var course in courses)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var unit = await GenerateUnitAsync(course, cancellationToken);
                var turmaName = string.IsNullOrWhiteSpace(course.CategoryName) ? "Sem turma" : course.CategoryName!;
                if (!unitsByTurma.TryGetValue(turmaName, out var units))
                {
                    units = [];
                    unitsByTurma[turmaName] = units;
                }

                units.Add(unit);
                job.ProcessedCourses++;
                job.ProgressPercent = (int)Math.Round(job.ProcessedCourses * 100d / courses.Count, MidpointRounding.AwayFromZero);
                job.UpdatedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var workbooks = unitsByTurma
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item =>
                {
                    var fileName = $"relatorio_notas_{Slugify(item.Key)}_{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.xlsx";
                    return (FileName: fileName, Content: ExcelGradeReportBuilder.BuildWorkbook(item.Key, job.RequestedAt, item.Value));
                })
                .ToArray();

            var output = workbooks.Length == 1
                ? (ContentType: ExcelContentType, FileName: workbooks[0].FileName, Content: workbooks[0].Content)
                : (ContentType: "application/zip", FileName: $"relatorios_notas_{Slugify(job.ConnectionAlias)}_{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip", Content: ExcelGradeReportBuilder.BuildZip(workbooks));

            var usedBytes = await ReportStorageCalculator.GetUsedBytesAsync(dbContext, job.OwnerId, cancellationToken, job.Id);
            if (usedBytes + output.Content.LongLength > ReportStorageCalculator.LimitBytes)
            {
                throw new InvalidOperationException($"O limite de {ReportStorageCalculator.FormatBytes(ReportStorageCalculator.LimitBytes)} por usuário seria ultrapassado. Exclua relatórios antigos antes de gerar outro arquivo.");
            }

            job.ContentText = null;
            job.ContentBase64 = Convert.ToBase64String(output.Content);
            job.ContentType = output.ContentType;
            job.FileName = output.FileName;
            job.FileSizeBytes = output.Content.LongLength;
            job.Status = "completed";
            job.ProgressPercent = 100;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.UpdatedAt = job.CompletedAt.Value;
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Relatório {ReportJobId} concluído com {CourseCount} curso(s) e {WorkbookCount} arquivo(s).", job.Id, courses.Count, workbooks.Length);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            job.Status = "failed";
            job.ErrorMessage = exception.Message.Length > 3900 ? exception.Message[..3900] : exception.Message;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.UpdatedAt = job.CompletedAt.Value;
            await dbContext.SaveChangesAsync(CancellationToken.None);
            logger.LogError(exception, "Relatório {ReportJobId} falhou.", job.Id);
            return true;
        }
        finally
        {
            connectionSelection.Alias = null;
            executionContext.Clear();
        }
    }

    private async Task<IReadOnlyList<CourseSummary>> ResolveCoursesAsync(
        ReportJobEntity job,
        CancellationToken cancellationToken)
    {
        var userExternalId = job.OwnerId.ToString();
        if (job.ScopeType == "course")
        {
            var course = await coursesGateway.GetMyCourseAsync(userExternalId, job.CourseId!, cancellationToken);
            return course is null ? throw new InvalidOperationException("O curso selecionado não foi encontrado na conexão Moodle.") : [course];
        }

        if (job.ScopeType == "courses")
        {
            var courseIds = string.IsNullOrWhiteSpace(job.CourseIdsJson)
                ? []
                : JsonSerializer.Deserialize<string[]>(job.CourseIdsJson) ?? [];
            var courses = new List<CourseSummary>(courseIds.Length);
            foreach (var courseId in courseIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var course = await coursesGateway.GetMyCourseAsync(userExternalId, courseId, cancellationToken);
                if (course is not null)
                {
                    courses.Add(course);
                }
            }

            return courses;
        }

        if (job.ScopeType != "category" || string.IsNullOrWhiteSpace(job.CategoryPath))
        {
            throw new InvalidOperationException("O escopo do relatório é inválido.");
        }

        var categoryCourses = new List<CourseSummary>();
        var page = 1;
        const int pageSize = 100;
        while (true)
        {
            var result = await coursesGateway.GetMyCoursesByCategoryAsync(
                userExternalId,
                job.CategoryPath,
                pageSize,
                page,
                cancellationToken);
            categoryCourses.AddRange(result.Items);
            if (!result.HasNextPage || result.Items.Count == 0)
            {
                break;
            }

            page++;
        }

        return categoryCourses;
    }

    private async Task<ExcelGradeUnit> GenerateUnitAsync(CourseSummary course, CancellationToken cancellationToken)
    {
        try
        {
            var report = await mediator.Send(new GenerateCourseGradesReportQuery(course.CourseId), cancellationToken);
            return new ExcelGradeUnit(course.CourseId, course.DisplayName ?? course.FullName, report.Students, report.Warning);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ExcelGradeUnit(course.CourseId, course.DisplayName ?? course.FullName, [], exception.Message);
        }
    }

    private sealed record ReportCourseMetadata(string Name, string? CategoryName);

    private static string Slugify(string value)
    {
        var slug = new string(value
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray());
        slug = string.Join('_', slug.Split('_', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(slug) ? "turma" : slug[..Math.Min(slug.Length, 60)];
    }
}
