using MoodleConnector.Application.Abstractions;
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
        Assert.False(notSubmitted.Submitted);
        Assert.False(notSubmitted.NeedsGrading);
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
        Assert.Equal("student-1", Assert.Single(page.Submissions).UserId);
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
