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
    public async Task Omits_no_grade_feedback_without_per_submission_reads()
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
        };

        var result = await fixture.CreateHandler().Handle(
            new GetStudentsWithPendingSubmissionsQuery("course-1", IncludeAwaitingGrading: true),
            CancellationToken.None);

        Assert.Contains("Atividades sem nota foram omitidas", result.Warning);
        Assert.Empty(result.AwaitingGrading);
        Assert.True(result.IsComplete);
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
                new AssignmentSettingsGateway(this));
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
            return Task.FromResult(new CourseParticipantsPage(courseId, page, pageSize, statusFilter, studentsOnly, includeEmail, false, [
                new CourseParticipantSummary("student-1", "Aluno 1", null, false, null, null, null, [], [])
            ]));
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

}
