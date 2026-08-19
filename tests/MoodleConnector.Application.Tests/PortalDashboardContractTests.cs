using MoodleConnector.Presentation;

public sealed class AppDashboardContractTests
{
    [Fact]
    public void Empty_dashboard_is_safe_and_preserves_scope_and_warnings()
    {
        var result = AppDashboardContractMapper.Empty("senai-goias", ["Selecione um curso para consultar indicadores detalhados."]);

        Assert.Equal("senai-goias", result.ConnectionRef);
        Assert.Empty(result.Priorities);
        Assert.Contains("Selecione um curso", result.Warnings.Single());
        Assert.Equal(AppDashboardBudget.MaxParticipantsRead, 500);
    }

    [Fact]
    public void Budget_is_bounded_and_does_not_encode_per_student_gradebook_fanout()
    {
        Assert.InRange(AppDashboardBudget.MaxCoursesRead, 1, 50);
        Assert.InRange(AppDashboardBudget.MaxParticipantsRead, 1, 500);
        Assert.InRange(AppDashboardBudget.MaxAssignmentsRead, 1, 100);
        Assert.InRange(AppDashboardBudget.MaxPriorities, 1, 50);
    }

    [Fact]
    public void Week_filter_normalizes_unknown_values_to_current_without_fabricating_history()
    {
        Assert.Equal(AppDashboardWeekFilter.Last, AppDashboardWeekFilter.Normalize("last"));
        Assert.Equal(AppDashboardWeekFilter.Current, AppDashboardWeekFilter.Normalize("unexpected"));
        Assert.Equal(AppDashboardWeekFilter.Current, AppDashboardWeekFilter.Normalize(null));
    }

    [Fact]
    public void Claris_indicators_keep_unavailable_values_explicit()
    {
        var summary = new AppDashboardSummaryDto(1, 4, 0, 2, 2)
        {
            TodayEvents = 3,
            TodayTasks = 1,
            ActivitiesToReview = 4,
            ActiveNormalStudents = 8,
            PendingSubmissionAssignments = 4,
            PendingCorrectionAssignments = null,
            NewAtRiskThisWeek = null,
        };

        Assert.Equal(3, summary.TodayEvents);
        Assert.Equal(8, summary.ActiveNormalStudents);
        Assert.Null(summary.PendingCorrectionAssignments);
        Assert.Null(summary.NewAtRiskThisWeek);
    }
}

