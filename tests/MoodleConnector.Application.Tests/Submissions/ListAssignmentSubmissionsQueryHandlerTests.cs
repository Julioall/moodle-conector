using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;
using MoodleConnector.Application.Submissions;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Tests.Submissions;

public class ListAssignmentSubmissionsQueryHandlerTests
{
    private static readonly DateTimeOffset DueAt = new(2026, 6, 10, 23, 59, 0, TimeSpan.Zero);

    [Fact]
    public async Task Deve_listar_quem_entregou_tarefa()
    {
        var submissionsGateway = new FakeSubmissionsGateway
        {
            Records =
            [
                Submitted("9001", "101", new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero), "graded")
            ]
        };
        var sut = CreateHandler(submissionsGateway: submissionsGateway);

        var result = await sut.Handle(
            new ListAssignmentSubmissionsQuery(
                "usuario-42",
                "CURSO-1",
                "11",
                AssignmentSubmissionFilter.Submitted,
                Page: 1,
                PageSize: 20,
                Since: null,
                Before: null,
                IncludeLate: true,
                IncludeUngraded: true),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("501", submissionsGateway.LastAssignmentId);
        Assert.Equal("submitted", submissionsGateway.LastStatus);
        Assert.Equal(1, result!.Total);
        Assert.Equal("101", result.Submissions[0].UserId);
        Assert.True(result.Submissions[0].Submitted);
        Assert.False(result.Submissions[0].Late);
    }

    [Fact]
    public async Task Deve_listar_estudante_sem_entrega()
    {
        var submissionsGateway = new FakeSubmissionsGateway
        {
            Records =
            [
                Submitted("9001", "101", new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero), "graded")
            ]
        };
        var sut = CreateHandler(submissionsGateway: submissionsGateway);

        var result = await sut.Handle(
            new ListAssignmentSubmissionsQuery(
                "usuario-42",
                "CURSO-1",
                "501",
                AssignmentSubmissionFilter.NotSubmitted,
                Page: 1,
                PageSize: 20,
                Since: null,
                Before: null,
                IncludeLate: true,
                IncludeUngraded: true),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(submissionsGateway.LastStatus);
        Assert.Equal(2, result!.Total);
        Assert.All(result.Submissions, submission => Assert.False(submission.Submitted));
        Assert.Contains(result.Submissions, submission => submission.UserId == "102");
    }

