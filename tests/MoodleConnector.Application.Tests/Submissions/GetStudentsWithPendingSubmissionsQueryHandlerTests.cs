using System.Text.Json;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;
using MoodleConnector.Application.Submissions.Queries;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Tests.Submissions;

public sealed class GetStudentsWithPendingSubmissionsQueryHandlerTests
{
    [Fact]
    public async Task Reports_real_absence_of_assign_without_marking_result_incomplete()
    {
        var fixture = new Fixture
        {
            Contents = Contents(),
        };

        var result = await fixture.CreateHandler().Handle(
            new GetStudentsWithPendingSubmissionsQuery("course-1", IncludeAwaitingGrading: true),
            CancellationToken.None);

        Assert.Contains("Nenhuma atividade do tipo 'assign' foi encontrada", result.Warning);
        Assert.DoesNotContain("ou processada", result.Warning);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task Distinguishes_assign_processing_failure_from_real_absence()
    {
        var fixture = new Fixture
        {
            Contents = Contents(Module("assign-1", "assign")),
            ThrowOnSubmissionRead = true,
        };

        var result = await fixture.CreateHandler().Handle(
            new GetStudentsWithPendingSubmissionsQuery("course-1", IncludeAwaitingGrading: true),
            CancellationToken.None);

        Assert.Contains("Foram encontradas atividades do tipo 'assign', mas nenhuma foi processada", result.Warning);
        Assert.Contains("contagem deste curso está incompleta", result.Warning);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task Marks_no_grade_submission_awaiting_when_grade_evidence_is_empty()
    {
        var fixture = new Fixture
        {
            Contents = Contents(Module("assign-1", "assign")),
            Submissions = [new AssignmentSubmissionsBatch(
                "assign-1",
                [new AssignmentSubmissionRecord(
                    "submission-1",
                    "student-1",
                    "submitted",
                    "notgraded",
                    DateTimeOffset.UtcNow.AddMinutes(-5),
                    DateTimeOffset.UtcNow,
                    1,
                    0,
                    false)])],
            AssignmentSettings = new Dictionary<string, AssignmentSettingsSummary>(StringComparer.Ordinal)
            {
                ["assign-1"] = new("assign-1", 0, "Atividade extra", IsGradable: false),
            },
            UseGradeRead = true,
        };

        var result = await fixture.CreateHandler().Handle(
            new GetStudentsWithPendingSubmissionsQuery("course-1", IncludeAwaitingGrading: true),
            CancellationToken.None);

        Assert.Null(result.Warning);
        Assert.Single(result.AwaitingGrading);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task Counts_only_no_grade_submissions_without_feedback()
    {
        var fixture = new Fixture
        {
            Contents = Contents(Module("assign-1", "assign")),
            Submissions = [new AssignmentSubmissionsBatch(
                "assign-1",
                [
                    new AssignmentSubmissionRecord("submission-1", "student-1", "submitted", "notgraded", DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow, 1, 0, false),
                    new AssignmentSubmissionRecord("submission-2", "student-2", "submitted", "notgraded", DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow, 1, 0, false)
                ])],
            AssignmentSettings = new Dictionary<string, AssignmentSettingsSummary>(StringComparer.Ordinal)
            {
                ["assign-1"] = new("assign-1", 0, "Atividade extra", IsGradable: false),
            },
            IncludeStudent2 = true,
            Grades = new Dictionary<string, AssignmentExistingGrade>(StringComparer.OrdinalIgnoreCase)
            {
                ["student-1"] = new("assign-1", "student-1", null, false, Feedback: "Feedback já publicado."),
                ["student-2"] = new("assign-1", "student-2", null, false, Feedback: null),
            }
        };

        var result = await fixture.CreateHandler().Handle(
            new GetStudentsWithPendingSubmissionsQuery("course-1", IncludeAwaitingGrading: true),
            CancellationToken.None);

        var pending = Assert.Single(result.AwaitingGrading);
        Assert.Equal("student-2", pending.StudentId);
        Assert.DoesNotContain(result.AwaitingGrading, item => item.StudentId == "student-1");
    }

    [Fact]
    public async Task Returns_serializable_awaiting_grading_records_with_context()
    {
        var fixture = new Fixture
        {
            Contents = Contents(Module("assign-1", "assign")),
            UseGradeRead = true,
            Submissions = [new AssignmentSubmissionsBatch(
                "assign-1",
                [new AssignmentSubmissionRecord("submission-1", "student-1", "submitted", "notgraded", DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow, 1, 0, false)])]
        };

        var result = await fixture.CreateHandler().Handle(
            new GetStudentsWithPendingSubmissionsQuery("course-1", IncludeAwaitingGrading: true),
            CancellationToken.None);

        var item = Assert.Single(result.AwaitingGrading);
        Assert.Equal("course-1", item.CourseId);
        Assert.Equal("assign-1", item.AssignmentId);
        Assert.Equal("student-1", item.StudentId);
        Assert.Equal("submitted", item.SubmissionStatus);
        Assert.Equal("awaiting_grading", item.GradingStatus);
        Assert.Contains("assignmentId", JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Infers_missing_students_as_pending_when_assignment_batch_is_successfully_empty()
    {
        var fixture = new Fixture
        {
            Contents = Contents(Module("assign-1", "assign")),
            IncludeStudent2 = true,
            // Moodle can return a successful assignment entry with no
            // submission records for students who have not submitted.
            Submissions = [new AssignmentSubmissionsBatch("assign-1", [])]
        };

        var result = await fixture.CreateHandler().Handle(
            new GetStudentsWithPendingSubmissionsQuery("course-1"),
            CancellationToken.None);

        Assert.Equal(2, result.Students.Count);
        Assert.All(result.Students, student => Assert.Equal("assign-1", Assert.Single(student.PendingAssignments).AssignmentId));
        Assert.True(result.IsComplete);
    }

    [Fact]
    public async Task Treats_new_submission_status_as_not_submitted()
    {
        var fixture = new Fixture
        {
            Contents = Contents(Module("assign-1", "assign")),
            Submissions = [new AssignmentSubmissionsBatch(
                "assign-1",
                [new AssignmentSubmissionRecord(
                    "submission-1",
                    "student-1",
                    "new",
                    "notgraded",
                    null,
                    null,
                    0,
                    0,
                    false)])]
        };

        var result = await fixture.CreateHandler().Handle(
            new GetStudentsWithPendingSubmissionsQuery("course-1"),
            CancellationToken.None);

        var student = Assert.Single(result.Students);
        Assert.Equal("student-1", student.StudentId);
        Assert.Equal("assign-1", Assert.Single(student.PendingAssignments).AssignmentId);
    }

    [Fact]
    public async Task Reads_feedback_in_parallel_by_assignment_with_bounded_gateway_calls()
    {
        var fixture = new Fixture
        {
            Contents = Contents(Module("assign-1", "assign"), Module("assign-2", "assign")),
            Submissions =
            [
                new AssignmentSubmissionsBatch("assign-1", [new AssignmentSubmissionRecord("s1", "student-1", "submitted", "notgraded", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, 0, false)]),
                new AssignmentSubmissionsBatch("assign-2", [new AssignmentSubmissionRecord("s2", "student-1", "submitted", "notgraded", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, 0, false)])
            ],
            AssignmentSettings = new Dictionary<string, AssignmentSettingsSummary>(StringComparer.Ordinal)
            {
                ["assign-1"] = new("assign-1", 0, "Extra 1", IsGradable: false),
                ["assign-2"] = new("assign-2", 0, "Extra 2", IsGradable: false),
            },
            Grades = new Dictionary<string, AssignmentExistingGrade>(StringComparer.OrdinalIgnoreCase)
            {
                ["student-1"] = new("assign-1", "student-1", null, false, Feedback: null),
            }
        };

        var result = await fixture.CreateHandler().Handle(
            new GetStudentsWithPendingSubmissionsQuery("course-1", IncludeAwaitingGrading: true),
            CancellationToken.None);

        Assert.Equal(2, result.AwaitingGrading.Count);
        Assert.Equal(2, fixture.FeedbackReads);
    }

    [Fact]
    public async Task Reuses_prefetched_course_data_and_skips_moodle_reads_for_empty_assign_scope()
    {
        var fixture = new Fixture
        {
            Contents = Contents(),
        };
        var participants = new CourseParticipantsPage(
            "course-1",
            1,
            100,
            ParticipantStatusFilter.Active,
            StudentsOnly: true,
            IncludeEmail: false,
            HasMore: false,
            Participants: [new CourseParticipantSummary("student-1", "Aluno 1", null, false, null, null, null, [], [])]);

        var result = await fixture.CreateHandler().Handle(
            new GetStudentsWithPendingSubmissionsQuery(
                "course-1",
                IncludeAwaitingGrading: true,
                PrefetchedContents: fixture.Contents,
                PrefetchedParticipants: participants),
            CancellationToken.None);

        Assert.Contains("Nenhuma atividade do tipo 'assign'", result.Warning);
        Assert.Equal(0, fixture.ParticipantReads);
        Assert.Equal(0, fixture.ContentReads);
        Assert.Equal(0, fixture.AssignmentSettingsReads);
        Assert.Equal(0, fixture.SubmissionReads);
    }

    [Fact]
    public async Task Reuses_complete_submission_snapshot_without_moodle_reads()
    {
        var fixture = new Fixture
        {
            Contents = Contents(Module("assign-1", "assign")),
        };
        var participants = new CourseParticipantsPage(
            "course-1",
            1,
            100,
            ParticipantStatusFilter.Active,
            StudentsOnly: true,
            IncludeEmail: false,
            HasMore: false,
            Participants: [new CourseParticipantSummary("student-1", "Aluno 1", null, false, null, null, null, [], [])]);
        var submissions = new CourseAssignmentSubmissionsSnapshot(
            "course-1",
            [new AssignmentSubmissionsSnapshotItem(
                "assign-1",
                "module-assign-1",
                "Atividade",
                null,
                [new AssignmentSubmissionSummary(
                    "student-1",
                    "Aluno 1",
                    null,
                    "not_submitted",
                    null,
                    false,
                    false,
                    false,
                    null,
                    null,
                    null,
                    0,
                    false,
                    [])],
                MaxGrade: 100m)]);

        var result = await fixture.CreateHandler().Handle(
            new GetStudentsWithPendingSubmissionsQuery(
                "course-1",
                IncludeAwaitingGrading: true,
                PrefetchedContents: fixture.Contents,
                PrefetchedParticipants: participants,
                PrefetchedSubmissions: submissions),
            CancellationToken.None);

        Assert.Single(result.Students);
        Assert.Equal(0, fixture.ParticipantReads);
        Assert.Equal(0, fixture.ContentReads);
        Assert.Equal(0, fixture.AssignmentSettingsReads);
        Assert.Equal(0, fixture.SubmissionReads);
    }

    private static CourseContentsSummary Contents(params CourseModuleSummary[] modules) =>
        new("course-1", ["assign"], false, false,
        [new CourseSectionSummary("section-1", 1, "Seção 1", null, true, modules.Length, modules.Length == 0, modules)]);

    private static CourseModuleSummary Module(string instanceId, string type) =>
        new($"module-{instanceId}", instanceId, type, "Atividade", null, true, true, null, null, [], []);

    private sealed class Fixture
    {
        public CourseContentsSummary Contents { get; init; } = Contents();
        public IReadOnlyList<AssignmentSubmissionsBatch> Submissions { get; init; } = [];
        public IReadOnlyDictionary<string, AssignmentSettingsSummary> AssignmentSettings { get; init; } =
            new Dictionary<string, AssignmentSettingsSummary>(StringComparer.Ordinal);
        public bool ThrowOnSubmissionRead { get; init; }
        public bool IncludeStudent2 { get; init; }
        public IReadOnlyDictionary<string, AssignmentExistingGrade> Grades { get; init; } =
            new Dictionary<string, AssignmentExistingGrade>(StringComparer.OrdinalIgnoreCase);
        public bool UseGradeRead { get; init; }
        public int FeedbackReads { get; set; }
        public int ParticipantReads { get; set; }
        public int ContentReads { get; set; }
        public int AssignmentSettingsReads { get; set; }
        public int SubmissionReads { get; set; }

        public GetStudentsWithPendingSubmissionsQueryHandler CreateHandler() =>
            new(
                new ParticipantsGateway(this),
                new SubmissionsGateway(this),
                new ContentsGateway(this),
                new CurrentUserGateway(),
                new AssignmentSettingsGateway(this),
                Grades.Count > 0 || UseGradeRead ? new AssignmentGradeReadGateway(this) : null);
    }

    private sealed class ParticipantsGateway(Fixture fixture) : IMoodleParticipantsGateway
    {
        public Task<CourseParticipantsPage> GetCourseParticipantsAsync(
            string userExternalId,
            string courseId,
            ParticipantStatusFilter statusFilter,
            int page,
            int pageSize,
            bool studentsOnly,
            bool includeEmail,
            string? groupId,
            CancellationToken cancellationToken)
        {
            fixture.ParticipantReads++;
            var participants = fixture.IncludeStudent2
                ? new[]
                {
                    new CourseParticipantSummary("student-1", "Aluno 1", null, false, null, null, null, [], []),
                    new CourseParticipantSummary("student-2", "Aluno 2", null, false, null, null, null, [], [])
                }
                : new[] { new CourseParticipantSummary("student-1", "Aluno 1", null, false, null, null, null, [], []) };
            return Task.FromResult(new CourseParticipantsPage(courseId, page, pageSize, statusFilter, studentsOnly, includeEmail, false, participants));
        }

        public Task<IReadOnlyList<CourseGroupSummary>> GetCourseGroupsAsync(string userExternalId, string courseId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CourseGroupSummary>>([]);
    }

    private sealed class ContentsGateway(Fixture fixture) : IMoodleCourseContentsGateway
    {
        public Task<CourseContentsSummary> GetCourseContentsAsync(
            string userExternalId,
            string courseId,
            IReadOnlyCollection<string> moduleTypes,
            bool includeHidden,
            bool onlyWithFiles,
            CancellationToken cancellationToken)
        {
            fixture.ContentReads++;
            return Task.FromResult(fixture.Contents);
        }
    }

    private sealed class SubmissionsGateway(Fixture fixture) : IMoodleAssignmentSubmissionsGateway
    {
        public Task<IReadOnlyList<AssignmentSubmissionsBatch>> GetAssignmentSubmissionsBatchAsync(
            string userExternalId,
            IReadOnlyCollection<string> assignmentIds,
            string? status,
            DateTimeOffset? since,
            DateTimeOffset? before,
            CancellationToken cancellationToken)
        {
            fixture.SubmissionReads++;
            if (fixture.ThrowOnSubmissionRead) throw new InvalidOperationException("submission read failed");
            return Task.FromResult(fixture.Submissions);
        }

        public Task<IReadOnlyList<AssignmentSubmissionRecord>> GetAssignmentSubmissionsAsync(
            string userExternalId,
            string assignmentId,
            string? status,
            DateTimeOffset? since,
            DateTimeOffset? before,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssignmentSubmissionRecord>>([]);
    }

    private sealed class CurrentUserGateway : IMoodleCurrentUserIdGateway
    {
        public Task<long> GetCurrentUserIdAsync(CancellationToken cancellationToken) => Task.FromResult(1L);
    }

    private sealed class AssignmentSettingsGateway(Fixture fixture) : IMoodleAssignmentSettingsGateway
    {
        public Task<IReadOnlyDictionary<string, AssignmentSettingsSummary>> GetCourseAssignmentSettingsAsync(
            string userExternalId,
            string courseId,
            CancellationToken cancellationToken)
        {
            fixture.AssignmentSettingsReads++;
            return Task.FromResult(fixture.AssignmentSettings);
        }

        public Task<AssignmentSettingsSummary?> GetAssignmentSettingsAsync(
            string userExternalId,
            string courseId,
            string assignmentId,
            CancellationToken cancellationToken) => Task.FromResult<AssignmentSettingsSummary?>(null);
    }

    private sealed class AssignmentGradeReadGateway(Fixture fixture) : IMoodleAssignmentGradeReadGateway
    {
        public Task<AssignmentExistingGrade?> GetExistingGradeAsync(string userExternalId, string assignmentId, string studentId, CancellationToken cancellationToken) =>
            Task.FromResult<AssignmentExistingGrade?>(fixture.Grades.GetValueOrDefault(studentId));

        public async Task<IReadOnlyDictionary<string, AssignmentExistingGrade>> GetExistingGradesAsync(
            string userExternalId,
            string assignmentId,
            IReadOnlyCollection<string> studentIds,
            CancellationToken cancellationToken)
        {
            fixture.FeedbackReads++;
            await Task.Delay(1, cancellationToken);
            return fixture.Grades;
        }
    }

}
