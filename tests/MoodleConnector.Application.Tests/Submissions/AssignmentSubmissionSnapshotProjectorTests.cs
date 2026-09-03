using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;
using MoodleConnector.Application.Submissions;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Tests.Submissions;

public sealed class AssignmentSubmissionSnapshotProjectorTests
{
    [Fact]
    public void Build_creates_rows_for_submitted_and_not_submitted_students()
    {
        var dueAt = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        var contents = Contents(dueAt);
        var participants = Participants();
        var batches = new[]
        {
            new AssignmentSubmissionsBatch(
                "assignment-1",
                [new AssignmentSubmissionRecord(
                    "submission-1",
                    "student-1",
                    "submitted",
                    "notgraded",
                    dueAt.AddHours(1),
                    dueAt.AddHours(1),
                    1,
                    2,
                    true)])
        };

        var snapshot = AssignmentSubmissionSnapshotProjector.Build(contents, participants, batches);
        var assignment = Assert.Single(snapshot.Assignments);

        Assert.True(assignment.IsComplete);
        Assert.Equal(2, assignment.Submissions.Count);
        var submitted = Assert.Single(assignment.Submissions, row => row.UserId == "student-1");
        var notSubmitted = Assert.Single(assignment.Submissions, row => row.UserId == "student-2");
        Assert.True(submitted.Submitted);
        Assert.True(submitted.Late);
        Assert.True(submitted.NeedsGrading);
        Assert.Equal(SubmissionEvaluationState.AwaitingGrading, submitted.EvaluationState);
        Assert.False(notSubmitted.Submitted);
        Assert.False(notSubmitted.NeedsGrading);
    }

