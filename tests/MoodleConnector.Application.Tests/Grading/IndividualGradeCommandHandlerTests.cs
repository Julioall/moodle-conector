using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.Grading;
using MoodleConnector.Application.PendingActions;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Tests.Grading;

public sealed class IndividualGradeCommandHandlerTests
{
    private sealed class FakeGradeReadGateway : IMoodleAssignmentGradeReadGateway
    {
        public Task<AssignmentExistingGrade?> GetExistingGradeAsync(string userExternalId, string assignmentId, string studentId, CancellationToken cancellationToken)
            => Task.FromResult<AssignmentExistingGrade?>(new AssignmentExistingGrade(assignmentId, studentId, null, false));
    }

    private sealed class FakeCurrentUserIdGateway : IMoodleCurrentUserIdGateway
    {
        public Task<long> GetCurrentUserIdAsync(CancellationToken cancellationToken) => Task.FromResult(42L);
    }

    private sealed class FakeParticipantsGateway : IMoodleParticipantsGateway
    {
        public Task<CourseParticipantsPage> GetCourseParticipantsAsync(string userExternalId, string courseId, ParticipantStatusFilter statusFilter, int page, int pageSize, bool studentsOnly, bool includeEmail, string? groupId, CancellationToken cancellationToken)
            => Task.FromResult(new CourseParticipantsPage(courseId, page, pageSize, statusFilter, studentsOnly, includeEmail, false, []));

        public Task<IReadOnlyList<CourseGroupSummary>> GetCourseGroupsAsync(string userExternalId, string courseId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<CourseGroupSummary>>([]);
    }

    private sealed class FakePendingActionService : IPendingActionService
    {
        public Guid ActionIdToReturn { get; set; } = Guid.NewGuid();
        
        public Task<PendingActionResponse> CreatePendingActionAsync(string toolName, ToolRiskLevel riskLevel, object payload, object preview, string confirmationText, TimeSpan expiresIn, long? courseId, CancellationToken cancellationToken)
            => Task.FromResult(new PendingActionResponse("pending", ActionIdToReturn, toolName, riskLevel, preview, confirmationText, DateTimeOffset.UtcNow.Add(expiresIn)));
    }

    private sealed class FakeAssignmentSettingsGateway : IMoodleAssignmentSettingsGateway
    {
        public Task<AssignmentSettingsSummary?> GetAssignmentSettingsAsync(
            string userExternalId,
            string courseId,
            string assignmentId,
            CancellationToken cancellationToken)
            => Task.FromResult<AssignmentSettingsSummary?>(new AssignmentSettingsSummary(assignmentId, 10m));
    }

    private PrepareIndividualGradeCommandHandler CreatePrepareHandler(bool gradeWriteEnabled = true)
    {
        var options = Options.Create(new AssignmentWriteFeatureOptions { AssignmentGradeWriteEnabled = gradeWriteEnabled });
        return new PrepareIndividualGradeCommandHandler(
            new FakeGradeReadGateway(),
            new FakeCurrentUserIdGateway(),
            new FakeParticipantsGateway(),
            new FakePendingActionService(),
            options,
            new FakeAssignmentSettingsGateway());
    }

    [Fact]
    public async Task Prepare_ThrowsInvalidOperationException_WhenFeatureDisabled()
    {
        var sut = CreatePrepareHandler(gradeWriteEnabled: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.Handle(new PrepareIndividualGradeCommand("1", "2", "3", 10, "Bom", "Avaliação ok"), CancellationToken.None));

        Assert.Contains("desabilitado", exception.Message);
    }

    [Fact]
    public async Task Prepare_ThrowsArgumentException_WhenRequiredInputsAreMissing()
    {
        var sut = CreatePrepareHandler();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.Handle(new PrepareIndividualGradeCommand("", "2", "3", 10, "Bom", "Justif"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => sut.Handle(new PrepareIndividualGradeCommand("1", "", "3", 10, "Bom", "Justif"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => sut.Handle(new PrepareIndividualGradeCommand("1", "2", "", 10, "Bom", "Justif"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => sut.Handle(new PrepareIndividualGradeCommand("1", "2", "3", 10, "Bom", ""), CancellationToken.None));
    }

    [Fact]
    public async Task Prepare_ThrowsArgumentOutOfRangeException_WhenGradeIsNegative()
    {
        var sut = CreatePrepareHandler();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            sut.Handle(new PrepareIndividualGradeCommand("1", "2", "3", -1, "Bom", "Avaliação ok"), CancellationToken.None));
    }

    [Fact]
    public async Task Prepare_Succeeds_WhenValidInputs()
    {
        var sut = CreatePrepareHandler();
        var result = await sut.Handle(new PrepareIndividualGradeCommand("10", "20", "30", 8.5m, "Ótimo", "Avaliado via rubrica"), CancellationToken.None);

        Assert.Equal("pending", result.Status);
        Assert.Equal(8.5m, result.Preview.ProposedGrade);
        Assert.Contains("8.50", result.Preview.ConfirmationText); // F2 format
    }
}
