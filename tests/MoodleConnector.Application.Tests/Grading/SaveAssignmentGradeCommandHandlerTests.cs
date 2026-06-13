using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.Grading;

namespace MoodleConnector.Application.Tests.Grading;

public sealed class SaveAssignmentGradeCommandHandlerTests
{
    [Fact]
    public async Task Handle_EnviaNotaIndividualParaGateway()
    {
        var gateway = new FakeMoodleAssignmentGradingGateway();
        var sut = CreateHandler(gateway);

        var result = await sut.Handle(
            new SaveAssignmentGradeCommand(
                "321",
                "501",
                "101",
                8.5m,
                "Bom trabalho. Revise apenas a conclusao.",
                AttemptNumber: -1,
                AddAttempt: false,
                ApplyToAll: false,
                WorkflowState: "graded"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(gateway.LastRequest);
        Assert.Equal("321", gateway.LastUserExternalId);
        Assert.Equal("501", gateway.LastRequest!.AssignmentId);
        Assert.Equal("101", gateway.LastRequest.StudentId);
        Assert.Equal(8.5m, gateway.LastRequest.Grade);
        Assert.Equal("Bom trabalho. Revise apenas a conclusao.", gateway.LastRequest.FeedbackText);
        Assert.Equal("mod_assign_save_grade", result.MoodleFunction);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_RejeitaIdentificadorDeTarefaVazio(string assignmentId)
    {
        var gateway = new FakeMoodleAssignmentGradingGateway();
        var sut = CreateHandler(gateway);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.Handle(
                new SaveAssignmentGradeCommand(
                    "321",
                    assignmentId,
                    "101",
                    8.5m,
                    "Feedback revisado.",
                    AttemptNumber: -1,
                    AddAttempt: false,
                    ApplyToAll: false,
                    WorkflowState: "graded"),
                CancellationToken.None));

        Assert.Equal("assignmentId", ex.ParamName);
        Assert.Null(gateway.LastRequest);
    }

    [Fact]
    public async Task Handle_RejeitaNotaNegativa()
    {
        var gateway = new FakeMoodleAssignmentGradingGateway();
        var sut = CreateHandler(gateway);

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            sut.Handle(
                new SaveAssignmentGradeCommand(
                    "321",
                    "501",
                    "101",
                    -1m,
                    "Feedback revisado.",
                    AttemptNumber: -1,
                    AddAttempt: false,
                    ApplyToAll: false,
                    WorkflowState: "graded"),
                CancellationToken.None));

        Assert.Equal("grade", ex.ParamName);
        Assert.Null(gateway.LastRequest);
    }

    [Fact]
    public async Task Handle_BloqueiaQuandoFlagDeNotaEstaDesabilitada()
    {
        var gateway = new FakeMoodleAssignmentGradingGateway();
        var sut = CreateHandler(
            gateway,
            assignmentGradeWriteEnabled: false,
            assignmentFeedbackWriteEnabled: true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.Handle(
                new SaveAssignmentGradeCommand(
                    "321",
                    "501",
                    "101",
                    8.5m,
                    "Feedback revisado.",
                    AttemptNumber: -1,
                    AddAttempt: false,
                    ApplyToAll: false,
                    WorkflowState: "graded"),
                CancellationToken.None));

        Assert.Equal("A escrita de notas em tarefas esta desabilitada por feature flag.", ex.Message);
        Assert.Null(gateway.LastRequest);
    }

    [Fact]
    public async Task Handle_BloqueiaFeedbackQuandoFlagDeFeedbackEstaDesabilitada()
    {
        var gateway = new FakeMoodleAssignmentGradingGateway();
        var sut = CreateHandler(
            gateway,
            assignmentGradeWriteEnabled: true,
            assignmentFeedbackWriteEnabled: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.Handle(
                new SaveAssignmentGradeCommand(
                    "321",
                    "501",
                    "101",
                    8.5m,
                    "Feedback revisado.",
                    AttemptNumber: -1,
                    AddAttempt: false,
                    ApplyToAll: false,
                    WorkflowState: "graded"),
                CancellationToken.None));

        Assert.Equal("A escrita de feedback em tarefas esta desabilitada por feature flag.", ex.Message);
        Assert.Null(gateway.LastRequest);
    }

    private static SaveAssignmentGradeCommandHandler CreateHandler(
        FakeMoodleAssignmentGradingGateway gateway,
        bool assignmentGradeWriteEnabled = true,
        bool assignmentFeedbackWriteEnabled = true)
    {
        return new SaveAssignmentGradeCommandHandler(
            gateway,
            Options.Create(new AssignmentWriteFeatureOptions
            {
                AssignmentGradeWriteEnabled = assignmentGradeWriteEnabled,
                AssignmentFeedbackWriteEnabled = assignmentFeedbackWriteEnabled
            }));
    }

    private sealed class FakeMoodleAssignmentGradingGateway : IMoodleAssignmentGradingGateway
    {
        public string? LastUserExternalId { get; private set; }

        public AssignmentGradeWriteRequest? LastRequest { get; private set; }

        public Task<AssignmentGradeWriteResult> SaveGradeAsync(
            string userExternalId,
            AssignmentGradeWriteRequest request,
            CancellationToken cancellationToken)
        {
            LastUserExternalId = userExternalId;
            LastRequest = request;
            return Task.FromResult(new AssignmentGradeWriteResult(
                Success: true,
                MoodleFunction: "mod_assign_save_grade",
                MoodleStatus: "ok"));
        }
    }
}