    [Fact]
    public void Build_excludes_submission_for_user_outside_active_student_population()
    {
        var snapshot = AssignmentSubmissionSnapshotProjector.Build(
            Contents(DateTimeOffset.UtcNow.AddDays(1)),
            Participants(),
            [new AssignmentSubmissionsBatch(
                "assignment-1",
                [new AssignmentSubmissionRecord(
                    "submission-staff",
                    "staff-1",
                    "new",
                    "notgraded",
                    null,
                    null,
                    null,
                    0,
                    false)])]);

        var rows = Assert.Single(snapshot.Assignments).Submissions;
        Assert.DoesNotContain(rows, row => row.UserId == "staff-1");
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void ToPage_applies_status_filter_and_pagination_without_moodle_call()
    {
        var contents = Contents(DateTimeOffset.UtcNow.AddDays(-1));
        var participants = Participants();
        var batches = new[]
        {
            new AssignmentSubmissionsBatch(
                "assignment-1",
                [new AssignmentSubmissionRecord(
                    "submission-1",
                    "student-1",
                    "submitted",
                    "needsgrading",
                    DateTimeOffset.UtcNow.AddHours(-1),
                    DateTimeOffset.UtcNow.AddHours(-1),
                    1,
                    0,
                    false)])
        };
        var snapshot = AssignmentSubmissionSnapshotProjector.Build(contents, participants, batches);
        var assignment = Assert.Single(snapshot.Assignments);

        var page = AssignmentSubmissionSnapshotProjector.ToPage(
            assignment,
            "course-1",
            AssignmentSubmissionFilter.NeedsGrading,
            page: 1,
            pageSize: 1,
            since: null,
            before: null,
            includeLate: true,
            includeUngraded: true);

        Assert.Equal(1, page.Total);
        Assert.False(page.HasMore);
        Assert.Single(page.Submissions);
    }

    [Fact]
    public void Build_includes_submitted_work_for_assignment_without_numeric_grade()
    {
        var contents = Contents(DateTimeOffset.UtcNow.AddDays(1));
        var snapshot = AssignmentSubmissionSnapshotProjector.Build(
            contents,
            Participants(),
            [new AssignmentSubmissionsBatch(
                "assignment-1",
                [new AssignmentSubmissionRecord(
                    "submission-1",
                    "student-1",
                    "submitted",
                    "notgraded",
                    DateTimeOffset.UtcNow.AddHours(-1),
                    DateTimeOffset.UtcNow.AddHours(-1),
                    1,
                    0,
                    false)])],
            new Dictionary<string, AssignmentSettingsSummary>(StringComparer.Ordinal)
            {
                ["assignment-1"] = new("assignment-1", 0m, "Atividade sem nota", IsGradable: false),
            });

        var assignment = Assert.Single(snapshot.Assignments);
        var page = AssignmentSubmissionSnapshotProjector.ToPage(
            assignment,
            "course-1",
            AssignmentSubmissionFilter.NeedsGrading,
            page: 1,
            pageSize: 20,
            since: null,
            before: null,
            includeLate: true,
            includeUngraded: true);

        Assert.Single(page.Submissions);
        Assert.Equal(1, page.Total);
        Assert.True(Assert.Single(assignment.Submissions, row => row.UserId == "student-1").NeedsGrading);
    }

    [Fact]
    public void Build_marks_assignment_incomplete_when_gateway_batch_has_error()
    {
        var contents = Contents(DateTimeOffset.UtcNow.AddDays(1));
        var snapshot = AssignmentSubmissionSnapshotProjector.Build(
            contents,
            Participants(),
            [new AssignmentSubmissionsBatch(
                "assignment-1",
                [],
                "moodle_timeout",
                "A consulta expirou.")]);

        var assignment = Assert.Single(snapshot.Assignments);
        Assert.False(assignment.IsComplete);
        Assert.Equal("moodle_timeout", assignment.ErrorCode);
        Assert.Equal(2, assignment.Submissions.Count);
        Assert.All(assignment.Submissions, row => Assert.False(row.Submitted));
    }

    [Fact]
    public void Build_persists_assignment_grade_settings_for_offline_pending_reads()
    {
        var snapshot = AssignmentSubmissionSnapshotProjector.Build(
            Contents(DateTimeOffset.UtcNow.AddDays(1)),
            Participants(),
            [new AssignmentSubmissionsBatch("assignment-1", [])],
            new Dictionary<string, AssignmentSettingsSummary>(StringComparer.Ordinal)
            {
                ["assignment-1"] = new("assignment-1", 100m, "Assignment 1"),
            });

        Assert.Equal(100m, Assert.Single(snapshot.Assignments).MaxGrade);
        Assert.Equal(true, Assert.Single(snapshot.Assignments).IsGradable);
    }

    [Fact]
    public void Build_persists_current_grade_feedback_and_complete_grade_coverage()
    {
        var snapshot = AssignmentSubmissionSnapshotProjector.Build(
            Contents(DateTimeOffset.UtcNow.AddDays(1)),
            Participants(),
            [new AssignmentSubmissionsBatch("assignment-1", [new AssignmentSubmissionRecord(
                "submission-1",
                "student-1",
                "submitted",
                "graded",
                DateTimeOffset.UtcNow.AddHours(-1),
                DateTimeOffset.UtcNow.AddHours(-1),
                1,
                0,
                false)])],
            new Dictionary<string, AssignmentSettingsSummary>(StringComparer.Ordinal)
            {
                ["assignment-1"] = new("assignment-1", 100m, "Assignment 1"),
            },
            new Dictionary<string, IReadOnlyDictionary<string, AssignmentExistingGrade>>(StringComparer.OrdinalIgnoreCase)
            {
                ["assignment-1"] = new Dictionary<string, AssignmentExistingGrade>(StringComparer.OrdinalIgnoreCase)
                {
                    ["student-1"] = new("assignment-1", "student-1", 87.5m, true, "Bom trabalho", 100m)
                }
            });

        var assignment = Assert.Single(snapshot.Assignments);
        Assert.Equal(AssignmentGradingMode.Numeric, assignment.GradingMode);
        Assert.NotNull(assignment.Coverage);
        Assert.True(assignment.Coverage!.NeedsGradingComplete);
        var submitted = Assert.Single(assignment.Submissions, row => row.UserId == "student-1");
        Assert.Equal(87.5m, submitted.CurrentGrade);
        Assert.Equal("Bom trabalho", submitted.CurrentFeedback);
        Assert.Equal(100m, submitted.GradeMax);
    }

    [Fact]
    public void Build_marks_grade_coverage_incomplete_when_grade_read_is_missing()
    {
        var snapshot = AssignmentSubmissionSnapshotProjector.Build(
            Contents(DateTimeOffset.UtcNow.AddDays(1)),
            Participants(),
            [new AssignmentSubmissionsBatch("assignment-1", [])],
            new Dictionary<string, AssignmentSettingsSummary>(StringComparer.Ordinal)
            {
                ["assignment-1"] = new("assignment-1", 100m, "Assignment 1"),
            });

        var assignment = Assert.Single(snapshot.Assignments);
        Assert.NotNull(assignment.Coverage);
        Assert.False(assignment.Coverage!.NeedsGradingComplete);
        Assert.Throws<InvalidOperationException>(() => AssignmentSubmissionSnapshotProjector.ToPage(
            assignment,
            "course-1",
            AssignmentSubmissionFilter.NeedsGrading,
            page: 1,
            pageSize: 20,
            since: null,
            before: null,
            includeLate: true,
            includeUngraded: true));
    }

    private static CourseContentsSummary Contents(DateTimeOffset dueAt) =>
        new(
            "course-1",
            [],
            IncludeHidden: false,
            OnlyWithFiles: false,
            [new CourseSectionSummary(
                "section-1",
                1,
                "Section 1",
                null,
                true,
                1,
                false,
                [new CourseModuleSummary(
                    "module-1",
                    "assignment-1",
                    "assign",
                    "Assignment 1",
                    null,
                    true,
                    true,
                    null,
                    null,
                    [new CourseModuleDate("Due date", dueAt)],
                    [])])]);

    private static CourseParticipantsPage Participants() =>
        new(
            "course-1",
            1,
            100,
            ParticipantStatusFilter.Active,
            StudentsOnly: true,
            IncludeEmail: false,
            HasMore: false,
            [
                new CourseParticipantSummary("student-1", "Student One", null, false, null, null, null, [], []),
                new CourseParticipantSummary("student-2", "Student Two", null, false, null, null, null, [], [])
            ]);
}
