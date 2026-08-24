using MoodleConnector.Domain;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Application.Submissions;

/// <summary>
/// Builds and reads the normalized assignment-submission snapshot shared by
/// MCP submission tools and the portal pending-submissions flow.
/// </summary>
public static class AssignmentSubmissionSnapshotProjector
{
    public static CourseAssignmentSubmissionsSnapshot Build(
        CourseContentsSummary contents,
        CourseParticipantsPage participants,
        IReadOnlyList<AssignmentSubmissionsBatch> batches,
        IReadOnlyDictionary<string, AssignmentSettingsSummary>? assignmentSettings = null)
    {
        var batchesByAssignment = batches
            .GroupBy(batch => batch.AssignmentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        var assignments = contents.Sections
            .SelectMany(section => section.Modules)
            .Where(module => string.Equals(module.ModuleType, "assign", StringComparison.OrdinalIgnoreCase))
            .Where(module => !string.IsNullOrWhiteSpace(module.InstanceId))
            .GroupBy(module => module.InstanceId!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(module =>
            {
                var assignmentId = module.InstanceId!;
                var dueAt = FindDueDate(module.Dates);
                if (!batchesByAssignment.TryGetValue(assignmentId, out var batch))
                {
                    return new AssignmentSubmissionsSnapshotItem(
                        assignmentId,
                        module.ModuleId,
                        module.Name,
                        dueAt,
                        [],
                        IsComplete: false,
                        ErrorCode: "assignment_snapshot_missing",
                        ErrorMessage: "O Moodle não retornou dados desta tarefa durante a sincronização.",
                        MaxGrade: FindMaxGrade(assignmentSettings, module));
                }

                return new AssignmentSubmissionsSnapshotItem(
                    assignmentId,
                    module.ModuleId,
                    module.Name,
                    dueAt,
                    BuildRows(participants.Participants, batch.Submissions, dueAt),
                    IsComplete: string.IsNullOrWhiteSpace(batch.ErrorCode),
                    ErrorCode: batch.ErrorCode,
                    ErrorMessage: batch.ErrorMessage,
                    MaxGrade: FindMaxGrade(assignmentSettings, module));
            })
            .ToArray();

        return new CourseAssignmentSubmissionsSnapshot(contents.CourseId, assignments);
    }

    public static AssignmentSubmissionsSnapshotItem? FindAssignment(
        CourseAssignmentSubmissionsSnapshot snapshot,
        string assignmentId)
    {
        if (string.IsNullOrWhiteSpace(assignmentId)) return null;
        var normalized = assignmentId.Trim();
        return snapshot.Assignments.FirstOrDefault(item =>
            string.Equals(item.AssignmentId, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.AssignmentModuleId, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static AssignmentSubmissionsPage ToPage(
        AssignmentSubmissionsSnapshotItem item,
        string courseId,
        AssignmentSubmissionFilter filter,
        int page,
        int pageSize,
        DateTimeOffset? since,
        DateTimeOffset? before,
        bool includeLate,
        bool includeUngraded)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        var safePageSize = Math.Clamp(pageSize, 1, 100);
        var rows = FilterRows(item.Submissions, filter, since, before, includeLate, includeUngraded);
        var pageRows = rows.Skip((page - 1) * safePageSize).Take(safePageSize + 1).ToArray();
        return new AssignmentSubmissionsPage(
            courseId,
            item.AssignmentId,
            item.AssignmentModuleId,
            item.AssignmentName,
            page,
            safePageSize,
            filter,
            includeLate,
            includeUngraded,
            since,
            before,
            rows.Count,
            pageRows.Length > safePageSize,
            pageRows.Take(safePageSize).ToArray());
    }

    public static AssignmentSubmissionSummary? FindStudent(
        AssignmentSubmissionsSnapshotItem item,
        string studentId) =>
        item.Submissions.FirstOrDefault(submission =>
            string.Equals(submission.UserId, studentId?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static AssignmentSubmissionRecord ToRecord(AssignmentSubmissionSummary summary) =>
        new(
            summary.SubmissionId ?? $"snapshot:{summary.UserId}",
            summary.UserId,
            summary.Status,
            summary.GradingStatus,
            summary.SubmittedAt,
            summary.ModifiedAt,
            summary.AttemptNumber,
            summary.FileCount,
            summary.HasOnlineText,
            Files: []);

    private static IReadOnlyList<AssignmentSubmissionSummary> BuildRows(
        IReadOnlyList<CourseParticipantSummary> participants,
        IReadOnlyList<AssignmentSubmissionRecord> submissions,
        DateTimeOffset? dueAt)
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
        var rows = new List<AssignmentSubmissionSummary>(participants.Count + latestSubmissionByUser.Count);

        foreach (var participant in participants)
        {
            latestSubmissionByUser.TryGetValue(participant.UserId, out var submission);
            rows.Add(ToSummary(participant.UserId, participant.FullName, submission, dueAt));
        }

        foreach (var submission in latestSubmissionByUser.Values.Where(submission => !participantIds.Contains(submission.UserId)))
        {
            rows.Add(ToSummary(submission.UserId, null, submission, dueAt));
        }

        return rows
            .OrderBy(row => row.FullName ?? row.UserId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<AssignmentSubmissionSummary> FilterRows(
        IReadOnlyList<AssignmentSubmissionSummary> rows,
        AssignmentSubmissionFilter filter,
        DateTimeOffset? since,
        DateTimeOffset? before,
        bool includeLate,
        bool includeUngraded)
    {
        return rows
            .Where(row => MatchesFilter(row, filter))
            .Where(row => since is null || (row.ModifiedAt ?? row.SubmittedAt) >= since)
            .Where(row => before is null || (row.ModifiedAt ?? row.SubmittedAt) <= before)
            .Where(row => includeLate || filter == AssignmentSubmissionFilter.Late || !row.Late)
            .Where(row => includeUngraded || filter == AssignmentSubmissionFilter.NeedsGrading || !row.NeedsGrading)
            .ToArray();
    }

    private static AssignmentSubmissionSummary ToSummary(
        string userId,
        string? fullName,
        AssignmentSubmissionRecord? submission,
        DateTimeOffset? dueAt)
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
        var needsGrading = submitted && (
            string.Equals(submission.GradingStatus, "notgraded", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(submission.GradingStatus, "needsgrading", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(submission.GradingStatus, "notmarked", StringComparison.OrdinalIgnoreCase));

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
            Files: []);
    }

    private static bool MatchesFilter(AssignmentSubmissionSummary row, AssignmentSubmissionFilter filter) =>
        filter switch
        {
            AssignmentSubmissionFilter.Submitted => row.Submitted,
            AssignmentSubmissionFilter.NotSubmitted => !row.Submitted,
            AssignmentSubmissionFilter.Late => row.Late,
            AssignmentSubmissionFilter.NeedsGrading => row.NeedsGrading,
            _ => true,
        };

    private static DateTimeOffset? FindDueDate(IReadOnlyList<CourseModuleDate> dates) =>
        dates.FirstOrDefault(date =>
            date.Label.Contains("due", StringComparison.OrdinalIgnoreCase) ||
            date.Label.Contains("prazo", StringComparison.OrdinalIgnoreCase) ||
            date.Label.Contains("entrega", StringComparison.OrdinalIgnoreCase))?.Date;

    private static decimal? FindMaxGrade(
        IReadOnlyDictionary<string, AssignmentSettingsSummary>? settings,
        CourseModuleSummary module)
    {
        if (settings is null)
        {
            return null;
        }

        if (settings.TryGetValue(module.InstanceId ?? string.Empty, out var byInstance))
        {
            return byInstance.MaxGrade;
        }

        return settings.TryGetValue(module.ModuleId ?? string.Empty, out var byModule)
            ? byModule.MaxGrade
            : null;
    }
}
