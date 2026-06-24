using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Reports.Queries;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Tests.Reports;

public sealed class GenerateWeeklyPerformanceReportQueryHandlerTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static CourseParticipantSummary MakeStudent(
        string id, string name, DateTimeOffset? lastAccess = null) =>
        new(UserId: id, FullName: name, Email: null, Suspended: false,
            FirstAccessAt: null, LastAccessAt: null, LastCourseAccessAt: lastAccess,
            Roles: [], Groups: []);

    private static GradebookItem MakeGradeItem(
        string id, string name, decimal? gradeRaw, decimal gradeMax = 100m)
    {
        // Compute percentage so that BelowMinimum logic works in GradebookMappingHelper
        decimal? pct = gradeRaw.HasValue && gradeMax > 0
            ? gradeRaw.Value / gradeMax * 100m
            : null;
        return new(id, name, ItemType: "assign", ItemModule: "activity",
            CategoryId: null, GradeRaw: gradeRaw, GradeFormatted: null,
            GradeMin: 0m, GradeMax: gradeMax, PercentageFormatted: pct,
            Feedback: null, FeedbackFormat: null,
            GradedDateSubmitted: null, GradedDateGraded: null, GraderId: null);
    }

    private static GenerateWeeklyPerformanceReportQueryHandler CreateHandler(
        IReadOnlyList<CourseParticipantSummary> students,
        Dictionary<string, IReadOnlyList<GradebookItem>>? gradesByStudent = null)
    {
        return new GenerateWeeklyPerformanceReportQueryHandler(
            new FakeParticipantsGateway(students),
            new FakeGradebookGateway(gradesByStudent ?? []),
            new FakeCurrentUserGateway());
    }

    // ── Tests ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NoStudents_ReturnsEmptyWithWarning()
    {
        var handler = CreateHandler([]);
        var result = await handler.Handle(
            new GenerateWeeklyPerformanceReportQuery("course1"), CancellationToken.None);

        Assert.Equal(0, result.TotalStudents);
        Assert.Equal(0, result.StudentsAtRisk);
        Assert.Contains("Nenhum estudante ativo", result.Warning);
    }

    [Fact]
    public async Task Handle_StudentNeverAccessedAndBelowMinimum_IsRisk()
    {
        var students = new[] { MakeStudent("1", "João Silva", lastAccess: null) };
        var grades = new Dictionary<string, IReadOnlyList<GradebookItem>>
        {
            ["1"] = [MakeGradeItem("item1", "SA 1", gradeRaw: 40m)]  // below 60%
        };

        var handler = CreateHandler(students, grades);
        var result = await handler.Handle(
            new GenerateWeeklyPerformanceReportQuery("course1", MinGradePercent: 60m, InactiveDaysThreshold: 7),
            CancellationToken.None);

        Assert.Equal(1, result.TotalStudents);
        Assert.Equal(1, result.StudentsAtRisk);

        var student = result.Students.Single();
        Assert.True(student.NeverAccessed);
        Assert.Equal("risk", student.AttentionLevel);
        Assert.Equal(1, student.BelowMinimumCount);
    }

    [Fact]
    public async Task Handle_ActiveStudentWithGoodGrades_IsOk()
    {
        var recentAccess = DateTimeOffset.UtcNow.AddDays(-2);
        var students = new[] { MakeStudent("2", "Maria Souza", recentAccess) };
        var grades = new Dictionary<string, IReadOnlyList<GradebookItem>>
        {
            ["2"] = [MakeGradeItem("item1", "SA 1", gradeRaw: 80m)]  // above 60%
        };

        var handler = CreateHandler(students, grades);
        var result = await handler.Handle(
            new GenerateWeeklyPerformanceReportQuery("course1", MinGradePercent: 60m, InactiveDaysThreshold: 7),
            CancellationToken.None);

        Assert.Equal(1, result.TotalStudents);
        Assert.Equal(0, result.StudentsAtRisk);
        Assert.Equal(0, result.StudentsWithAttention);
        Assert.Equal("ok", result.Students.Single().AttentionLevel);
    }

    [Fact]
    public async Task Handle_ActiveStudentWithOneBelowMin_IsAttention()
    {
        var recentAccess = DateTimeOffset.UtcNow.AddDays(-2);
        var students = new[] { MakeStudent("3", "Carlos Lima", recentAccess) };
        var grades = new Dictionary<string, IReadOnlyList<GradebookItem>>
        {
            ["3"] = [MakeGradeItem("item1", "SA 1", gradeRaw: 45m)]  // below 60%, 1 factor
        };

        var handler = CreateHandler(students, grades);
        var result = await handler.Handle(
            new GenerateWeeklyPerformanceReportQuery("course1", MinGradePercent: 60m, InactiveDaysThreshold: 7),
            CancellationToken.None);

        Assert.Equal(1, result.StudentsWithAttention);
        Assert.Equal(0, result.StudentsAtRisk);
        Assert.Equal("attention", result.Students.Single().AttentionLevel);
    }

    [Fact]
    public async Task Handle_RiskStudentSortedBeforeOkStudent()
    {
        var now = DateTimeOffset.UtcNow;
        var students = new[]
        {
            MakeStudent("ok1",   "Maria OK",   now.AddDays(-1)),
            MakeStudent("risk1", "João Risco", lastAccess: null),
        };
        var grades = new Dictionary<string, IReadOnlyList<GradebookItem>>
        {
            ["ok1"]   = [MakeGradeItem("i1", "SA 1", 80m)],
            ["risk1"] = [MakeGradeItem("i2", "SA 1", 30m)],
        };

        var handler = CreateHandler(students, grades);
        var result = await handler.Handle(
            new GenerateWeeklyPerformanceReportQuery("course1"), CancellationToken.None);

        Assert.Equal("risk1", result.Students.First().StudentId);
        Assert.Equal("ok1",   result.Students.Last().StudentId);
    }

    [Fact]
    public async Task Handle_InactiveStudentInAccessRecipients_ActiveStudentNot()
    {
        var students = new[]
        {
            MakeStudent("inactive1", "Carlos Inativo", lastAccess: null),
            MakeStudent("active1",   "Ana Ativa",      DateTimeOffset.UtcNow.AddDays(-1)),
        };

        var handler = CreateHandler(students);
        var result = await handler.Handle(
            new GenerateWeeklyPerformanceReportQuery("course1", InactiveDaysThreshold: 7),
            CancellationToken.None);

        Assert.Single(result.SuggestedRecipientIdsForAccess);
        Assert.Equal("inactive1", result.SuggestedRecipientIdsForAccess.Single());
        Assert.DoesNotContain("active1", result.SuggestedRecipientIdsForAccess);
    }

    // ── Fakes ─────────────────────────────────────────────────────────────────────

    private sealed class FakeParticipantsGateway(IReadOnlyList<CourseParticipantSummary> students)
        : IMoodleParticipantsGateway
    {
        public Task<CourseParticipantsPage> GetCourseParticipantsAsync(
            string userExternalId, string courseId, ParticipantStatusFilter statusFilter,
            int page, int pageSize, bool studentsOnly, bool includeEmail, string? groupId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CourseParticipantsPage(courseId, page, pageSize,
                statusFilter, studentsOnly, includeEmail, HasMore: false, students));

        public Task<IReadOnlyList<CourseGroupSummary>> GetCourseGroupsAsync(
            string userExternalId, string courseId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CourseGroupSummary>>([]);
    }

    private sealed class FakeGradebookGateway(
        Dictionary<string, IReadOnlyList<GradebookItem>> gradesByStudent)
        : IMoodleGradebookGateway
    {
        public Task<CourseGradebook> GetStudentGradebookAsync(
            string courseId, string studentId, CancellationToken cancellationToken)
        {
            var items = gradesByStudent.TryGetValue(studentId, out var g) ? g : [];
            return Task.FromResult(new CourseGradebook(courseId, studentId, items));
        }
    }

    private sealed class FakeCurrentUserGateway : IMoodleCurrentUserIdGateway
    {
        public Task<long> GetCurrentUserIdAsync(CancellationToken cancellationToken) =>
            Task.FromResult(42L);
    }
}
