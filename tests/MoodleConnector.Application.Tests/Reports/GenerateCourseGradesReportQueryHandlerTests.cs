using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Reports.Queries;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Tests.Reports;

public sealed class GenerateCourseGradesReportQueryHandlerTests
{
    [Fact]
    public async Task Handle_UsesCourseTotalInsteadOfSummingActivityGrades()
    {
        var student = MakeStudent("1", "João Silva", new DateTimeOffset(2026, 8, 17, 12, 30, 0, TimeSpan.Zero));
        var handler = CreateHandler(
            [student],
            new Dictionary<string, IReadOnlyList<GradebookItem>>
            {
                [student.UserId] =
                [
                    MakeGradeItem("activity-1", "Atividade 1", "assign", 40m),
                    MakeGradeItem("activity-2", "Atividade 2", "assign", 40m),
                    MakeGradeItem("course-total", "Total do curso", "course", 70m)
                ]
            });

        var result = await handler.Handle(new GenerateCourseGradesReportQuery("course-1"), CancellationToken.None);

        var row = Assert.Single(result.Students);
        Assert.Equal(70m, row.TotalGrade);
        Assert.Equal(100m, row.TotalGradeMax);
        Assert.Equal(70m, row.TotalGradePercentage);
        Assert.Equal(student.LastAccessAt, row.LastAccessAt);
        Assert.Equal("com_nota", row.Status);
        Assert.Equal(1, result.StudentsWithGrade);
        Assert.Equal(0, result.StudentsWithoutGrade);
    }

    [Fact]
    public async Task Handle_DoesNotCreateGradeWhenMoodleDoesNotReturnCourseTotal()
    {
        var student = MakeStudent("2", "Maria Souza");
        var handler = CreateHandler(
            [student],
            new Dictionary<string, IReadOnlyList<GradebookItem>>
            {
                [student.UserId] = [MakeGradeItem("activity-1", "Atividade 1", "assign", 100m)]
            });

        var result = await handler.Handle(new GenerateCourseGradesReportQuery("course-1"), CancellationToken.None);

        var row = Assert.Single(result.Students);
        Assert.Null(row.TotalGrade);
        Assert.Equal("sem_nota", row.Status);
        Assert.Equal(0, result.StudentsWithGrade);
        Assert.Equal(1, result.StudentsWithoutGrade);
    }

    [Fact]
    public async Task Handle_ComputesPercentageWhenMoodleOmitsIt()
    {
        var student = MakeStudent("3", "Carlos Lima");
        var handler = CreateHandler(
            [student],
            new Dictionary<string, IReadOnlyList<GradebookItem>>
            {
                [student.UserId] = [new GradebookItem(
                    "course-total", "Total do curso", "course", string.Empty, null,
                    39m, "39", 0m, 100m, null, null, null, null, null, null)]
            });

        var result = await handler.Handle(new GenerateCourseGradesReportQuery("course-1"), CancellationToken.None);

        Assert.Equal(39m, Assert.Single(result.Students).TotalGradePercentage);
    }

    [Fact]
    public async Task Handle_Distingue_erro_de_gradebook_de_aluno_sem_nota()
    {
        var students = new[]
        {
            MakeStudent("1", "Aluno com gradebook"),
            MakeStudent("2", "Aluno não retornado"),
        };
        var prefetched = new CourseGradebookSnapshot(
            "course-1",
            new Dictionary<string, CourseGradebook>(StringComparer.OrdinalIgnoreCase)
            {
                ["1"] = new CourseGradebook("course-1", "1", [
                    MakeGradeItem("course-total", "Total do curso", "course", 70m),
                ]),
            },
            new GradebookSnapshotCoverage(
                "mixed",
                RequestedStudentCount: 2,
                ReturnedStudentCount: 1,
                IsComplete: false,
                Truncated: false,
                MissingStudentIds: ["2"],
                Warnings: ["student_read_failed"])
            {
                // The failed fallback is explicitly separated from a user
                // merely omitted by a bulk response.
                ErrorStudentIds = ["2"],
            });

        var handler = CreateHandler(students, []);
        var result = await handler.Handle(
            new GenerateCourseGradesReportQuery("course-1", PrefetchedGradebook: prefetched),
            CancellationToken.None);

        Assert.Equal("com_nota", result.Students.Single(row => row.StudentId == "1").Status);
        Assert.Equal(GradebookCoverageStates.Error, result.Students.Single(row => row.StudentId == "2").GradebookStatus);
    }

    private static GenerateCourseGradesReportQueryHandler CreateHandler(
        IReadOnlyList<CourseParticipantSummary> students,
        Dictionary<string, IReadOnlyList<GradebookItem>> gradesByStudent) =>
        new(
            new FakeParticipantsGateway(students),
            new FakeGradebookGateway(gradesByStudent),
            new FakeCurrentUserGateway());

    private static CourseParticipantSummary MakeStudent(string id, string name, DateTimeOffset? lastAccessAt = null) =>
        new(id, name, null, false, null, lastAccessAt, null, [], []);

    private static GradebookItem MakeGradeItem(string id, string name, string type, decimal grade) =>
        new(id, name, type, type == "course" ? string.Empty : "activity", null, grade, $"{grade:0.0}", 0m, 100m, grade, null, null, null, null, null);

    private sealed class FakeParticipantsGateway(IReadOnlyList<CourseParticipantSummary> students)
        : IMoodleParticipantsGateway
    {
        public Task<CourseParticipantsPage> GetCourseParticipantsAsync(
            string userExternalId, string courseId, ParticipantStatusFilter statusFilter,
            int page, int pageSize, bool studentsOnly, bool includeEmail, string? groupId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CourseParticipantsPage(courseId, page, pageSize,
                statusFilter, studentsOnly, includeEmail, false, students));

        public Task<IReadOnlyList<CourseGroupSummary>> GetCourseGroupsAsync(
            string userExternalId, string courseId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CourseGroupSummary>>([]);
    }

    private sealed class FakeGradebookGateway(
        Dictionary<string, IReadOnlyList<GradebookItem>> gradesByStudent)
        : IMoodleGradebookGateway
    {
        public Task<CourseGradebook> GetStudentGradebookAsync(
            string courseId, string studentId, CancellationToken cancellationToken) =>
            Task.FromResult(new CourseGradebook(
                courseId,
                studentId,
                gradesByStudent.TryGetValue(studentId, out var items) ? items : []));
    }

    private sealed class FakeCurrentUserGateway : IMoodleCurrentUserIdGateway
    {
        public Task<long> GetCurrentUserIdAsync(CancellationToken cancellationToken) =>
            Task.FromResult(42L);
    }
}
