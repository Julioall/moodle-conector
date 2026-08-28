using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Submissions;

public sealed record ListAssignmentSubmissionsQuery(
    string UserExternalId,
    string CourseId,
    string AssignmentId,
    AssignmentSubmissionFilter Filter,
    int Page,
    int PageSize,
    DateTimeOffset? Since,
    DateTimeOffset? Before,
    bool IncludeLate,
    bool IncludeUngraded) : IRequest<AssignmentSubmissionsPage?>;

public sealed record GetStudentSubmissionQuery(
    string UserExternalId,
    string CourseId,
    string AssignmentId,
    string StudentId) : IRequest<AssignmentSubmissionSummary?>;

internal sealed record AssignmentSubmissionRows(
    string CourseId,
    string AssignmentId,
    string AssignmentModuleId,
    string AssignmentName,
    IReadOnlyList<AssignmentSubmissionSummary> Rows);

public sealed class ListAssignmentSubmissionsQueryHandler(
    IMoodleCoursesGateway coursesGateway,
    IMoodleCourseContentsGateway contentsGateway,
    IMoodleParticipantsGateway participantsGateway,
    IMoodleAssignmentSubmissionsGateway submissionsGateway,
    IMoodleAssignmentSettingsGateway? assignmentSettingsGateway = null,
    IMoodleAssignmentGradeReadGateway? gradeReadGateway = null)
    : IRequestHandler<ListAssignmentSubmissionsQuery, AssignmentSubmissionsPage?>
{
    private const int ParticipantFetchPageSize = 100;
    private const int MaxParticipantsToMerge = 1000;

    public async Task<AssignmentSubmissionsPage?> Handle(
        ListAssignmentSubmissionsQuery request,
        CancellationToken cancellationToken)
    {
        var rowsContext = await BuildRowsAsync(request, cancellationToken);
        if (rowsContext is null)
        {
            return null;
        }

        var rows = rowsContext.Rows;
        if (request.Page < 1) throw new ArgumentOutOfRangeException(nameof(request.Page), "A página deve ser maior ou igual a 1. A paginação começa em 1.");
        var page = request.Page;
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var pagedRows = rows
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1)
            .ToArray();

        return new AssignmentSubmissionsPage(
            rowsContext.CourseId,
            rowsContext.AssignmentId,
            rowsContext.AssignmentModuleId,
            rowsContext.AssignmentName,
            page,
            pageSize,
            request.Filter,
            request.IncludeLate,
            request.IncludeUngraded,
            request.Since,
            request.Before,
            rows.Count,
            pagedRows.Length > pageSize,
            pagedRows.Take(pageSize).ToArray());
    }

    internal async Task<AssignmentSubmissionRows?> BuildRowsAsync(
        ListAssignmentSubmissionsQuery request,
        CancellationToken cancellationToken)
    {
        var course = await coursesGateway.GetMyCourseAsync(
            request.UserExternalId,
            request.CourseId,
            cancellationToken);
        if (course is null)
        {
            return null;
        }

        var assignment = await ResolveAssignmentAsync(
            request.UserExternalId,
            course.CourseId,
            request.AssignmentId,
            cancellationToken);
        if (assignment is null)
        {
            return null;
        }

        var assignmentInstanceId = assignment.InstanceId ?? assignment.ActivityId;
        var participants = await GetStudentsAsync(request.UserExternalId, course.CourseId, cancellationToken);
        var submissions = await submissionsGateway.GetAssignmentSubmissionsAsync(
            request.UserExternalId,
            assignmentInstanceId,
            ToMoodleSubmissionStatus(request.Filter),
            request.Since,
            request.Before,
            cancellationToken);

        AssignmentSettingsSummary? assignmentSettings = null;
        if (assignmentSettingsGateway is not null)
        {
            try
            {
                assignmentSettings = await assignmentSettingsGateway.GetAssignmentSettingsAsync(
                    request.UserExternalId,
                    course.CourseId,
                    assignmentInstanceId,
                    cancellationToken);
            }
            catch
            {
                // The submission status remains useful when optional grading
                // configuration is unavailable. Do not infer no-grade here.
            }
        }

        bool? isGradable = assignmentSettings is null
            ? null
            : assignmentSettings.IsGradable ?? (assignmentSettings.MaxGrade > 0 ? true : null);
        IReadOnlyDictionary<string, AssignmentExistingGrade>? existingGrades = null;
        if (request.Filter == AssignmentSubmissionFilter.NeedsGrading && gradeReadGateway is not null)
        {
            try
            {
                existingGrades = await gradeReadGateway.GetExistingGradesAsync(
                    request.UserExternalId,
                    assignmentInstanceId,
                    submissions.Select(submission => submission.UserId).ToArray(),
                    cancellationToken);
            }
            catch
            {
                // Do not turn a missing grade capability into zero pending
                // submissions. Fall back to Moodle's submission status.
                existingGrades = new Dictionary<string, AssignmentExistingGrade>(StringComparer.OrdinalIgnoreCase);
            }
        }

        var rows = BuildRows(
            participants,
            submissions,
            assignment.DueAt,
            request.Filter,
            request.IncludeLate,
            request.IncludeUngraded,
            isGradable,
            existingGrades);
        return new AssignmentSubmissionRows(
            course.CourseId,
            assignmentInstanceId,
            assignment.ActivityId,
            assignment.Name,
            rows);
    }

    internal async Task<CourseActivitySummary?> ResolveAssignmentAsync(
        string userExternalId,
        string courseId,
        string assignmentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assignmentId))
        {
            return null;
        }

        var contents = await contentsGateway.GetCourseContentsAsync(
            userExternalId,
            courseId,
            CourseActivityModuleTypes.Assignments,
            includeHidden: true,
            onlyWithFiles: false,
            cancellationToken);
        var normalizedAssignmentId = assignmentId.Trim();
        var module = contents.Sections
            .SelectMany(section => section.Modules)
            .FirstOrDefault(activity =>
                string.Equals(activity.ModuleType, "assign", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(activity.ModuleId, normalizedAssignmentId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(activity.InstanceId, normalizedAssignmentId, StringComparison.OrdinalIgnoreCase)));

        return module is null ? null : ToAssignmentActivity(module);
    }

    internal static CourseActivitySummary ToAssignmentActivity(CourseModuleSummary module)
    {
        return MoodleConnector.Application.Activities.ListCourseActivitiesQueryHandler.ToActivity(module);
    }

    private async Task<IReadOnlyList<CourseParticipantSummary>> GetStudentsAsync(
        string userExternalId,
        string courseId,
        CancellationToken cancellationToken)
    {
        var students = new List<CourseParticipantSummary>();
        var page = 1;
        while (students.Count < MaxParticipantsToMerge)
        {
            var result = await participantsGateway.GetCourseParticipantsAsync(
                userExternalId,
                courseId,
                ParticipantStatusFilter.Active,
                page,
                ParticipantFetchPageSize,
                studentsOnly: true,
                includeEmail: false,
                groupId: null,
                cancellationToken);

            students.AddRange(result.Participants);
            if (!result.HasMore)
            {
                break;
            }

            page++;
        }

        return students;
    }

    private static IReadOnlyList<AssignmentSubmissionSummary> BuildRows(
        IReadOnlyList<CourseParticipantSummary> participants,
        IReadOnlyList<AssignmentSubmissionRecord> submissions,
        DateTimeOffset? dueAt,
        AssignmentSubmissionFilter filter,
        bool includeLate,
        bool includeUngraded,
        bool? isGradable,
        IReadOnlyDictionary<string, AssignmentExistingGrade>? existingGrades = null)
    {
        var latestSubmissionByUser = submissions
            .GroupBy(submission => submission.UserId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(submission => submission.AttemptNumber ?? -1)
                    .ThenByDescending(submission => submission.ModifiedAt ?? submission.CreatedAt ?? DateTimeOffset.MinValue)
                    .First(),
                StringComparer.OrdinalIgnoreCase);
        var participantIds = participants
            .Select(participant => participant.UserId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = new List<AssignmentSubmissionSummary>();

        foreach (var participant in participants)
        {
            latestSubmissionByUser.TryGetValue(participant.UserId, out var submission);
            rows.Add(ToSummary(participant.UserId, participant.FullName, submission, dueAt, isGradable, existingGrades));
        }

        foreach (var submission in latestSubmissionByUser.Values.Where(submission => !participantIds.Contains(submission.UserId)))
        {
            rows.Add(ToSummary(submission.UserId, fullName: null, submission, dueAt, isGradable, existingGrades));
        }

        return rows
            .Where(row => MatchesFilter(row, filter))
            .Where(row => includeLate || filter == AssignmentSubmissionFilter.Late || !row.Late)
            .Where(row => includeUngraded || filter == AssignmentSubmissionFilter.NeedsGrading || !row.NeedsGrading)
            .OrderBy(row => row.FullName ?? row.UserId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static AssignmentSubmissionSummary ToSummary(
        string userId,
        string? fullName,
        AssignmentSubmissionRecord? submission,
        DateTimeOffset? dueAt,
        bool? isGradable,
        IReadOnlyDictionary<string, AssignmentExistingGrade>? existingGrades)
    {
        if (submission is null)
        {
            return new AssignmentSubmissionSummary(
                userId,
                fullName,
                SubmissionId: null,
                "not_submitted",
                GradingStatus: null,
                Submitted: false,
                Late: false,
                NeedsGrading: false,
                SubmittedAt: null,
                ModifiedAt: null,
                AttemptNumber: null,
                FileCount: 0,
                HasOnlineText: false,
                Files: []);
        }

        var submitted = string.Equals(submission.Status, "submitted", StringComparison.OrdinalIgnoreCase);
        var submittedAt = submitted ? submission.ModifiedAt ?? submission.CreatedAt : null;
        var late = submittedAt is not null && dueAt is not null && submittedAt > dueAt;
        AssignmentExistingGrade? existingGrade = null;
        if (existingGrades is not null)
        {
            existingGrades.TryGetValue(userId, out existingGrade);
        }
        // Moodle represents an ungraded submission with the numeric sentinel
        // -1. Preserve that value for diagnostics, but do not treat it as a
        // grade already entered by the teacher.
        var hasMoodleGradeValue = existingGrade?.HasGrade == true;
        var needsGrading = IsNeedsGrading(submission.GradingStatus, submitted) &&
            (existingGrades is null ? isGradable != false : !hasMoodleGradeValue);

        return new AssignmentSubmissionSummary(
            userId,
            fullName,
            submission.SubmissionId,
            submission.Status,
            submission.GradingStatus,
            submitted,
            late,
            needsGrading,
            submittedAt,
            submission.ModifiedAt,
            submission.AttemptNumber,
            submission.FileCount,
            submission.HasOnlineText,
            submission.Files ?? []);
    }

    private static bool MatchesFilter(AssignmentSubmissionSummary row, AssignmentSubmissionFilter filter)
    {
        return filter switch
        {
            AssignmentSubmissionFilter.Submitted => row.Submitted,
            AssignmentSubmissionFilter.NotSubmitted => !row.Submitted,
            AssignmentSubmissionFilter.Late => row.Late,
            AssignmentSubmissionFilter.NeedsGrading => row.NeedsGrading,
            AssignmentSubmissionFilter.All => true,
            _ => true
        };
    }

    private static bool IsNeedsGrading(string? gradingStatus, bool submitted)
    {
        if (!submitted)
        {
            return false;
        }

        return string.Equals(gradingStatus, "notgraded", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(gradingStatus, "needsgrading", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(gradingStatus, "notmarked", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ToMoodleSubmissionStatus(AssignmentSubmissionFilter filter)
    {
        return filter switch
        {
            AssignmentSubmissionFilter.Submitted => "submitted",
            AssignmentSubmissionFilter.NeedsGrading => null,
            _ => null,
        };
    }
}

public sealed class GetStudentSubmissionQueryHandler(
    IMoodleCoursesGateway coursesGateway,
    IMoodleCourseContentsGateway contentsGateway,
    IMoodleParticipantsGateway participantsGateway,
    IMoodleAssignmentSubmissionsGateway submissionsGateway,
    IMoodleAssignmentSettingsGateway? assignmentSettingsGateway = null)
    : IRequestHandler<GetStudentSubmissionQuery, AssignmentSubmissionSummary?>
{
    public async Task<AssignmentSubmissionSummary?> Handle(
        GetStudentSubmissionQuery request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.StudentId))
        {
            return null;
        }

        var handler = new ListAssignmentSubmissionsQueryHandler(
            coursesGateway,
            contentsGateway,
            participantsGateway,
            submissionsGateway,
            assignmentSettingsGateway);
        var rowsContext = await handler.BuildRowsAsync(
            new ListAssignmentSubmissionsQuery(
                request.UserExternalId,
                request.CourseId,
                request.AssignmentId,
                AssignmentSubmissionFilter.All,
                Page: 1,
                PageSize: 100,
                Since: null,
                Before: null,
                IncludeLate: true,
                IncludeUngraded: true),
            cancellationToken);

        return rowsContext?.Rows.FirstOrDefault(submission =>
            string.Equals(submission.UserId, request.StudentId.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
