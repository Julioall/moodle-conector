using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Gradebook.Queries;
using MoodleConnector.Application.Reports.Queries;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Tests.Reports;

public sealed class DerivedGradeReportsQueryHandlerTests
{
    [Fact]
    public async Task ClassCouncil_ConsidersCourseTotalAndIgnoresOptionalRecoveryAsPending()
    {
        var sut = new GenerateClassCouncilReportQueryHandler(
            new FakeParticipantsGateway(),
            new FakeGradebookGateway(),
            new FakeCurrentUserGateway());

        var result = await sut.Handle(
            new GenerateClassCouncilReportQuery("10", MinGradePercent: 60m),
            CancellationToken.None);

        var row = Assert.Single(result.Students);
        Assert.Equal(2, row.BelowMinimumCount);
        Assert.Equal(0, row.PendingItemsCount);
        Assert.Equal("recovery_needed", row.SituationFlag);
    }

    [Fact]
    public async Task PostExecution_ConsidersCourseTotalAndIgnoresOptionalRecoveryAsPending()
    {
        var sut = new GeneratePostExecutionReportQueryHandler(
            new FakeParticipantsGateway(),
            new FakeGradebookGateway(),
            new FakeCurrentUserGateway());

        var result = await sut.Handle(
            new GeneratePostExecutionReportQuery("10", MinGradePercent: 60m),
            CancellationToken.None);

        var row = Assert.Single(result.Students);
        Assert.Equal(2, row.BelowMinimumCount);
        Assert.Equal(0, row.PendingCount);
        Assert.Equal("pending_recovery", row.OutcomeIndicator);
    }

    [Fact]
    public async Task StudentsBelowMinimum_IncludesCourseTotalAndZeroGrade()
    {
        var sut = new GetStudentsBelowMinGradeQueryHandler(
            new FakeParticipantsGateway(),
            new FakeGradebookGateway(),
            new FakeCurrentUserGateway());

        var result = await sut.Handle(
            new GetStudentsBelowMinGradeQuery("10", MinGradePercent: 60m),
            CancellationToken.None);

        var student = Assert.Single(result.Students);
        Assert.Equal(2, student.BelowMinimumItems.Count);
        Assert.Contains(student.BelowMinimumItems, item => item.ItemType == "course");
        Assert.DoesNotContain(student.BelowMinimumItems, item => item.ItemName.Contains("recupera", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<GradebookItem> GradebookItems =>
    [
        new GradebookItem("course", "Total do curso", "course", "", null,
            47m, "47", 0m, 100m, null, null, null, null, null, null),
        new GradebookItem("zero", "Momento presencial", "assign", "activity", null,
            0m, "0", 0m, 26m, null, null, null, null, null, null),
        new GradebookItem("recovery", "SAP Recuperação", "assign", "activity", null,
            null, null, 0m, 100m, null, null, null, null, null, null)
    ];

    private sealed class FakeParticipantsGateway : IMoodleParticipantsGateway
    {
        public Task<CourseParticipantsPage> GetCourseParticipantsAsync(
            string userExternalId, string courseId, ParticipantStatusFilter statusFilter,
            int page, int pageSize, bool studentsOnly, bool includeEmail, string? groupId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CourseParticipantsPage(
                courseId, page, pageSize, statusFilter, studentsOnly, includeEmail,
                HasMore: false,
                Participants:
                [
                    new CourseParticipantSummary(
                        "1", "Lavinia", null, false, null,
                        DateTimeOffset.UtcNow.AddDays(-1),
                        DateTimeOffset.UtcNow.AddDays(-1), [], [])
                ]));

        public Task<IReadOnlyList<CourseGroupSummary>> GetCourseGroupsAsync(
            string userExternalId, string courseId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CourseGroupSummary>>([]);
    }

    private sealed class FakeGradebookGateway : IMoodleGradebookGateway
    {
        public Task<CourseGradebook> GetStudentGradebookAsync(
            string courseId, string studentId, CancellationToken cancellationToken) =>
            Task.FromResult(new CourseGradebook(courseId, studentId, GradebookItems));
    }

    private sealed class FakeCurrentUserGateway : IMoodleCurrentUserIdGateway
    {
        public Task<long> GetCurrentUserIdAsync(CancellationToken cancellationToken) =>
            Task.FromResult(42L);
    }
}
