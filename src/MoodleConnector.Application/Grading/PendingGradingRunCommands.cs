using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Courses;
using MoodleConnector.Application.Submissions;
using MoodleConnector.Domain;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Grading;

/// <summary>
/// Inicia a preparacao de correcoes pendentes em todos os cursos acessiveis.
/// Cada curso recebe um sublote proprio para preservar o escopo de processamento
/// e permitir uma exportacao CSV independente por lote.
/// </summary>
public sealed record StartPendingGradingRunCommand(
    string UserExternalId,
    int MaxCourses,
    int MaxItemsPerBatch,
    bool IncludeRubric = true,
    bool IncludeSubmissionFiles = true,
    bool IncludeCourseMaterials = false,
    string? TeacherInstructions = null,
    string Priority = "normal",
    bool UseSubmissionSnapshots = false,
    Guid? SnapshotOwnerId = null,
    string? SnapshotClientId = null,
    string? SnapshotConnectionAlias = null,
    string? CourseId = null) : IRequest<StartPendingGradingRunResult>;

public sealed record StartPendingGradingRunResult(
    [property: JsonPropertyName("coursesDiscovered")] int CoursesDiscovered,
    [property: JsonPropertyName("coursesScanned")] int CoursesScanned,
    [property: JsonPropertyName("coursesWithPendingSubmissions")] int CoursesWithPendingSubmissions,
    [property: JsonPropertyName("totalItems")] int TotalItems,
    [property: JsonPropertyName("batches")] IReadOnlyList<PendingGradingRunBatch> Batches,
    [property: JsonPropertyName("courses")] IReadOnlyList<PendingGradingRunCourse> Courses,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("nextStep")] string NextStep);

public sealed record PendingGradingRunBatch(
    [property: JsonPropertyName("batchJobId")] Guid BatchJobId,
    [property: JsonPropertyName("courseId")] string CourseId,
    [property: JsonPropertyName("courseName")] string CourseName,
    [property: JsonPropertyName("assignmentIds")] IReadOnlyList<string> AssignmentIds,
    [property: JsonPropertyName("totalItems")] int TotalItems,
    [property: JsonPropertyName("blockedItems")] int BlockedItems);

public sealed record PendingGradingRunCourse(
    [property: JsonPropertyName("courseId")] string CourseId,
    [property: JsonPropertyName("courseName")] string CourseName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("batchJobId")] Guid? BatchJobId,
    [property: JsonPropertyName("message")] string? Message);

