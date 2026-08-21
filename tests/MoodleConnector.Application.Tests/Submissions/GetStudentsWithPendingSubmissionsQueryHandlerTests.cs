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
    public async Task Marks_feedback_confirmation_failure_as_incomplete_and_omits_item()
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
                ["assign-1"] = new("assign-1", 0, "Atividade extra"),
            },
            ThrowOnFeedbackRead = true,
        };

        var result = await fixture.CreateHandler().Handle(
            new GetStudentsWithPendingSubmissionsQuery("course-1", IncludeAwaitingGrading: true),
            CancellationToken.None);

        Assert.Contains("Não foi possível confirmar todos os feedbacks", result.Warning);
        Assert.Empty(result.AwaitingGrading);
        Assert.False(result.IsComplete);
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
        public bool ThrowOnFeedbackRead { get; init; }

        public GetStudentsWithPendingSubmissionsQueryHandler CreateHandler() =>
            new(
                new ParticipantsGateway(),
                new SubmissionsGateway(this),
                new ContentsGateway(this),
                new CurrentUserGateway(),
                new AssignmentSettingsGateway(this),
                new SubmissionStatusGateway(this));
    }

    private sealed class ParticipantsGateway : IMoodleParticipantsGateway
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
            CancellationToken cancellationToken) =>
            Task.FromResult(new CourseParticipantsPage(courseId, page, pageSize, statusFilter, studentsOnly, includeEmail, false, [
                new CourseParticipantSummary("student-1", "Aluno 1", null, false, null, null, null, [], [])
            ]));

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
            CancellationToken cancellationToken) => Task.FromResult(fixture.Contents);
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
            CancellationToken cancellationToken) => Task.FromResult(fixture.AssignmentSettings);

        public Task<AssignmentSettingsSummary?> GetAssignmentSettingsAsync(
            string userExternalId,
            string courseId,
            string assignmentId,
            CancellationToken cancellationToken) => Task.FromResult<AssignmentSettingsSummary?>(null);
    }

    private sealed class SubmissionStatusGateway(Fixture fixture) : IMoodleAssignmentSubmissionStatusGateway
    {
        public Task<AssignmentSubmissionAttemptStatus?> GetSubmissionStatusAsync(
            string userExternalId,
            string assignmentId,
            string studentId,
            CancellationToken cancellationToken)
        {
            if (fixture.ThrowOnFeedbackRead) throw new InvalidOperationException("feedback read failed");
            return Task.FromResult<AssignmentSubmissionAttemptStatus?>(new(assignmentId, studentId, 1, "submitted", false));
        }
    }
}
