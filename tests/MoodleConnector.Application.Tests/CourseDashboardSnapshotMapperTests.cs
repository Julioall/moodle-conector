using MoodleConnector.Domain;
using MoodleConnector.Presentation;

namespace MoodleConnector.Application.Tests;

public sealed class CourseDashboardSnapshotMapperTests
{
    [Fact]
    public void Builds_course_indicators_from_its_own_snapshot_rows_only()
    {
        var now = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var dashboard = CourseDashboardSnapshotMapper.Create(
            Course("course-1"),
            Participants(now),
            PendingSnapshot(),
            connectionRef: "demo",
            todayEvents: 1,
            todayTasks: 2,
            generatedAt: now,
            week: AppDashboardWeekFilter.Current,
            weekStartsAt: now.AddDays(-1),
            weekEndsAt: now.AddDays(6),
            refreshQueued: false);

        Assert.Equal(2, dashboard.Summary.ActiveStudents);
        Assert.Equal(1, dashboard.Summary.StudentsAtRisk);
        Assert.Equal(2, dashboard.Summary.StudentsNeedingAttention);
        Assert.Equal(1, dashboard.Summary.PendingSubmissionAssignments);
        Assert.Equal(1, dashboard.Summary.PendingCorrectionAssignments);
        Assert.Equal(3, dashboard.Priorities.Count);
        Assert.All(dashboard.Priorities, item => Assert.Equal("course-1", item.CourseId));
        Assert.Single(dashboard.ActivitiesToReview);
    }

    [Fact]
    public void Keeps_unavailable_snapshot_indicators_explicit()
    {
        var now = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        var dashboard = CourseDashboardSnapshotMapper.Create(
            Course("course-1"),
            participants: null,
            pendingSnapshot: null,
            connectionRef: "demo",
            todayEvents: 0,
            todayTasks: 0,
            generatedAt: now,
            week: AppDashboardWeekFilter.Current,
            weekStartsAt: now,
            weekEndsAt: now.AddDays(7),
            refreshQueued: true);

        Assert.Null(dashboard.Summary.ActiveStudents);
        Assert.Null(dashboard.Summary.PendingSubmissionAssignments);
        Assert.Null(dashboard.Summary.PendingCorrectionAssignments);
        Assert.Contains(dashboard.Warnings, warning => warning.Contains("está sendo preparada", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dashboard.Warnings, warning => warning.Contains("Atualização solicitada", StringComparison.OrdinalIgnoreCase));
    }

    private static CourseSummary Course(string courseId) => new(
        courseId, null, "CURSO", "Curso de teste", null, null, null,
        null, null, true, null, null, null, null, null, null);

    private static CourseParticipantsPage Participants(DateTimeOffset now) => new(
        "course-1", 1, 25, ParticipantStatusFilter.Active, true, false, false,
        [
            new CourseParticipantSummary("student-1", "Alice", null, false, null, null, now.AddDays(-21), [], []),
            new CourseParticipantSummary("student-2", "Bruno", null, false, null, null, now.AddDays(-1), [], []),
        ]);

    private static AppDashboardPendingMetricDto PendingSnapshot() => new(
        new AppDashboardSummaryDto(2, 1, 1, 0, 1),
        [
            new AppDashboardPriorityDto("course-1:student-2:assign-1", "Entrega pendente", "Bruno · Atividade", "attention", "course-1", "student-2"),
            new AppDashboardPriorityDto("course-2:student-9:assign-9", "Entrega pendente", "Outro · Atividade", "risk", "course-2", "student-9"),
        ],
        [
            new AppDashboardPriorityDto("course-1:student-2:assign-1:grading", "Atividade para corrigir", "Bruno · Atividade", "attention", "course-1", "student-2"),
            new AppDashboardPriorityDto("course-2:student-9:assign-9:grading", "Atividade para corrigir", "Outro · Atividade", "attention", "course-2", "student-9"),
        ],
        [],
        [],
        [])
    {
        CoursesInScope = 2,
        CoursesAnalyzed = 2,
    };
}