public sealed class StartPendingGradingRunCommandHandler(
    IMediator mediator,
    IMoodleCourseContentsGateway contentsGateway,
    IMoodleSnapshotStore? snapshotStore = null,
    IMoodleSnapshotSyncQueue? snapshotSyncQueue = null)
    : IRequestHandler<StartPendingGradingRunCommand, StartPendingGradingRunResult>
{
    private const int CoursePageSize = 100;

    public async Task<StartPendingGradingRunResult> Handle(
        StartPendingGradingRunCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserExternalId))
        {
            throw new ArgumentException("O usuario Moodle e obrigatorio.", nameof(request.UserExternalId));
        }

        if (request.MaxCourses < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.MaxCourses), "O limite de cursos nao pode ser negativo.");
        }

        var maxCourses = request.MaxCourses == 0 ? int.MaxValue : Math.Clamp(request.MaxCourses, 1, 1000);
        var maxItemsPerBatch = Math.Clamp(request.MaxItemsPerBatch, 1, 400);
        var useSnapshots = request.UseSubmissionSnapshots &&
            request.SnapshotOwnerId is not null &&
            !string.IsNullOrWhiteSpace(request.SnapshotClientId) &&
            !string.IsNullOrWhiteSpace(request.SnapshotConnectionAlias) &&
            snapshotStore is not null;
        var requestedCourseId = string.IsNullOrWhiteSpace(request.CourseId)
            ? null
            : request.CourseId.Trim();
        IReadOnlyList<CourseSummary> courses;
        if (requestedCourseId is not null)
        {
            var course = await mediator.Send(
                new GetCourseQuery(request.UserExternalId, requestedCourseId),
                cancellationToken);
            courses = course is null ? [] : [course];
        }
        else
        {
            courses = useSnapshots
                ? []
                : await LoadCoursesAsync(request.UserExternalId, maxCourses, cancellationToken);
        }
        var batches = new List<PendingGradingRunBatch>();
        var courseResults = new List<PendingGradingRunCourse>();
        var warnings = new List<string>();

        if (requestedCourseId is not null && courses.Count == 0)
        {
            warnings.Add($"Curso {requestedCourseId} nao foi encontrado ou nao esta acessivel para o usuario atual.");
        }
        else if (useSnapshots && requestedCourseId is null)
        {
            courses = await LoadCoursesFromSnapshotAsync(request, maxCourses, warnings, cancellationToken);
        }

        foreach (var course in courses)
        {
            var courseName = ResolveCourseName(course);
            if (useSnapshots)
            {
                courseResults.Add(await ProcessSnapshotCourseAsync(
                    request,
                    course,
                    courseName,
                    maxItemsPerBatch,
                    batches,
                    warnings,
                    cancellationToken));
                continue;
            }

            CourseContentsSummary contents;
            try
            {
                contents = await contentsGateway.GetCourseContentsAsync(
                    request.UserExternalId,
                    course.CourseId,
                    moduleTypes: ["assign"],
                    includeHidden: false,
                    onlyWithFiles: false,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var message = $"Nao foi possivel ler as atividades do curso: {ex.Message}";
                courseResults.Add(new PendingGradingRunCourse(course.CourseId, courseName, "course_read_failed", null, message));
                warnings.Add($"Curso {course.CourseId}: {message}");
                continue;
            }

            var assignmentIds = contents.Sections
                .SelectMany(section => section.Modules)
                .Where(module =>
                    string.Equals(module.ModuleType, "assign", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(module.InstanceId))
                .Select(module => module.InstanceId!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (assignmentIds.Length == 0)
            {
                courseResults.Add(new PendingGradingRunCourse(
                    course.CourseId,
                    courseName,
                    "no_assignments",
                    null,
                    "Nenhuma atividade avaliativa do tipo assign foi encontrada."));
                continue;
            }

            var batchesBeforeCourse = batches.Count;
            var courseMessages = new List<string>();
            foreach (var assignmentId in assignmentIds)
            {
                IReadOnlyList<AssignmentSubmissionSummary> submissions;
                try
                {
                    submissions = await LoadPendingSubmissionsAsync(
                        request.UserExternalId,
                        course.CourseId,
                        assignmentId,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var message = $"Nao foi possivel listar as entregas da tarefa {assignmentId}: {ex.Message}";
                    courseMessages.Add(message);
                    warnings.Add($"Curso {course.CourseId}: {message}");
                    continue;
                }

                foreach (var submissionChunk in submissions.Chunk(maxItemsPerBatch))
                {
                    CreateAssistedGradingBatchResult batch;
                    try
                    {
                        batch = await mediator.Send(
                            new CreateAssistedGradingBatchCommand(
                                request.UserExternalId,
                                course.CourseId,
                                AssignmentIds: [assignmentId],
                                SubmissionIds: [],
                                MaxItems: maxItemsPerBatch,
                                OnlyAwaitingGrading: true,
                                IncludeRubric: request.IncludeRubric,
                                IncludeSubmissionFiles: request.IncludeSubmissionFiles,
                                IncludeCourseMaterials: request.IncludeCourseMaterials,
                                TeacherInstructions: request.TeacherInstructions,
                                Priority: request.Priority,
                                CourseDisplayName: courseName,
                                PrefetchedSubmissions: submissionChunk),
                            cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        var message = $"Nao foi possivel preparar o sublote da tarefa {assignmentId}: {ex.Message}";
                        courseMessages.Add(message);
                        warnings.Add($"Curso {course.CourseId}: {message}");
                        continue;
                    }

                    if (batch.BatchJobId == Guid.Empty || batch.AcceptedItems == 0)
                    {
                        continue;
                    }

                    batches.Add(new PendingGradingRunBatch(
                        batch.BatchJobId,
                        batch.CourseId,
                        courseName,
                        batch.AssignmentIds,
                        batch.AcceptedItems,
                        batch.BlockedItems));
                    warnings.AddRange(batch.Warnings.Select(warning => $"Curso {course.CourseId}: {warning}"));
                }
            }

            var courseBatchCount = batches.Count - batchesBeforeCourse;
            courseResults.Add(new PendingGradingRunCourse(
                course.CourseId,
                courseName,
                courseBatchCount > 0
                    ? "batch_created"
                    : courseMessages.Count > 0
                        ? "partial_failure"
                        : "no_pending_submissions",
                courseBatchCount == 1 ? batches[^1].BatchJobId : null,
                courseBatchCount > 0
                    ? $"{courseBatchCount} sublote(s) criado(s). {string.Join(" ", courseMessages)}".Trim()
                    : courseMessages.Count > 0
                        ? string.Join(" ", courseMessages)
                        : "Nenhuma entrega aguardando correcao foi encontrada."));
        }

        return new StartPendingGradingRunResult(
            CoursesDiscovered: courses.Count,
            CoursesScanned: courseResults.Count,
            CoursesWithPendingSubmissions: courseResults.Count(course => course.Status == "batch_created"),
            TotalItems: batches.Sum(batch => batch.TotalItems),
            Batches: batches,
            Courses: courseResults,
            Warnings: warnings,
            NextStep: batches.Count == 0
                ? "Nao ha entregas pendentes elegiveis para iniciar a correcao. Consulte os cursos com falha para ajuste manual."
                : "Para cada batchJobId, prepare o pacote de IA, gere nota e feedback, salve os rascunhos e use export_grading_corrections_csv para receber o CSV. Nao chame ferramentas de revisao, confirmacao ou envio ao Moodle.");
    }

    private async Task<IReadOnlyList<CourseSummary>> LoadCoursesFromSnapshotAsync(
        StartPendingGradingRunCommand request,
        int maxCourses,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var snapshot = await snapshotStore!.GetCoursesAsync(
            request.SnapshotOwnerId!.Value,
            request.SnapshotConnectionAlias!,
            cancellationToken);
        if (snapshot is null)
        {
            var queued = await QueueSnapshotAsync(request, MoodleSnapshotDatasets.Courses, null, force: true, cancellationToken);
            warnings.Add(
                queued
                    ? "O snapshot de cursos ainda não está disponível; a atualização foi agendada e nenhuma consulta live foi feita."
                    : "O snapshot de cursos ainda não está disponível e não foi possível agendar a atualização.");
            return [];
        }

        if (snapshot.IsStale || !snapshot.IsComplete)
        {
            var queued = await QueueSnapshotAsync(request, MoodleSnapshotDatasets.Courses, null, force: true, cancellationToken);
            warnings.Add(
                queued
                    ? "O snapshot de cursos está desatualizado ou incompleto; a atualização foi agendada."
                    : "O snapshot de cursos está desatualizado ou incompleto e não foi possível agendar a atualização.");
        }

        return snapshot.Data
            .Take(maxCourses)
            .ToArray();
    }

    private async Task<PendingGradingRunCourse> ProcessSnapshotCourseAsync(
        StartPendingGradingRunCommand request,
        CourseSummary course,
        string courseName,
        int maxItemsPerBatch,
        List<PendingGradingRunBatch> batches,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var snapshot = await snapshotStore!.GetAsync<CourseAssignmentSubmissionsSnapshot>(
            request.SnapshotOwnerId!.Value,
            request.SnapshotConnectionAlias!,
            MoodleSnapshotDatasets.Submissions,
            course.CourseId,
            cancellationToken);
        if (snapshot is null)
        {
            var queued = await QueueSnapshotAsync(request, MoodleSnapshotDatasets.Submissions, course.CourseId, force: true, cancellationToken);
            var message = queued
                ? "O snapshot de entregas ainda não está disponível; a atualização foi agendada."
                : "O snapshot de entregas ainda não está disponível e não foi possível agendar a atualização.";
            warnings.Add($"Curso {course.CourseId}: {message}");
            return new PendingGradingRunCourse(course.CourseId, courseName, "snapshot_unavailable", null, message);
        }

        if (snapshot.IsStale || !snapshot.IsComplete)
        {
            var queued = await QueueSnapshotAsync(request, MoodleSnapshotDatasets.Submissions, course.CourseId, force: true, cancellationToken);
            warnings.Add($"Curso {course.CourseId}: o snapshot de entregas está desatualizado ou incompleto; a leitura usou apenas os dados disponíveis e agendou atualização={queued}.");
        }

        if (snapshot.Data.Assignments.Count == 0)
        {
            return new PendingGradingRunCourse(
                course.CourseId,
                courseName,
                "no_assignments",
                null,
                "Nenhuma atividade avaliativa do tipo assign está disponível no snapshot.");
        }

        var batchesBeforeCourse = batches.Count;
        var courseMessages = new List<string>();
        foreach (var assignment in snapshot.Data.Assignments)
        {
            if (!assignment.IsComplete ||
                assignment.Coverage is not null && !assignment.Coverage.NeedsGradingComplete)
            {
                var message = assignment.ErrorMessage ??
                    "Os dados da tarefa não possuem cobertura completa de participantes, submissões, configuração e notas.";
                courseMessages.Add($"Tarefa {assignment.AssignmentId}: {message}");
                warnings.Add($"Curso {course.CourseId}: tarefa {assignment.AssignmentId}: {message}");
                continue;
            }

            var submissions = assignment.Submissions
                .Where(submission => submission.NeedsGrading)
                .ToArray();
            foreach (var submissionChunk in submissions.Chunk(maxItemsPerBatch))
            {
                try
                {
                    var batch = await mediator.Send(
                        new CreateAssistedGradingBatchCommand(
                            request.UserExternalId,
                            course.CourseId,
                            AssignmentIds: [assignment.AssignmentId],
                            SubmissionIds: [],
                            MaxItems: maxItemsPerBatch,
                            OnlyAwaitingGrading: true,
                            IncludeRubric: request.IncludeRubric,
                            IncludeSubmissionFiles: request.IncludeSubmissionFiles,
                            IncludeCourseMaterials: request.IncludeCourseMaterials,
                            TeacherInstructions: request.TeacherInstructions,
                            Priority: request.Priority,
                            CourseDisplayName: courseName,
                            PrefetchedSubmissions: submissionChunk),
                        cancellationToken);

                    if (batch.BatchJobId == Guid.Empty || batch.AcceptedItems == 0)
                    {
                        continue;
                    }

                    batches.Add(new PendingGradingRunBatch(
                        batch.BatchJobId,
                        batch.CourseId,
                        courseName,
                        batch.AssignmentIds,
                        batch.AcceptedItems,
                        batch.BlockedItems));
                    warnings.AddRange(batch.Warnings.Select(warning => $"Curso {course.CourseId}: {warning}"));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var message = $"Nao foi possivel preparar o sublote da tarefa {assignment.AssignmentId}: {ex.Message}";
                    courseMessages.Add(message);
                    warnings.Add($"Curso {course.CourseId}: {message}");
                }
            }
        }

        var courseBatchCount = batches.Count - batchesBeforeCourse;
        return new PendingGradingRunCourse(
            course.CourseId,
            courseName,
            courseBatchCount > 0
                ? "batch_created"
                : courseMessages.Count > 0
                    ? "partial_failure"
                    : "no_pending_submissions",
            courseBatchCount == 1 ? batches[^1].BatchJobId : null,
            courseBatchCount > 0
                ? $"{courseBatchCount} sublote(s) criado(s). {string.Join(" ", courseMessages)}".Trim()
                : courseMessages.Count > 0
                    ? string.Join(" ", courseMessages)
                    : "Nenhuma entrega aguardando correcao foi encontrada.");
    }

    private async Task<bool> QueueSnapshotAsync(
        StartPendingGradingRunCommand request,
        string dataset,
        string? courseId,
        bool force,
        CancellationToken cancellationToken)
    {
        if (snapshotSyncQueue is null)
        {
            return false;
        }

        return await snapshotSyncQueue.EnqueueAsync(
            new MoodleSnapshotSyncRequest(
                request.SnapshotOwnerId!.Value,
                request.SnapshotClientId!,
                request.SnapshotConnectionAlias!,
                request.UserExternalId,
                force,
                dataset,
                courseId,
                Priority: 5),
            cancellationToken);
    }

    private async Task<IReadOnlyList<AssignmentSubmissionSummary>> LoadPendingSubmissionsAsync(
        string userExternalId,
        string courseId,
        string assignmentId,
        CancellationToken cancellationToken)
    {
        var submissions = new List<AssignmentSubmissionSummary>();
        var page = 1;
        while (true)
        {
            var response = await mediator.Send(
                new ListAssignmentSubmissionsQuery(
                    userExternalId,
                    courseId,
                    assignmentId,
                    AssignmentSubmissionFilter.NeedsGrading,
                    page,
                    PageSize: 100,
                    Since: null,
                    Before: null,
                    IncludeLate: true,
                    IncludeUngraded: true),
                cancellationToken)
                ?? throw new InvalidOperationException("Tarefa nao encontrada para o usuario atual.");

            submissions.AddRange(response.Submissions.Where(submission => submission.NeedsGrading));
            if (!response.HasMore)
            {
                break;
            }

            page++;
        }

        return submissions;
    }

    private async Task<IReadOnlyList<CourseSummary>> LoadCoursesAsync(
        string userExternalId,
        int maxCourses,
        CancellationToken cancellationToken)
    {
        var courses = new List<CourseSummary>();
        var seenCourseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var page = 1;

        while (courses.Count < maxCourses)
        {
            var result = await mediator.Send(
                new ListMyCoursesQuery(userExternalId, Math.Min(CoursePageSize, maxCourses - courses.Count), page),
                cancellationToken);
            foreach (var course in result.Items)
            {
                if (seenCourseIds.Add(course.CourseId))
                {
                    courses.Add(course);
                    if (courses.Count >= maxCourses)
                    {
                        break;
                    }
                }
            }

            if (!result.HasNextPage || result.Items.Count == 0)
            {
                break;
            }

            page++;
        }

        return courses;
    }

    private static string ResolveCourseName(CourseSummary course) =>
        course.DisplayName ?? course.FullName ?? course.ShortName ?? course.CourseId;
}

public sealed record GetPendingGradingRunReportQuery(
    IReadOnlyList<Guid> BatchJobIds) : IRequest<PendingGradingRunReportResult>;

public sealed record PendingGradingRunReportResult(
    [property: JsonPropertyName("generatedAt")] DateTimeOffset GeneratedAt,
    [property: JsonPropertyName("batchJobIds")] IReadOnlyList<Guid> BatchJobIds,
    [property: JsonPropertyName("totalItems")] int TotalItems,
    [property: JsonPropertyName("correctedCount")] int CorrectedCount,
    [property: JsonPropertyName("notCorrectedCount")] int NotCorrectedCount,
    [property: JsonPropertyName("batches")] IReadOnlyList<PendingGradingRunBatchReport> Batches,
    [property: JsonPropertyName("correctedItems")] IReadOnlyList<PendingGradingRunItemOutcome> CorrectedItems,
    [property: JsonPropertyName("notCorrectedItems")] IReadOnlyList<PendingGradingRunItemOutcome> NotCorrectedItems,
    [property: JsonPropertyName("reportMarkdown")] string ReportMarkdown);

public sealed record PendingGradingRunBatchReport(
    [property: JsonPropertyName("batchJobId")] Guid BatchJobId,
    [property: JsonPropertyName("courseId")] string CourseId,
    [property: JsonPropertyName("totalItems")] int TotalItems,
    [property: JsonPropertyName("correctedCount")] int CorrectedCount,
    [property: JsonPropertyName("notCorrectedCount")] int NotCorrectedCount);

public sealed record PendingGradingRunItemOutcome(
    [property: JsonPropertyName("batchJobId")] Guid BatchJobId,
    [property: JsonPropertyName("courseId")] string CourseId,
    [property: JsonPropertyName("gradingItemId")] Guid GradingItemId,
    [property: JsonPropertyName("assignmentId")] string AssignmentId,
    [property: JsonPropertyName("submissionId")] string? SubmissionId,
    [property: JsonPropertyName("studentId")] string StudentId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("commitStatus")] string CommitStatus,
    [property: JsonPropertyName("grade")] decimal? Grade,
    [property: JsonPropertyName("reason")] string? Reason);

public sealed class GetPendingGradingRunReportQueryHandler(
    IGradingReviewRepository repository,
    ICurrentUserContext currentUser)
    : IRequestHandler<GetPendingGradingRunReportQuery, PendingGradingRunReportResult>
{
    public async Task<PendingGradingRunReportResult> Handle(
        GetPendingGradingRunReportQuery request,
        CancellationToken cancellationToken)
    {
        var batchIds = request.BatchJobIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (batchIds.Length == 0)
        {
            throw new ArgumentException("Informe pelo menos um lote de correcao valido.", nameof(request.BatchJobIds));
        }

        var corrected = new List<PendingGradingRunItemOutcome>();
        var notCorrected = new List<PendingGradingRunItemOutcome>();
        var batchReports = new List<PendingGradingRunBatchReport>();

        foreach (var batchId in batchIds)
        {
            var batch = await repository.GetBatchAsync(batchId, cancellationToken)
                ?? throw new InvalidOperationException($"Lote de correcao {batchId} nao encontrado.");
            GradingAccessControl.EnsureCanAccessBatch(batch, currentUser);
            var items = await GradingItemProcessor.LoadAllBatchItemsAsync(repository, batch.Id, cancellationToken);
            var batchCorrected = 0;
            var batchNotCorrected = 0;

            foreach (var item in items)
            {
                var outcome = ToOutcome(batch, item);
                if (item.Status == GradingItemStatus.Committed || item.CommitStatus == GradingCommitStatus.Succeeded)
                {
                    corrected.Add(outcome with { Reason = null, Grade = item.FinalGrade ?? item.SuggestedGrade });
                    batchCorrected++;
                }
                else
                {
                    notCorrected.Add(outcome);
                    batchNotCorrected++;
                }
            }

            batchReports.Add(new PendingGradingRunBatchReport(
                batch.Id,
                batch.CourseId.ToString(CultureInfo.InvariantCulture),
                items.Count,
                batchCorrected,
                batchNotCorrected));
        }

        var report = new PendingGradingRunReportResult(
            DateTimeOffset.UtcNow,
            batchIds,
            corrected.Count + notCorrected.Count,
            corrected.Count,
            notCorrected.Count,
            batchReports,
            corrected.OrderBy(item => item.CourseId, StringComparer.Ordinal)
                .ThenBy(item => item.AssignmentId, StringComparer.Ordinal)
                .ThenBy(item => item.StudentId, StringComparer.Ordinal)
                .ToArray(),
            notCorrected.OrderBy(item => item.CourseId, StringComparer.Ordinal)
                .ThenBy(item => item.AssignmentId, StringComparer.Ordinal)
                .ThenBy(item => item.StudentId, StringComparer.Ordinal)
                .ToArray(),
            ReportMarkdown: string.Empty);

        return report with { ReportMarkdown = BuildReportMarkdown(report) };
    }

    private static PendingGradingRunItemOutcome ToOutcome(AssistedGradingBatch batch, AssistedGradingItem item)
    {
        var reason = item.CommitStatus == GradingCommitStatus.ExecutionUnknown
            ? "Resultado da escrita no Moodle desconhecido; reconcilie a ação antes de tentar novamente."
            : item.CommitStatus == GradingCommitStatus.Failed
            ? "Falha ao lancar no Moodle: " + Describe(item.CommitError)
            : item.Status == GradingItemStatus.Blocked
                ? "Bloqueado: " + Describe(item.DraftFeedback)
                : item.Status == GradingItemStatus.Failed
                    ? "Falha no processamento: " + Describe(item.CommitError ?? item.DraftFeedback)
                    : item.Status == GradingItemStatus.AwaitingAiAnalysis
                        ? "Aguardando analise pela IA."
                        : item.ReviewStatus != GradingReviewStatus.Reviewed
                            ? "Aguardando revisao humana do rascunho."
                            : item.CommitStatus == GradingCommitStatus.Pending
                                ? "Aguardando previa e confirmacao para lancamento no Moodle."
                                : "Aguardando processamento da correcao.";

        return new PendingGradingRunItemOutcome(
            batch.Id,
            batch.CourseId.ToString(CultureInfo.InvariantCulture),
            item.Id,
            item.AssignmentId.ToString(CultureInfo.InvariantCulture),
            item.SubmissionId?.ToString(CultureInfo.InvariantCulture),
            item.MoodleUserId.ToString(CultureInfo.InvariantCulture),
            item.Status.ToString(),
            item.CommitStatus.ToString(),
            item.FinalGrade ?? item.SuggestedGrade,
            reason);
    }

    private static string BuildReportMarkdown(PendingGradingRunReportResult report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Relatorio final de correcoes pendentes");
        builder.AppendLine();
        builder.AppendLine($"- Lotes processados: {report.BatchJobIds.Count}");
        builder.AppendLine($"- Total de entregas: {report.TotalItems}");
        builder.AppendLine($"- Corrigidas e lancadas no Moodle: {report.CorrectedCount}");
        builder.AppendLine($"- Nao corrigidas: {report.NotCorrectedCount}");
        builder.AppendLine();
        builder.AppendLine("## Corrigidas e lancadas no Moodle");
        builder.AppendLine();
        if (report.CorrectedItems.Count == 0)
        {
            builder.AppendLine("- Nenhuma entrega foi lancada no Moodle ainda.");
        }
        else
        {
            foreach (var item in report.CorrectedItems)
            {
                builder.AppendLine($"- Curso `{item.CourseId}`, tarefa `{item.AssignmentId}`, estudante `{item.StudentId}`, item `{item.GradingItemId}`, nota `{FormatGrade(item.Grade)}`.");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Nao corrigidas para ajuste manual");
        builder.AppendLine();
        if (report.NotCorrectedItems.Count == 0)
        {
            builder.AppendLine("- Nenhuma pendencia restante.");
        }
        else
        {
            foreach (var item in report.NotCorrectedItems)
            {
                builder.AppendLine($"- Curso `{item.CourseId}`, tarefa `{item.AssignmentId}`, estudante `{item.StudentId}`, item `{item.GradingItemId}`: {item.Reason}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string Describe(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "motivo nao informado." : value.Trim();

    private static string FormatGrade(decimal? grade) =>
        grade?.ToString("0.####", CultureInfo.InvariantCulture) ?? "n/d";
}
