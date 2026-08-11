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
        Assert.Equal(AppDashboardBudget.MaxParticipantsRead, 100);
    }

    [Fact]
    public void Budget_is_bounded_and_does_not_encode_per_student_gradebook_fanout()
    {
        Assert.InRange(AppDashboardBudget.MaxCoursesRead, 1, 50);
        Assert.InRange(AppDashboardBudget.MaxParticipantsRead, 1, 100);
        Assert.InRange(AppDashboardBudget.MaxPriorities, 1, 20);
    }
}

