using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Messages;
using MoodleConnector.Application.PendingActions;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Tests.Messages;

public sealed class PrepareTutorMessageCommandHandlerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CourseParticipantSummary MakeStudent(string id, string name) =>
        new(UserId: id, FullName: name, Email: null, Suspended: false,
            FirstAccessAt: null, LastAccessAt: null, LastCourseAccessAt: null,
            Roles: [], Groups: []);

    private static PrepareTutorMessageCommandHandler CreateHandler(
        IReadOnlyList<CourseParticipantSummary>? participants = null)
    {
        return new PrepareTutorMessageCommandHandler(
            new FakeParticipantsGateway(participants ?? []),
            new FakeCurrentUserGateway(),
            new FakePendingActionService());
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TutorMessageType.BoasVindas)]
    [InlineData(TutorMessageType.CobrancaAcesso)]
    [InlineData(TutorMessageType.CobrancaSa)]
    [InlineData(TutorMessageType.Encerramento)]
    [InlineData(TutorMessageType.Recuperacao)]
    [InlineData(TutorMessageType.Acompanhamento)]
    public async Task Handle_AllMessageTypes_ReturnValidPreview(TutorMessageType type)
    {
        var sut = CreateHandler([MakeStudent("1", "Alice")]);

        var result = await sut.Handle(
            new PrepareTutorMessageCommand("10", type, ["1"]),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(type.ToString(), result.MessageType);
        Assert.Equal("10", result.CourseId);
        Assert.Equal(1, result.RecipientCount);
        Assert.NotEmpty(result.MessageText);
        Assert.NotEmpty(result.ConfirmationText);
        Assert.NotEmpty(result.Risks);
        Assert.True(result.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Handle_WithCustomText_UsesCustomTextInPreview()
    {
        var sut = CreateHandler([MakeStudent("1", "Alice")]);

        var result = await sut.Handle(
            new PrepareTutorMessageCommand("10", TutorMessageType.Acompanhamento, ["1"],
                CustomText: "Texto customizado de teste."),
            CancellationToken.None);

        Assert.Equal("Texto customizado de teste.", result.MessageText);
    }

    [Fact]
    public async Task Handle_ThrowsArgumentException_WhenNoRecipients()
    {
        var sut = CreateHandler();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.Handle(
                new PrepareTutorMessageCommand("10", TutorMessageType.BoasVindas, []),
                CancellationToken.None));
    }

    [Fact]
    public async Task Handle_RecoveryMessage_HasAdditionalRisk()
    {
        var sut = CreateHandler([MakeStudent("1", "Alice")]);

        var result = await sut.Handle(
            new PrepareTutorMessageCommand("10", TutorMessageType.Recuperacao, ["1"]),
            CancellationToken.None);

        // Recuperacao type should have one extra risk about the recovery activity
        Assert.True(result.Risks.Count > 3);
        Assert.Contains(result.Risks, r => r.Contains("recuperação", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Handle_ConfirmationTextContainsMessageTypeAndCount()
    {
        var sut = CreateHandler([MakeStudent("1", "Alice"), MakeStudent("2", "Bob")]);

        var result = await sut.Handle(
            new PrepareTutorMessageCommand("10", TutorMessageType.BoasVindas, ["1", "2"]),
            CancellationToken.None);

        Assert.Contains("BOASVINDAS", result.ConfirmationText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2", result.ConfirmationText);
    }

    [Fact]
    public async Task Handle_WhenParticipantsGatewayFails_FallsBackToIdOnlyRecipients()
    {
        // Arrange: gateway throws
        var sut = new PrepareTutorMessageCommandHandler(
            new ThrowingParticipantsGateway(),
            new FakeCurrentUserGateway(),
            new FakePendingActionService());

        var result = await sut.Handle(
            new PrepareTutorMessageCommand("10", TutorMessageType.BoasVindas, ["999"]),
            CancellationToken.None);

        // Should not throw — falls back to ID-only recipient preview
        Assert.Single(result.Recipients);
        Assert.Equal("999", result.Recipients[0].StudentId);
        Assert.Contains("999", result.Recipients[0].FullName);
    }

    // ── Fakes ─────────────────────────────────────────────────────────────────

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

    private sealed class ThrowingParticipantsGateway : IMoodleParticipantsGateway
    {
        public Task<CourseParticipantsPage> GetCourseParticipantsAsync(
            string userExternalId, string courseId, ParticipantStatusFilter statusFilter,
            int page, int pageSize, bool studentsOnly, bool includeEmail, string? groupId,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("Simulated gateway error");

        public Task<IReadOnlyList<CourseGroupSummary>> GetCourseGroupsAsync(
            string userExternalId, string courseId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CourseGroupSummary>>([]);
    }

    private sealed class FakeCurrentUserGateway : IMoodleCurrentUserIdGateway
    {
        public Task<long> GetCurrentUserIdAsync(CancellationToken cancellationToken) =>
            Task.FromResult(42L);
    }

    private sealed class FakePendingActionService : IPendingActionService
    {
        public Task<PendingActionResponse> CreatePendingActionAsync(
            string toolName, ToolRiskLevel riskLevel, object payload,
            object preview, string confirmationText, TimeSpan expiresIn,
            long? courseId, CancellationToken cancellationToken)
        {
            var response = new PendingActionResponse(
                Status: "pending",
                PendingActionId: Guid.NewGuid(),
                ToolName: toolName,
                RiskLevel: riskLevel,
                Preview: preview,
                ConfirmationText: confirmationText,
                ExpiresAt: DateTimeOffset.UtcNow.Add(expiresIn));
            return Task.FromResult(response);
        }
    }
}
