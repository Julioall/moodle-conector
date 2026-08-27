using System.Text.Json;
using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;
using MoodleConnector.Application.PendingActions;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Tests.Grading;

public sealed class GradingLaunchCommandHandlerTests
{
    [Fact]
    public async Task CreatePreview_CriaPendingActionComItensRevisados()
    {
        var fixture = new Fixture();
        var batch = fixture.CreateBatchWithReviewedItem();
        var sut = new CreateGradingLaunchPreviewCommandHandler(
            fixture.GradingRepository,
            fixture.PendingActions,
            fixture.CurrentUser,
            fixture.SettingsGateway);

        var result = await sut.Handle(
            new CreateGradingLaunchPreviewCommand(
                batch.Id,
                GradingItemIds: [],
                OnlyReviewed: true),
            CancellationToken.None);

        Assert.Equal(fixture.PendingActions.PendingActionId, result.PendingActionId);
        Assert.Equal(batch.Id, result.BatchJobId);
        Assert.Equal(1, result.ReadyItems);
        Assert.Equal(0, result.BlockedItems);
        Assert.StartsWith("CONFIRMO O LANCAMENTO DE 1 CORRECAO NO MOODLE PARA O LOTE", result.ConfirmationText, StringComparison.Ordinal);
        Assert.Contains(batch.Id.ToString(), result.ConfirmationText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CURSO 10", result.ConfirmationText, StringComparison.Ordinal);
        Assert.NotNull(fixture.PendingActions.LastPayload);
        Assert.Single(fixture.PendingActions.LastPayload!.Items);
        Assert.Equal("501", fixture.PendingActions.LastPayload.Items[0].AssignmentId);
        Assert.Equal("101", fixture.PendingActions.LastPayload.Items[0].StudentId);
        Assert.Equal(8.5m, fixture.PendingActions.LastPayload.Items[0].Grade);
        Assert.Equal("Feedback final revisado.", fixture.PendingActions.LastPayload.Items[0].FeedbackText);
        Assert.False(string.IsNullOrWhiteSpace(fixture.PendingActions.LastPayload.Items[0].DraftVersionHash));
        Assert.Equal(fixture.GradingRepository.Items.Single().ContextHash, fixture.PendingActions.LastPayload.Items[0].ContextHash);
    }

    [Fact]
    public async Task CreatePreview_BloqueiaItemSemRevisao()
    {
        var fixture = new Fixture();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        item.SetDraft(8m, 0.8m, "Rascunho.");
        await fixture.GradingRepository.AddBatchAsync(batch, CancellationToken.None);
        await fixture.GradingRepository.AddItemAsync(item, CancellationToken.None);
        var sut = new CreateGradingLaunchPreviewCommandHandler(
            fixture.GradingRepository,
            fixture.PendingActions,
            fixture.CurrentUser,
            fixture.SettingsGateway);

        var result = await sut.Handle(
            new CreateGradingLaunchPreviewCommand(
                batch.Id,
                GradingItemIds: [],
                OnlyReviewed: true),
            CancellationToken.None);

        Assert.Equal(Guid.Empty, result.PendingActionId);
        Assert.Equal(0, result.ReadyItems);
        Assert.Equal(1, result.BlockedItems);
        Assert.Null(fixture.PendingActions.LastPayload);
        Assert.Contains(result.Warnings, warning => warning.Contains("Nenhum item revisado", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePreview_BloqueiaNotaFinalAcimaDaNotaMaximaDasEvidencias()
    {
        var fixture = new Fixture();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        item.SetDraft(8m, 0.8m, "Rascunho.");
        item.ApplyTeacherReview(12m, "Feedback final revisado.", "teacher-1", 321, "approved", "ok");
        AttachVersionedContext(item, batch);
        await fixture.GradingRepository.AddBatchAsync(batch, CancellationToken.None);
        await fixture.GradingRepository.AddItemAsync(item, CancellationToken.None);
        await fixture.GradingRepository.AddEvidenceAsync(
            new GradingEvidence(
                Guid.NewGuid(),
                item.Id,
                "c1",
                "Criterio 1",
                MaxPoints: 5m,
                SuggestedPoints: 4m,
                EvidenceText: "Evidencia.",
                GapsText: null,
                TeacherReviewRequired: false,
                CreatedAt: DateTimeOffset.UtcNow),
            CancellationToken.None);
        await fixture.GradingRepository.AddEvidenceAsync(
            new GradingEvidence(
                Guid.NewGuid(),
                item.Id,
                "c2",
                "Criterio 2",
                MaxPoints: 5m,
                SuggestedPoints: 4m,
                EvidenceText: "Evidencia.",
                GapsText: null,
                TeacherReviewRequired: false,
                CreatedAt: DateTimeOffset.UtcNow),
            CancellationToken.None);
        var sut = new CreateGradingLaunchPreviewCommandHandler(
            fixture.GradingRepository,
            fixture.PendingActions,
            fixture.CurrentUser,
            fixture.SettingsGateway);

        var result = await sut.Handle(
            new CreateGradingLaunchPreviewCommand(
                batch.Id,
                GradingItemIds: [],
                OnlyReviewed: true),
            CancellationToken.None);

        Assert.Equal(Guid.Empty, result.PendingActionId);
        Assert.Equal(0, result.ReadyItems);
        Assert.Equal(1, result.BlockedItems);
        Assert.Null(fixture.PendingActions.LastPayload);
        Assert.Contains(result.Warnings, warning => warning.Contains("nota final 12", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("nota maxima 10", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePreview_BloqueiaItemLegadoSemContextoVersionado()
    {
        var fixture = new Fixture();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        item.SetDraft(8m, 0.8m, "Rascunho.");
        item.ApplyTeacherReview(8.5m, "Feedback final revisado.", "teacher-1", 321, "approved", "ok");
        await fixture.GradingRepository.AddBatchAsync(batch, CancellationToken.None);
        await fixture.GradingRepository.AddItemAsync(item, CancellationToken.None);

        var sut = new CreateGradingLaunchPreviewCommandHandler(
            fixture.GradingRepository,
            fixture.PendingActions,
            fixture.CurrentUser,
            fixture.SettingsGateway);

        var result = await sut.Handle(
            new CreateGradingLaunchPreviewCommand(batch.Id, [], OnlyReviewed: true),
            CancellationToken.None);

        Assert.Equal(Guid.Empty, result.PendingActionId);
        Assert.Equal(0, result.ReadyItems);
        Assert.Equal(1, result.BlockedItems);
        Assert.Null(fixture.PendingActions.LastPayload);
        Assert.Contains(result.Warnings, warning => warning.Contains("contexto de correcao", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreatePreview_DeOutroCriadorSemEscopoAdmin_DeveFalhar()
    {
        var fixture = new Fixture(currentUserSubject: "teacher-2");
        var batch = fixture.CreateBatchWithReviewedItem();
        var sut = new CreateGradingLaunchPreviewCommandHandler(
            fixture.GradingRepository,
            fixture.PendingActions,
            fixture.CurrentUser,
            fixture.SettingsGateway);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.Handle(
                new CreateGradingLaunchPreviewCommand(
                    batch.Id,
                    GradingItemIds: [],
                    OnlyReviewed: true),
                CancellationToken.None));

        Assert.Equal("Usuario atual nao esta autorizado a acessar este lote de correcao.", ex.Message);
        Assert.Null(fixture.PendingActions.LastPayload);
    }

    [Fact]
    public async Task ConfirmLaunch_ConfirmaAcaoEEnviaNotasIndividuais()
    {
        var fixture = new Fixture();
        var batch = fixture.CreateBatchWithReviewedItem();
        var item = fixture.GradingRepository.Items.Single();
        var pendingAction = fixture.CreatePendingLaunchAction(batch.Id, item.Id);
        fixture.PendingRepository.Actions.Add(pendingAction);
        var sut = new ConfirmMoodleBatchLaunchCommandHandler(
            fixture.PendingRepository,
            fixture.GradingRepository,
            fixture.Confirmations,
            fixture.Capabilities,
            fixture.ExistingGrades,
            fixture.SubmissionStatuses,
            fixture.EnrollmentGateway,
            fixture.AuditLogs,
            fixture.Mediator);

        var result = await sut.Handle(
            new ConfirmMoodleBatchLaunchCommand(
                pendingAction.Id,
                "CONFIRMAR LANCAMENTO 1 ITEM"),
            CancellationToken.None);

        Assert.Equal("confirmed", result.Status);
        Assert.Equal(1, result.SentItems);
        Assert.Equal(0, result.FailedItems);
        Assert.Single(fixture.Mediator.SavedGrades);
        Assert.Equal("501", fixture.Mediator.SavedGrades[0].AssignmentId);
        Assert.Equal("101", fixture.Mediator.SavedGrades[0].StudentId);
        Assert.Equal(GradingCommitStatus.Succeeded, item.CommitStatus);
        Assert.Equal("moodle.write.assignments.grade", fixture.Confirmations.LastRequiredScope);
        var auditLog = Assert.Single(fixture.AuditLogs.Logs, log => log.Status == "commit_succeeded");
        Assert.Equal("confirmar_lancamento_lote_moodle", auditLog.ToolName);
        Assert.Equal("mod_assign_save_grade", auditLog.MoodleFunction);
        Assert.Equal("audit-1", auditLog.CorrelationId);
        Assert.Contains(item.Id.ToString(), auditLog.RequestSanitizedJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmLaunch_Repetido_NaoReenviaItemJaEnviado()
    {
        var fixture = new Fixture();
        var batch = fixture.CreateBatchWithReviewedItem();
        var item = fixture.GradingRepository.Items.Single();
        item.MarkCommitSucceeded();
        var pendingAction = fixture.CreatePendingLaunchAction(batch.Id, item.Id);
        fixture.PendingRepository.Actions.Add(pendingAction);
        var sut = new ConfirmMoodleBatchLaunchCommandHandler(
            fixture.PendingRepository,
            fixture.GradingRepository,
            fixture.Confirmations,
            fixture.Capabilities,
            fixture.ExistingGrades,
            fixture.SubmissionStatuses,
            fixture.EnrollmentGateway,
            fixture.AuditLogs,
            fixture.Mediator);

        var result = await sut.Handle(
            new ConfirmMoodleBatchLaunchCommand(
                pendingAction.Id,
                "CONFIRMAR LANCAMENTO 1 ITEM"),
            CancellationToken.None);

        Assert.Equal(0, result.SentItems);
        Assert.Empty(fixture.Mediator.SavedGrades);
    }

    [Fact]
    public async Task ConfirmLaunch_BloqueiaPreviaLegadaSemContextoVersionado()
    {
        var fixture = new Fixture();
        var batch = fixture.CreateBatchWithReviewedItem(includeContext: false);
        var item = fixture.GradingRepository.Items.Single();
        var pendingAction = fixture.CreatePendingLaunchAction(batch.Id, item.Id);
        fixture.PendingRepository.Actions.Add(pendingAction);
        var sut = new ConfirmMoodleBatchLaunchCommandHandler(
            fixture.PendingRepository,
            fixture.GradingRepository,
            fixture.Confirmations,
            fixture.Capabilities,
            fixture.ExistingGrades,
            fixture.SubmissionStatuses,
            fixture.EnrollmentGateway,
            fixture.AuditLogs,
            fixture.Mediator);

        var result = await sut.Handle(
            new ConfirmMoodleBatchLaunchCommand(pendingAction.Id, "CONFIRMAR LANCAMENTO 1 ITEM"),
            CancellationToken.None);

        Assert.Equal(0, result.SentItems);
        Assert.Equal(1, result.FailedItems);
        Assert.Empty(fixture.Mediator.SavedGrades);
        Assert.Equal("grading_context_missing", Assert.Single(fixture.AuditLogs.Logs, log => log.Status == "commit_blocked").ErrorCode);
    }

    [Fact]
    public async Task ConfirmLaunch_BloqueiaPreviaComContextoDivergente()
    {
        var fixture = new Fixture();
        var batch = fixture.CreateBatchWithReviewedItem();
        var item = fixture.GradingRepository.Items.Single();
        var pendingAction = fixture.CreatePendingLaunchAction(batch.Id, item.Id, contextHash: "hash-contexto-antigo");
        fixture.PendingRepository.Actions.Add(pendingAction);
        var sut = new ConfirmMoodleBatchLaunchCommandHandler(
            fixture.PendingRepository,
            fixture.GradingRepository,
            fixture.Confirmations,
            fixture.Capabilities,
            fixture.ExistingGrades,
            fixture.SubmissionStatuses,
            fixture.EnrollmentGateway,
            fixture.AuditLogs,
            fixture.Mediator);

        var result = await sut.Handle(
            new ConfirmMoodleBatchLaunchCommand(pendingAction.Id, "CONFIRMAR LANCAMENTO 1 ITEM"),
            CancellationToken.None);

        Assert.Equal(0, result.SentItems);
        Assert.Equal(1, result.FailedItems);
        Assert.Empty(fixture.Mediator.SavedGrades);
        Assert.Equal("grading_context_hash_mismatch", Assert.Single(fixture.AuditLogs.Logs, log => log.Status == "commit_blocked").ErrorCode);
    }

    [Fact]
    public async Task ConfirmLaunch_ComPreviaObsoleta_NaoEnviaNota()
    {
        var fixture = new Fixture();
        var batch = fixture.CreateBatchWithReviewedItem();
        var item = fixture.GradingRepository.Items.Single();
        var pendingAction = fixture.CreatePendingLaunchAction(
            batch.Id,
            item.Id,
            draftVersionHash: "hash-antigo");
        fixture.PendingRepository.Actions.Add(pendingAction);
        var sut = new ConfirmMoodleBatchLaunchCommandHandler(
            fixture.PendingRepository,
            fixture.GradingRepository,
            fixture.Confirmations,
            fixture.Capabilities,
            fixture.ExistingGrades,
            fixture.SubmissionStatuses,
            fixture.EnrollmentGateway,
            fixture.AuditLogs,
            fixture.Mediator);

        var result = await sut.Handle(
            new ConfirmMoodleBatchLaunchCommand(
                pendingAction.Id,
                "CONFIRMAR LANCAMENTO 1 ITEM"),
            CancellationToken.None);

        Assert.Equal(0, result.SentItems);
        Assert.Equal(1, result.FailedItems);
        Assert.Empty(fixture.Mediator.SavedGrades);
        Assert.Equal(GradingCommitStatus.Failed, item.CommitStatus);
        Assert.Contains("rascunho foi alterado", result.Failures[0].Message, StringComparison.OrdinalIgnoreCase);
        var auditLog = Assert.Single(fixture.AuditLogs.Logs, log => log.Status == "commit_blocked");
        Assert.Equal("grading_draft_version_mismatch", auditLog.ErrorCode);
    }

    [Fact]
    public async Task ConfirmLaunch_BloqueiaQuandoJaExisteNotaNoMoodle()
    {
        var fixture = new Fixture();
        var batch = fixture.CreateBatchWithReviewedItem();
        var item = fixture.GradingRepository.Items.Single();
        fixture.ExistingGrades.ExistingGrades.Add(new AssignmentExistingGrade(
            AssignmentId: "501",
            StudentId: "101",
            Grade: 7.5m,
            HasGrade: true));
        var pendingAction = fixture.CreatePendingLaunchAction(batch.Id, item.Id);
        fixture.PendingRepository.Actions.Add(pendingAction);
        var sut = new ConfirmMoodleBatchLaunchCommandHandler(
            fixture.PendingRepository,
            fixture.GradingRepository,
            fixture.Confirmations,
            fixture.Capabilities,
            fixture.ExistingGrades,
            fixture.SubmissionStatuses,
            fixture.EnrollmentGateway,
            fixture.AuditLogs,
            fixture.Mediator);

        var result = await sut.Handle(
            new ConfirmMoodleBatchLaunchCommand(
                pendingAction.Id,
                "CONFIRMAR LANCAMENTO 1 ITEM"),
            CancellationToken.None);

        Assert.Equal(0, result.SentItems);
        Assert.Equal(1, result.FailedItems);
        Assert.Empty(fixture.Mediator.SavedGrades);
        Assert.Equal(GradingCommitStatus.Failed, item.CommitStatus);
        Assert.Contains("nota existente", result.Failures[0].Message, StringComparison.OrdinalIgnoreCase);
        var auditLog = Assert.Single(fixture.AuditLogs.Logs, log => log.Status == "commit_blocked");
        Assert.Equal("moodle_existing_grade", auditLog.ErrorCode);
    }

    [Fact]
    public async Task ConfirmLaunch_SobrescreveNotaExistenteQuandoAutorizadoNaPrevia()
    {
        var fixture = new Fixture();
        var batch = fixture.CreateBatchWithReviewedItem();
        var item = fixture.GradingRepository.Items.Single();
        fixture.ExistingGrades.ExistingGrades.Add(new AssignmentExistingGrade("501", "101", 7.5m, true));
        fixture.SubmissionStatuses.Statuses.Add(new AssignmentSubmissionAttemptStatus("501", "101", 0, "submitted", HasFeedback: true));
        var pendingAction = fixture.CreatePendingLaunchAction(batch.Id, item.Id, allowOverwriteExisting: true);
        fixture.PendingRepository.Actions.Add(pendingAction);
        var sut = new ConfirmMoodleBatchLaunchCommandHandler(
            fixture.PendingRepository, fixture.GradingRepository, fixture.Confirmations,
            fixture.Capabilities, fixture.ExistingGrades, fixture.SubmissionStatuses,
            fixture.EnrollmentGateway, fixture.AuditLogs, fixture.Mediator);

        var result = await sut.Handle(
            new ConfirmMoodleBatchLaunchCommand(pendingAction.Id, "CONFIRMAR LANCAMENTO 1 ITEM"),
            CancellationToken.None);

        Assert.Equal(1, result.SentItems);
        Assert.Equal(0, result.FailedItems);
        Assert.Single(fixture.Mediator.SavedGrades);
        Assert.Equal(GradingCommitStatus.Succeeded, item.CommitStatus);
    }

    [Fact]
    public async Task ConfirmLaunch_BloqueiaQuandoNaoConsegueValidarNotaExistente()
    {
        var fixture = new Fixture();
        var batch = fixture.CreateBatchWithReviewedItem();
        var item = fixture.GradingRepository.Items.Single();
        fixture.ExistingGrades.Error = new InvalidOperationException("mod_assign_get_grades indisponivel");
        var pendingAction = fixture.CreatePendingLaunchAction(batch.Id, item.Id);
        fixture.PendingRepository.Actions.Add(pendingAction);
        var sut = new ConfirmMoodleBatchLaunchCommandHandler(
            fixture.PendingRepository,
            fixture.GradingRepository,
            fixture.Confirmations,
            fixture.Capabilities,
            fixture.ExistingGrades,
            fixture.SubmissionStatuses,
            fixture.EnrollmentGateway,
            fixture.AuditLogs,
            fixture.Mediator);

        var result = await sut.Handle(
            new ConfirmMoodleBatchLaunchCommand(
                pendingAction.Id,
                "CONFIRMAR LANCAMENTO 1 ITEM"),
            CancellationToken.None);

        Assert.Equal(0, result.SentItems);
        Assert.Equal(1, result.FailedItems);
        Assert.Empty(fixture.Mediator.SavedGrades);
        Assert.Equal(GradingCommitStatus.Failed, item.CommitStatus);
        Assert.Contains("validar se ja existe nota", result.Failures[0].Message, StringComparison.OrdinalIgnoreCase);
        var auditLog = Assert.Single(fixture.AuditLogs.Logs, log => log.Status == "commit_blocked");
        Assert.Equal("moodle_existing_grade_validation_failed", auditLog.ErrorCode);
    }

    [Fact]
    public async Task ConfirmLaunch_BloqueiaQuandoTentativaDoMoodleMudou()
    {
        var fixture = new Fixture();
        var batch = fixture.CreateBatchWithReviewedItem();
        var item = fixture.GradingRepository.Items.Single();
        fixture.SubmissionStatuses.Statuses.Add(new AssignmentSubmissionAttemptStatus(
            AssignmentId: "501",
            StudentId: "101",
            AttemptNumber: 1,
            SubmissionStatus: "submitted"));
        var pendingAction = fixture.CreatePendingLaunchAction(batch.Id, item.Id);
        fixture.PendingRepository.Actions.Add(pendingAction);
        var sut = new ConfirmMoodleBatchLaunchCommandHandler(
            fixture.PendingRepository,
            fixture.GradingRepository,
            fixture.Confirmations,
            fixture.Capabilities,
            fixture.ExistingGrades,
            fixture.SubmissionStatuses,
            fixture.EnrollmentGateway,
            fixture.AuditLogs,
            fixture.Mediator);

        var result = await sut.Handle(
            new ConfirmMoodleBatchLaunchCommand(
                pendingAction.Id,
                "CONFIRMAR LANCAMENTO 1 ITEM"),
            CancellationToken.None);

        Assert.Equal(0, result.SentItems);
        Assert.Equal(1, result.FailedItems);
        Assert.Empty(fixture.Mediator.SavedGrades);
        Assert.Equal(GradingCommitStatus.Failed, item.CommitStatus);
        Assert.Contains("tentativa", result.Failures[0].Message, StringComparison.OrdinalIgnoreCase);
        var auditLog = Assert.Single(fixture.AuditLogs.Logs, log => log.Status == "commit_blocked");
        Assert.Equal("moodle_submission_attempt_mismatch", auditLog.ErrorCode);
    }

    [Fact]
    public async Task ConfirmLaunch_BloqueiaQuandoSubmissaoAtualNaoEstaEntregue()
    {
        var fixture = new Fixture();
        var batch = fixture.CreateBatchWithReviewedItem();
        var item = fixture.GradingRepository.Items.Single();
        fixture.SubmissionStatuses.Statuses.Add(new AssignmentSubmissionAttemptStatus(
            AssignmentId: "501",
            StudentId: "101",
            AttemptNumber: 0,
            SubmissionStatus: "new"));
        var pendingAction = fixture.CreatePendingLaunchAction(batch.Id, item.Id);
        fixture.PendingRepository.Actions.Add(pendingAction);
        var sut = new ConfirmMoodleBatchLaunchCommandHandler(
            fixture.PendingRepository,
            fixture.GradingRepository,
            fixture.Confirmations,
            fixture.Capabilities,
            fixture.ExistingGrades,
            fixture.SubmissionStatuses,
            fixture.EnrollmentGateway,
            fixture.AuditLogs,
            fixture.Mediator);

        var result = await sut.Handle(
            new ConfirmMoodleBatchLaunchCommand(
                pendingAction.Id,
                "CONFIRMAR LANCAMENTO 1 ITEM"),
            CancellationToken.None);

        Assert.Equal(0, result.SentItems);
        Assert.Equal(1, result.FailedItems);
        Assert.Empty(fixture.Mediator.SavedGrades);
        Assert.Equal(GradingCommitStatus.Failed, item.CommitStatus);
        Assert.Contains("submissao atual nao esta entregue", result.Failures[0].Message, StringComparison.OrdinalIgnoreCase);
        var auditLog = Assert.Single(fixture.AuditLogs.Logs, log => log.Status == "commit_blocked");
        Assert.Equal("moodle_submission_not_submitted", auditLog.ErrorCode);
    }

    [Fact]
    public async Task ConfirmLaunch_BloqueiaQuandoFuncaoMoodleDeEscritaNaoEstaDisponivel()
    {
        var fixture = new Fixture();
        fixture.Capabilities.Functions.Clear();
        fixture.Capabilities.Functions.Add("mod_assign_get_submissions");
        var batch = fixture.CreateBatchWithReviewedItem();
        var item = fixture.GradingRepository.Items.Single();
        var pendingAction = fixture.CreatePendingLaunchAction(batch.Id, item.Id);
        fixture.PendingRepository.Actions.Add(pendingAction);
        var sut = new ConfirmMoodleBatchLaunchCommandHandler(
            fixture.PendingRepository,
            fixture.GradingRepository,
            fixture.Confirmations,
            fixture.Capabilities,
            fixture.ExistingGrades,
            fixture.SubmissionStatuses,
            fixture.EnrollmentGateway,
            fixture.AuditLogs,
            fixture.Mediator);

        var result = await sut.Handle(
            new ConfirmMoodleBatchLaunchCommand(
                pendingAction.Id,
                "CONFIRMAR LANCAMENTO 1 ITEM"),
            CancellationToken.None);

        Assert.Equal("confirmed", result.Status);
        Assert.Equal(0, result.SentItems);
        Assert.Equal(1, result.FailedItems);
        Assert.Empty(fixture.Mediator.SavedGrades);
        Assert.Equal(GradingCommitStatus.Failed, item.CommitStatus);
        Assert.Contains("mod_assign_save_grade", result.Failures[0].Message, StringComparison.OrdinalIgnoreCase);
        var auditLog = Assert.Single(fixture.AuditLogs.Logs, log => log.Status == "commit_blocked");
        Assert.Equal("moodle_function_unavailable", auditLog.ErrorCode);
        Assert.Equal("mod_assign_save_grade", auditLog.MoodleFunction);
    }

    [Fact]
    public async Task ConfirmLaunch_BloqueiaQuandoEstudanteNaoEstaInscritoNoCurso()
    {
        var fixture = new Fixture();
        var batch = fixture.CreateBatchWithReviewedItem();
        var item = fixture.GradingRepository.Items.Single();
        fixture.EnrollmentGateway.EnrolledUserIds.Clear(); // nenhum estudante inscrito
        var pendingAction = fixture.CreatePendingLaunchAction(batch.Id, item.Id);
        fixture.PendingRepository.Actions.Add(pendingAction);
        var sut = new ConfirmMoodleBatchLaunchCommandHandler(
            fixture.PendingRepository,
            fixture.GradingRepository,
            fixture.Confirmations,
            fixture.Capabilities,
            fixture.ExistingGrades,
            fixture.SubmissionStatuses,
            fixture.EnrollmentGateway,
            fixture.AuditLogs,
            fixture.Mediator);

        var result = await sut.Handle(
            new ConfirmMoodleBatchLaunchCommand(
                pendingAction.Id,
                "CONFIRMAR LANCAMENTO 1 ITEM"),
            CancellationToken.None);

        Assert.Equal(0, result.SentItems);
        Assert.Equal(1, result.FailedItems);
        Assert.Empty(fixture.Mediator.SavedGrades);
        Assert.Equal(GradingCommitStatus.Failed, item.CommitStatus);
        Assert.Contains("nao esta inscrito", result.Failures[0].Message, StringComparison.OrdinalIgnoreCase);
        var auditLog = Assert.Single(fixture.AuditLogs.Logs, log => log.Status == "commit_blocked");
        Assert.Equal("moodle_student_not_enrolled", auditLog.ErrorCode);
    }

    [Fact]
    public async Task ConfirmLaunch_BloqueiaQuandoValidacaoDeEnrollmentFalha()
    {
        var fixture = new Fixture();
        var batch = fixture.CreateBatchWithReviewedItem();
        var item = fixture.GradingRepository.Items.Single();
        fixture.EnrollmentGateway.Error = new InvalidOperationException("Token expirado");
        var pendingAction = fixture.CreatePendingLaunchAction(batch.Id, item.Id);
        fixture.PendingRepository.Actions.Add(pendingAction);
        var sut = new ConfirmMoodleBatchLaunchCommandHandler(
            fixture.PendingRepository,
            fixture.GradingRepository,
            fixture.Confirmations,
            fixture.Capabilities,
            fixture.ExistingGrades,
            fixture.SubmissionStatuses,
            fixture.EnrollmentGateway,
            fixture.AuditLogs,
            fixture.Mediator);

        var result = await sut.Handle(
            new ConfirmMoodleBatchLaunchCommand(
                pendingAction.Id,
                "CONFIRMAR LANCAMENTO 1 ITEM"),
            CancellationToken.None);

        Assert.Equal(0, result.SentItems);
        Assert.Equal(1, result.FailedItems);
        Assert.Empty(fixture.Mediator.SavedGrades);
        Assert.Equal(GradingCommitStatus.Failed, item.CommitStatus);
        Assert.Contains("validar se o estudante", result.Failures[0].Message, StringComparison.OrdinalIgnoreCase);
        var auditLog = Assert.Single(fixture.AuditLogs.Logs, log => log.Status == "commit_blocked");
        Assert.Equal("moodle_enrollment_validation_failed", auditLog.ErrorCode);
    }

    [Fact]
    public async Task ConfirmLaunch_BloqueiaQuandoCapabilitiesGatewayLancaExcecao()
    {
        var fixture = new Fixture();
        fixture.Capabilities.Error = new InvalidOperationException("Token expirado ou sem escopo");
        var batch = fixture.CreateBatchWithReviewedItem();
        var item = fixture.GradingRepository.Items.Single();
        var pendingAction = fixture.CreatePendingLaunchAction(batch.Id, item.Id);
        fixture.PendingRepository.Actions.Add(pendingAction);
        var sut = new ConfirmMoodleBatchLaunchCommandHandler(
            fixture.PendingRepository,
            fixture.GradingRepository,
            fixture.Confirmations,
            fixture.Capabilities,
            fixture.ExistingGrades,
            fixture.SubmissionStatuses,
            fixture.EnrollmentGateway,
            fixture.AuditLogs,
            fixture.Mediator);

        var result = await sut.Handle(
            new ConfirmMoodleBatchLaunchCommand(
                pendingAction.Id,
                "CONFIRMAR LANCAMENTO 1 ITEM"),
            CancellationToken.None);

        Assert.Equal("confirmed", result.Status);
        Assert.Equal(0, result.SentItems);
        Assert.Equal(1, result.FailedItems);
        Assert.Empty(fixture.Mediator.SavedGrades);
        Assert.Equal(GradingCommitStatus.Failed, item.CommitStatus);
        Assert.Contains("validar a funcao Moodle", result.Failures[0].Message, StringComparison.OrdinalIgnoreCase);
        var auditLog = Assert.Single(fixture.AuditLogs.Logs, log => log.Status == "commit_blocked");
        Assert.Equal("moodle_function_validation_failed", auditLog.ErrorCode);
    }

    [Fact]
    public async Task ConfirmLaunch_BloqueiaQuandoJaExisteFeedbackNoMoodle()
    {
        var fixture = new Fixture();
        var batch = fixture.CreateBatchWithReviewedItem();
        var item = fixture.GradingRepository.Items.Single();
        // Substituir o status padrão por um com HasFeedback = true
        fixture.SubmissionStatuses.Statuses.Clear();
        fixture.SubmissionStatuses.Statuses.Add(new AssignmentSubmissionAttemptStatus(
            AssignmentId: "501",
            StudentId: "101",
            AttemptNumber: 0,
            SubmissionStatus: "submitted",
            HasFeedback: true));
        var pendingAction = fixture.CreatePendingLaunchAction(batch.Id, item.Id);
        fixture.PendingRepository.Actions.Add(pendingAction);
        var sut = new ConfirmMoodleBatchLaunchCommandHandler(
            fixture.PendingRepository,
            fixture.GradingRepository,
            fixture.Confirmations,
            fixture.Capabilities,
            fixture.ExistingGrades,
            fixture.SubmissionStatuses,
            fixture.EnrollmentGateway,
            fixture.AuditLogs,
            fixture.Mediator);

        var result = await sut.Handle(
            new ConfirmMoodleBatchLaunchCommand(
                pendingAction.Id,
                "CONFIRMAR LANCAMENTO 1 ITEM"),
            CancellationToken.None);

        Assert.Equal(0, result.SentItems);
        Assert.Equal(1, result.FailedItems);
        Assert.Empty(fixture.Mediator.SavedGrades);
        Assert.Equal(GradingCommitStatus.Failed, item.CommitStatus);
        Assert.Contains("feedback existente", result.Failures[0].Message, StringComparison.OrdinalIgnoreCase);
        var auditLog = Assert.Single(fixture.AuditLogs.Logs, log => log.Status == "commit_blocked");
        Assert.Equal("moodle_existing_feedback", auditLog.ErrorCode);
    }

    private sealed class Fixture(string currentUserSubject = "teacher-1", IReadOnlyCollection<string>? scopes = null)
    {
        public FakeGradingReviewRepository GradingRepository { get; } = new();
        public FakePendingActionService PendingActions { get; } = new();
        public FakePendingActionRepository PendingRepository { get; } = new();
        public FakeActionConfirmationService Confirmations { get; } = new();
        public FakeMoodleGradingCapabilitiesGateway Capabilities { get; } = new();
        public FakeMoodleAssignmentGradeReadGateway ExistingGrades { get; } = new();
        public FakeMoodleAssignmentSubmissionStatusGateway SubmissionStatuses { get; } = new();
        public FakeMoodleParticipantsGateway EnrollmentGateway { get; } = new();
        public FakeAuditLogRepository AuditLogs { get; } = new();
        public FakeMediator Mediator { get; } = new();
        public FakeCurrentUserContext CurrentUser { get; } = new(currentUserSubject, scopes);
        public FakeMoodleAssignmentSettingsGateway SettingsGateway { get; } = new();

        public AssistedGradingBatch CreateBatchWithReviewedItem(bool includeContext = true)
        {
            var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
            var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
            item.SetDraft(8m, 0.8m, "Rascunho.");
            item.ApplyTeacherReview(8.5m, "Feedback final revisado.", "teacher-1", 321, "approved", "ok");
            if (includeContext)
            {
                AttachVersionedContext(item, batch);
            }
            GradingRepository.Batches.Add(batch);
            GradingRepository.Items.Add(item);
            return batch;
        }

        public PendingMoodleAction CreatePendingLaunchAction(
            Guid batchId,
            Guid gradingItemId,
            string? draftVersionHash = null,
            bool allowOverwriteExisting = false,
            string? contextHash = null)
        {
            if (!SubmissionStatuses.Statuses.Any(status =>
                status.AssignmentId == "501" &&
                status.StudentId == "101"))
            {
                SubmissionStatuses.Statuses.Add(new AssignmentSubmissionAttemptStatus(
                    AssignmentId: "501",
                    StudentId: "101",
                    AttemptNumber: 0,
                    SubmissionStatus: "submitted"));
            }

            var payload = new GradingLaunchPayload(
                batchId,
                [
                    new GradingLaunchPayloadItem(
                        gradingItemId,
                        "10",
                        "501",
                        "101",
                        8.5m,
                        "Feedback final revisado.",
                        AttemptNumber: 0,
                        DraftVersionHash: draftVersionHash ?? GradingDraftVersionHash.Compute(GradingRepository.Items.Single(item => item.Id == gradingItemId)),
                        ContextHash: contextHash ?? GradingRepository.Items.Single(item => item.Id == gradingItemId).ContextHash)
                ],
                allowOverwriteExisting);

            return new PendingMoodleAction
            {
                Id = PendingActions.PendingActionId,
                ToolName = "criar_previa_lancamento_lote",
                RiskLevel = ToolRiskLevel.CriticalHumanConfirmedWrite,
                CreatedBySubject = "teacher-1",
                CourseId = 10,
                PayloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                PreviewJson = "{}",
                ConfirmationText = "CONFIRMAR LANCAMENTO 1 ITEM",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                IdempotencyKey = "idem-1",
                CorrelationId = "audit-1"
            };
        }
    }

    private static void AttachVersionedContext(AssistedGradingItem item, AssistedGradingBatch batch)
    {
        var context = GradingContext.Build(
            item.Id,
            batch.Id,
            "10",
            "501",
            "9001",
            "101",
            "Enunciado de teste",
            "Criterio de teste",
            null,
            10m,
            null,
            "Entrega de teste",
            [],
            null,
            null);
        var snapshot = GradingContextSnapshotFactory.Create(
            item,
            context,
            new GradingContextOptions(
                IncludeRubric: true,
                IncludeSubmissionFiles: true,
                IncludeCourseMaterials: false));
        item.RecordContextSnapshot(snapshot);
    }

    private sealed class FakeCurrentUserContext(string subject, IReadOnlyCollection<string>? scopes = null) : ICurrentUserContext
    {
        public string Subject { get; } = subject;
        public string? Email => "teacher@example.com";
        public IReadOnlyCollection<string> Scopes { get; } = scopes ?? [];

        public bool HasScope(string scope)
        {
            return Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class FakeMoodleAssignmentSettingsGateway : IMoodleAssignmentSettingsGateway
    {
        public Task<AssignmentSettingsSummary?> GetAssignmentSettingsAsync(
            string userExternalId,
            string courseId,
            string assignmentId,
            CancellationToken cancellationToken)
            => Task.FromResult<AssignmentSettingsSummary?>(new AssignmentSettingsSummary(assignmentId, 10m, "Atividade"));
    }

    private sealed class FakeGradingReviewRepository : IGradingReviewRepository
    {
        public List<AssistedGradingBatch> Batches { get; } = [];
        public List<AssistedGradingItem> Items { get; } = [];
        public List<GradingEvidence> Evidence { get; } = [];

        public Task AddBatchAsync(AssistedGradingBatch batch, CancellationToken cancellationToken)
        {
            Batches.Add(batch);
            return Task.CompletedTask;
        }

        public Task<AssistedGradingBatch?> GetBatchAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(Batches.SingleOrDefault(batch => batch.Id == id));
        }

        public Task AddItemAsync(AssistedGradingItem item, CancellationToken cancellationToken)
        {
            Items.Add(item);
            return Task.CompletedTask;
        }

        public Task AddArtifactAsync(GradingArtifact artifact, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task AddEvidenceAsync(GradingEvidence evidence, CancellationToken cancellationToken)
        {
            Evidence.Add(evidence);
            return Task.CompletedTask;
        }

        public Task<AssistedGradingItem?> GetItemAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(Items.SingleOrDefault(item => item.Id == id));
        }

        public Task<IReadOnlyList<AssistedGradingItem>> ListItemsByBatchAsync(
            Guid batchId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            var items = Items
                .Where(item => item.BatchId == batchId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToArray();
            return Task.FromResult<IReadOnlyList<AssistedGradingItem>>(items);
        }

        public Task<int> CountItemsByBatchAsync(Guid batchId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Items.Count(item => item.BatchId == batchId));
        }

        public Task<IReadOnlyList<GradingArtifact>> ListArtifactsByItemAsync(
            Guid gradingItemId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<GradingArtifact>>([]);
        }

        public Task<IReadOnlyList<GradingEvidence>> ListEvidenceByItemAsync(
            Guid gradingItemId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<GradingEvidence>>(Evidence
                .Where(evidence => evidence.GradingItemId == gradingItemId)
                .ToArray());
        }

        public Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByStatusAsync(
            GradingBatchStatus status, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AssistedGradingBatch>>(Array.Empty<AssistedGradingBatch>());
        public Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByCreatorAsync(string createdBySubject, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AssistedGradingBatch>>(Array.Empty<AssistedGradingBatch>());        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakePendingActionService : IPendingActionService
    {
        public Guid PendingActionId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000999");
        public GradingLaunchPayload? LastPayload { get; private set; }

        public Task<PendingActionResponse> CreatePendingActionAsync(
            string toolName,
            ToolRiskLevel riskLevel,
            object payload,
            object preview,
            string confirmationText,
            TimeSpan expiresIn,
            long? courseId,
            CancellationToken cancellationToken)
        {
            LastPayload = Assert.IsType<GradingLaunchPayload>(payload);
            return Task.FromResult(new PendingActionResponse(
                "pending_confirmation",
                PendingActionId,
                toolName,
                riskLevel,
                preview,
                confirmationText,
                DateTimeOffset.UtcNow.Add(expiresIn)));
        }
    }

    private sealed class FakePendingActionRepository : IPendingMoodleActionRepository
    {
        public List<PendingMoodleAction> Actions { get; } = [];

        public Task AddAsync(PendingMoodleAction action, CancellationToken cancellationToken)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }

        public Task<PendingMoodleAction?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(Actions.SingleOrDefault(action => action.Id == id));
        }

        public Task<PendingActionConfirmationClaimResult> TryConfirmWithAuditAsync(Guid id, string confirmedBySubject, DateTimeOffset confirmedAt, MoodleAuditLog confirmationAudit, CancellationToken cancellationToken)
        {
            var action = Actions.SingleOrDefault(candidate => candidate.Id == id);
            if (action?.Status != PendingActionStatus.PendingConfirmation)
                return Task.FromResult(new PendingActionConfirmationClaimResult(false, action?.Status ?? PendingActionStatus.Expired, action?.ConfirmedAt));
            action.Confirm(confirmedBySubject, confirmedAt);
            return Task.FromResult(new PendingActionConfirmationClaimResult(true, action.Status, action.ConfirmedAt));
        }

        public Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByStatusAsync(
            GradingBatchStatus status, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AssistedGradingBatch>>(Array.Empty<AssistedGradingBatch>());
        public Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByCreatorAsync(string createdBySubject, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AssistedGradingBatch>>(Array.Empty<AssistedGradingBatch>());        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeActionConfirmationService : IActionConfirmationService
    {
        public string? LastRequiredScope { get; private set; }

        public Task<ActionConfirmationResponse> ConfirmAsync(
            Guid pendingActionId,
            string confirmationText,
            string? requiredScope,
            CancellationToken cancellationToken)
        {
            LastRequiredScope = requiredScope;
            return Task.FromResult(new ActionConfirmationResponse(
                "confirmed",
                pendingActionId,
                "criar_previa_lancamento_lote",
                ToolRiskLevel.CriticalHumanConfirmedWrite,
                DateTimeOffset.UtcNow,
                "audit-1"));
        }
    }

    private sealed class FakeMoodleGradingCapabilitiesGateway : IMoodleGradingCapabilitiesGateway
    {
        public List<string> Functions { get; } = ["mod_assign_save_grade"];
        public Exception? Error { get; set; }

        public Task<MoodleWebServiceFunctionCatalog> GetFunctionCatalogAsync(
            string userExternalId,
            CancellationToken cancellationToken)
        {
            if (Error is not null)
            {
                throw Error;
            }

            return Task.FromResult(new MoodleWebServiceFunctionCatalog(
                "moodle_mobile_app",
                Functions));
        }
    }

    private sealed class FakeMoodleAssignmentGradeReadGateway : IMoodleAssignmentGradeReadGateway
    {
        public List<AssignmentExistingGrade> ExistingGrades { get; } = [];
        public Exception? Error { get; set; }

        public Task<AssignmentExistingGrade?> GetExistingGradeAsync(
            string userExternalId,
            string assignmentId,
            string studentId,
            CancellationToken cancellationToken)
        {
            if (Error is not null)
            {
                throw Error;
            }

            return Task.FromResult(ExistingGrades.SingleOrDefault(grade =>
                grade.AssignmentId == assignmentId &&
                grade.StudentId == studentId));
        }
    }

    private sealed class FakeMoodleAssignmentSubmissionStatusGateway : IMoodleAssignmentSubmissionStatusGateway
    {
        public List<AssignmentSubmissionAttemptStatus> Statuses { get; } = [];

        public Task<AssignmentSubmissionAttemptStatus?> GetSubmissionStatusAsync(
            string userExternalId,
            string assignmentId,
            string studentId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Statuses.LastOrDefault(status =>
                status.AssignmentId == assignmentId &&
                status.StudentId == studentId));
        }
    }

    private sealed class FakeAuditLogRepository : IMoodleAuditLogRepository
    {
        public List<MoodleAuditLog> Logs { get; } = [];

        public Task AddAsync(MoodleAuditLog log, CancellationToken cancellationToken)
        {
            Logs.Add(log);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MoodleAuditLog>> ListByCorrelationIdAsync(
            string correlationId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            var items = Logs
                .Where(log => log.CorrelationId == correlationId)
                .OrderBy(log => log.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToArray();
            return Task.FromResult<IReadOnlyList<MoodleAuditLog>>(items);
        }

        public Task<int> CountByCorrelationIdAsync(
            string correlationId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Logs.Count(log => log.CorrelationId == correlationId));
        }

        public Task<IReadOnlyList<MoodleAuditLog>> ListByBatchJobIdAsync(
            Guid batchJobId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            var items = Logs
                .Where(log => log.BatchJobId == batchJobId)
                .OrderBy(log => log.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToArray();
            return Task.FromResult<IReadOnlyList<MoodleAuditLog>>(items);
        }

        public Task<int> CountByBatchJobIdAsync(Guid batchJobId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Logs.Count(log => log.BatchJobId == batchJobId));
        }

        public Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByStatusAsync(
            GradingBatchStatus status, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AssistedGradingBatch>>(Array.Empty<AssistedGradingBatch>());
        public Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByCreatorAsync(string createdBySubject, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AssistedGradingBatch>>(Array.Empty<AssistedGradingBatch>());        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMediator : IMediator
    {
        public List<SaveAssignmentGradeCommand> SavedGrades { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is SaveAssignmentGradeCommand save)
            {
                SavedGrades.Add(save);
                return Task.FromResult((TResponse)(object)new AssignmentGradeWriteResult(
                    true,
                    "mod_assign_save_grade",
                    "ok"));
            }

            throw new NotSupportedException($"Request nao suportado no fake mediator: {request.GetType().Name}");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            if (request is SaveAssignmentGradeCommand save)
            {
                SavedGrades.Add(save);
                return Task.FromResult<object?>(new AssignmentGradeWriteResult(
                    true,
                    "mod_assign_save_grade",
                    "ok"));
            }

            throw new NotSupportedException($"Request nao suportado no fake mediator: {request.GetType().Name}");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<object?>();
    }

    private sealed class FakeMoodleParticipantsGateway : IMoodleParticipantsGateway
    {
        public List<string> EnrolledUserIds { get; } = ["101"];
        public Exception? Error { get; set; }

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
            if (Error is not null)
            {
                throw Error;
            }

            var participants = EnrolledUserIds
                .Select(id => new CourseParticipantSummary(
                    id,
                    $"Student {id}",
                    Email: null,
                    Suspended: false,
                    FirstAccessAt: null,
                    LastAccessAt: null,
                    LastCourseAccessAt: null,
                    Roles: [new CourseParticipantRole("5", "student", "Estudante")],
                    Groups: []))
                .ToArray();

            return Task.FromResult(new CourseParticipantsPage(
                courseId,
                page,
                pageSize,
                statusFilter,
                studentsOnly,
                includeEmail,
                HasMore: false,
                participants));
        }

        public Task<IReadOnlyList<CourseGroupSummary>> GetCourseGroupsAsync(
            string userExternalId,
            string courseId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CourseGroupSummary>>([]);
        }
    }
}