    [Fact]
    public async Task Nao_inclui_submissao_de_usuario_fora_da_lista_de_estudantes()
    {
        var sut = CreateHandler(submissionsGateway: new FakeSubmissionsGateway
        {
            Records =
            [
                Submitted("9001", "101", new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero), "graded"),
                Submitted("9002", "teacher-1", new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero), "notgraded")
            ]
        });

        var result = await sut.Handle(
            new ListAssignmentSubmissionsQuery("usuario-42", "CURSO-1", "11", AssignmentSubmissionFilter.All,
                Page: 1, PageSize: 20, Since: null, Before: null, IncludeLate: true, IncludeUngraded: true),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.DoesNotContain(result!.Submissions, submission => submission.UserId == "teacher-1");
        Assert.All(result.Submissions, submission => Assert.NotNull(submission.FullName));
    }

    [Fact]
    public async Task Deve_identificar_entrega_atrasada()
    {
        var sut = CreateHandler(
            submissionsGateway: new FakeSubmissionsGateway
            {
                Records =
                [
                    Submitted("9001", "101", new DateTimeOffset(2026, 6, 11, 1, 0, 0, TimeSpan.Zero), "graded")
                ]
            });

        var result = await sut.Handle(
            new ListAssignmentSubmissionsQuery(
                "usuario-42",
                "CURSO-1",
                "11",
                AssignmentSubmissionFilter.Late,
                Page: 1,
                PageSize: 20,
                Since: null,
                Before: null,
                IncludeLate: true,
                IncludeUngraded: true),
            CancellationToken.None);

        Assert.NotNull(result);
        var submission = Assert.Single(result!.Submissions);
        Assert.Equal("101", submission.UserId);
        Assert.True(submission.Late);
    }

    [Fact]
    public async Task Deve_listar_entregas_aguardando_correcao()
    {
        var submissionsGateway = new FakeSubmissionsGateway
        {
            Records =
            [
                Submitted("9001", "101", new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero), "notgraded"),
                Submitted("9002", "102", new DateTimeOffset(2026, 6, 10, 11, 0, 0, TimeSpan.Zero), "graded"),
                Submitted("9003", "103", new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero), null)
            ]
        };
        var sut = CreateHandler(
            submissionsGateway: submissionsGateway);

        var result = await sut.Handle(
            new ListAssignmentSubmissionsQuery(
                "usuario-42",
                "CURSO-1",
                "11",
                AssignmentSubmissionFilter.NeedsGrading,
                Page: 1,
                PageSize: 20,
                Since: null,
                Before: null,
                IncludeLate: true,
                IncludeUngraded: true),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Null(submissionsGateway.LastStatus);
        var submission = Assert.Single(result!.Submissions);
        Assert.Equal("101", submission.UserId);
        Assert.True(submission.NeedsGrading);
    }

    [Fact]
    public async Task Deve_manter_marcador_menos_um_do_Moodle_como_aguardando_correcao()
    {
        var sut = CreateHandler(
            submissionsGateway: new FakeSubmissionsGateway
            {
                Records =
                [
                    Submitted("9001", "101", new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero), "notgraded"),
                    Submitted("9002", "102", new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero), "notgraded"),
                    Submitted("9003", "103", new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero), "notgraded")
                ]
            },
            settingsGateway: new FakeAssignmentSettingsGateway
            {
                Settings = new AssignmentSettingsSummary("501", 0m, "Atividade sem nota", IsGradable: false)
            },
            gradeReadGateway: new FakeAssignmentGradeReadGateway
            {
                Grades = new Dictionary<string, AssignmentExistingGrade>(StringComparer.OrdinalIgnoreCase)
                {
                    ["101"] = new("501", "101", Grade: null, HasGrade: false),
                    ["102"] = new("501", "102", Grade: -1m, HasGrade: false),
                    ["103"] = new("501", "103", Grade: 20m, HasGrade: true)
                }
            });

        var result = await sut.Handle(
            new ListAssignmentSubmissionsQuery(
                "usuario-42",
                "CURSO-1",
                "11",
                AssignmentSubmissionFilter.NeedsGrading,
                Page: 1,
                PageSize: 20,
                Since: null,
                Before: null,
                IncludeLate: true,
                IncludeUngraded: true),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Total);
        Assert.Equal(["101", "102"], result.Submissions.Select(submission => submission.UserId));
        Assert.All(result.Submissions, submission => Assert.True(submission.NeedsGrading));
    }

    [Fact]
    public async Task Deve_manter_aguardando_correcao_quando_atividade_usa_escala()
    {
        var sut = CreateHandler(
            submissionsGateway: new FakeSubmissionsGateway
            {
                Records =
                [
                    Submitted("9001", "101", new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.Zero), "notgraded")
                ]
            },
            settingsGateway: new FakeAssignmentSettingsGateway
            {
                Settings = new AssignmentSettingsSummary("501", 0m, "Atividade com escala", IsGradable: true)
            });

        var result = await sut.Handle(
            new ListAssignmentSubmissionsQuery(
                "usuario-42",
                "CURSO-1",
                "11",
                AssignmentSubmissionFilter.NeedsGrading,
                Page: 1,
                PageSize: 20,
                Since: null,
                Before: null,
                IncludeLate: true,
                IncludeUngraded: true),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(Assert.Single(result!.Submissions).NeedsGrading);
    }

    [Fact]
    public async Task Deve_paginar_respostas_grandes()
    {
        var sut = CreateHandler();

        var result = await sut.Handle(
            new ListAssignmentSubmissionsQuery(
                "usuario-42",
                "CURSO-1",
                "11",
                AssignmentSubmissionFilter.All,
                Page: 2,
                PageSize: 1,
                Since: null,
                Before: null,
                IncludeLate: true,
                IncludeUngraded: true),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Total);
        Assert.True(result.HasMore);
        Assert.Single(result.Submissions);
        Assert.Equal("102", result.Submissions[0].UserId);
    }

    [Fact]
    public async Task Deve_retornar_null_para_tarefa_inexistente()
    {
        var submissionsGateway = new FakeSubmissionsGateway();
        var sut = CreateHandler(
            contentsGateway: new FakeContentsGateway { ReturnWithoutAssignment = true },
            submissionsGateway: submissionsGateway);

        var result = await sut.Handle(
            new ListAssignmentSubmissionsQuery(
                "usuario-42",
                "CURSO-1",
                "999",
                AssignmentSubmissionFilter.All,
                Page: 1,
                PageSize: 20,
                Since: null,
                Before: null,
                IncludeLate: true,
                IncludeUngraded: true),
            CancellationToken.None);

        Assert.Null(result);
        Assert.False(submissionsGateway.WasCalled);
    }

    [Fact]
    public async Task Deve_consultar_status_de_estudante_sem_entrega()
    {
        var sut = new GetStudentSubmissionQueryHandler(
            new FakeCoursesGateway(),
            new FakeContentsGateway(),
            new FakeParticipantsGateway(),
            new FakeSubmissionsGateway());

        var result = await sut.Handle(
            new GetStudentSubmissionQuery("usuario-42", "CURSO-1", "11", "102"),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("102", result!.UserId);
        Assert.False(result.Submitted);
        Assert.Equal("not_submitted", result.Status);
    }

    private static ListAssignmentSubmissionsQueryHandler CreateHandler(
        FakeContentsGateway? contentsGateway = null,
        FakeSubmissionsGateway? submissionsGateway = null,
        FakeAssignmentSettingsGateway? settingsGateway = null,
        FakeAssignmentGradeReadGateway? gradeReadGateway = null)
    {
        return new ListAssignmentSubmissionsQueryHandler(
            new FakeCoursesGateway(),
            contentsGateway ?? new FakeContentsGateway(),
            new FakeParticipantsGateway(),
            submissionsGateway ?? new FakeSubmissionsGateway(),
            settingsGateway,
            gradeReadGateway);
    }

    private static AssignmentSubmissionRecord Submitted(
        string submissionId,
        string userId,
        DateTimeOffset modifiedAt,
        string? gradingStatus)
    {
        return new AssignmentSubmissionRecord(
            submissionId,
            userId,
            "submitted",
            gradingStatus,
            CreatedAt: modifiedAt.AddMinutes(-5),
            ModifiedAt: modifiedAt,
            AttemptNumber: 0,
            FileCount: 1,
            HasOnlineText: true);
    }

    private sealed class FakeCoursesGateway : IMoodleCoursesGateway
    {
        public Task<PagedCourses> GetMyCoursesAsync(string userExternalId, int limit, int page, CancellationToken cancellationToken) { throw new NotSupportedException(); }

        public Task<IReadOnlyList<CourseSummary>> SearchMyCoursesAsync(
            string userExternalId,
            string query,
            int limit,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CourseSummary?> GetMyCourseAsync(
            string userExternalId,
            string courseId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<CourseSummary?>(new CourseSummary(
                "123",
                "ID-123",
                "CURSO-1",
                "Curso 1",
                "Curso 1",
                10,
                "Categoria",
                null,
                null,
                true,
                null,
                null,
                null,
                null,
                null,
                null));
        }
    }

    private sealed class FakeContentsGateway : IMoodleCourseContentsGateway
    {
        public bool ReturnWithoutAssignment { get; init; }

        public Task<CourseContentsSummary> GetCourseContentsAsync(
            string userExternalId,
            string courseId,
            IReadOnlyCollection<string> moduleTypes,
            bool includeHidden,
            bool onlyWithFiles,
            CancellationToken cancellationToken)
        {
            var modules = ReturnWithoutAssignment
                ? Array.Empty<CourseModuleSummary>()
                :
                [
                    new CourseModuleSummary(
                        "11",
                        "501",
                        "assign",
                        "Tarefa 1",
                        "https://moodle.example/mod/assign/view.php?id=11",
                        true,
                        true,
                        "Descricao",
                        null,
                        [new CourseModuleDate("Entrega ate", DueAt)],
                        [])
                ];

            return Task.FromResult(new CourseContentsSummary(
                courseId,
                moduleTypes.ToArray(),
                includeHidden,
                onlyWithFiles,
                [new CourseSectionSummary("1", 1, "Topico 1", null, true, modules.Length, modules.Length == 0, modules)]));
        }
    }

    private sealed class FakeParticipantsGateway : IMoodleParticipantsGateway
    {
        private static readonly CourseParticipantSummary[] Participants =
        [
            Participant("101", "Ana Souza"),
            Participant("102", "Bruno Lima"),
            Participant("103", "Carla Dias")
        ];

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
            var pageItems = Participants
                .Skip((Math.Max(1, page) - 1) * pageSize)
                .Take(pageSize)
                .ToArray();
            var hasMore = page * pageSize < Participants.Length;

            return Task.FromResult(new CourseParticipantsPage(
                courseId,
                page,
                pageSize,
                statusFilter,
                studentsOnly,
                includeEmail,
                hasMore,
                pageItems));
        }

        public Task<IReadOnlyList<CourseGroupSummary>> GetCourseGroupsAsync(
            string userExternalId,
            string courseId,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        private static CourseParticipantSummary Participant(string userId, string fullName)
        {
            return new CourseParticipantSummary(
                userId,
                fullName,
                Email: null,
                Suspended: false,
                FirstAccessAt: null,
                LastAccessAt: null,
                LastCourseAccessAt: null,
                Roles: [],
                Groups: []);
        }
    }

    private sealed class FakeSubmissionsGateway : IMoodleAssignmentSubmissionsGateway
    {
        public IReadOnlyList<AssignmentSubmissionRecord> Records { get; init; } = [];

        public bool WasCalled { get; private set; }

        public string LastAssignmentId { get; private set; } = string.Empty;

        public string? LastStatus { get; private set; }

        public Task<IReadOnlyList<AssignmentSubmissionRecord>> GetAssignmentSubmissionsAsync(
            string userExternalId,
            string assignmentId,
            string? status,
            DateTimeOffset? since,
            DateTimeOffset? before,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            LastAssignmentId = assignmentId;
            LastStatus = status;
            return Task.FromResult(Records);
        }
    }

    private sealed class FakeAssignmentSettingsGateway : IMoodleAssignmentSettingsGateway
    {
        public AssignmentSettingsSummary? Settings { get; init; }

        public Task<AssignmentSettingsSummary?> GetAssignmentSettingsAsync(
            string userExternalId,
            string courseId,
            string assignmentId,
            CancellationToken cancellationToken) => Task.FromResult(Settings);
    }

    private sealed class FakeAssignmentGradeReadGateway : IMoodleAssignmentGradeReadGateway
    {
        public IReadOnlyDictionary<string, AssignmentExistingGrade> Grades { get; init; } =
            new Dictionary<string, AssignmentExistingGrade>(StringComparer.OrdinalIgnoreCase);

        public Task<AssignmentExistingGrade?> GetExistingGradeAsync(
            string userExternalId,
            string assignmentId,
            string studentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Grades.GetValueOrDefault(studentId));

        public Task<IReadOnlyDictionary<string, AssignmentExistingGrade>> GetExistingGradesAsync(
            string userExternalId,
            string assignmentId,
            IReadOnlyCollection<string> studentIds,
            CancellationToken cancellationToken) =>
            Task.FromResult(Grades);
    }
}
