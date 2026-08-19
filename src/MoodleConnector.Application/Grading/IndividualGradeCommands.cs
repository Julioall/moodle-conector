using System.Globalization;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Auditing;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.PendingActions;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Grading;

// â”€â”€ Shared payload â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

internal sealed record IndividualGradePayload(
    string CourseId,
    string AssignmentId,
    string StudentId,
    string StudentFullName,
    decimal ProposedGrade,
    string FeedbackText,
    string JustificationText,
    decimal? PreviousGrade,
    string RequiredScope);

// â”€â”€ Prepare â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

/// <summary>
/// Prévia do lançamento de nota individual.
/// </summary>
public sealed record IndividualGradePreview(
    string AssignmentId,
    string StudentId,
    string StudentFullName,
    string CourseId,
    decimal ProposedGrade,
    decimal? GradeMax,
    decimal? PreviousGrade,
    string? PreviousFeedback,
    string ConfirmationText,
    IReadOnlyList<string> Risks,
    DateTimeOffset ExpiresAt);

public sealed record IndividualGradePrepareResult(
    Guid PendingActionId,
    string Status,
    IndividualGradePreview Preview);

/// <summary>
/// Prepara o lançamento de nota individual para um estudante em uma SA.
///
/// Risco: CriticalHumanConfirmedWrite.
/// Feature flag: AssignmentGradeWriteEnabled.
/// Escopo: moodle.write.assignments.grade.
///
/// Busca a nota atual antes de exibir a prévia para que o tutor possa comparar.
/// A confirmação exige texto exato incluindo a nota numérica.
/// </summary>
public sealed record PrepareIndividualGradeCommand(
    string CourseId,
    string AssignmentId,
    string StudentId,
    decimal ProposedGrade,
    string? FeedbackText,
    string JustificationText) : IRequest<IndividualGradePrepareResult>;

public sealed class PrepareIndividualGradeCommandHandler(
    IMoodleAssignmentGradeReadGateway gradeReadGateway,
    IMoodleCurrentUserIdGateway currentUserIdGateway,
    IMoodleParticipantsGateway participantsGateway,
    IPendingActionService pendingActions,
    IOptions<AssignmentWriteFeatureOptions> features,
    IMoodleCourseContentsGateway? contentsGateway = null)
    : IRequestHandler<PrepareIndividualGradeCommand, IndividualGradePrepareResult>
{
    private static readonly TimeSpan PendingActionExpiration = TimeSpan.FromMinutes(15);
    private const string CommitToolName = "confirmar_lancamento_nota";
    private const string RequiredScope = "moodle.write.assignments.grade";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IndividualGradePrepareResult> Handle(
        PrepareIndividualGradeCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Feature flag
        if (!features.Value.AssignmentGradeWriteEnabled)
            throw new InvalidOperationException(
                "O lançamento de notas individuais está desabilitado. " +
                "Habilite AssignmentGradeWriteEnabled na configuração.");

        // 2. Validate inputs
        if (string.IsNullOrWhiteSpace(request.CourseId))
            throw new ArgumentException("courseId é obrigatório.");
        if (string.IsNullOrWhiteSpace(request.AssignmentId))
            throw new ArgumentException("assignmentId é obrigatório.");
        if (string.IsNullOrWhiteSpace(request.StudentId))
            throw new ArgumentException("studentId é obrigatório.");
        if (request.ProposedGrade < 0)
            throw new ArgumentOutOfRangeException(nameof(request.ProposedGrade), "A nota não pode ser negativa.");
        if (string.IsNullOrWhiteSpace(request.JustificationText))
            throw new ArgumentException("Uma justificativa é obrigatória para o lançamento de nota.");

        var effectiveAssignmentId = request.AssignmentId.Trim();
        if (contentsGateway is not null)
        {
            try
            {
                var currentUser = (await currentUserIdGateway.GetCurrentUserIdAsync(cancellationToken)).ToString();
                var contents = await contentsGateway.GetCourseContentsAsync(
                    currentUser,
                    request.CourseId.Trim(),
                    CourseActivityModuleTypes.Assignments,
                    includeHidden: true,
                    onlyWithFiles: false,
                    cancellationToken);
                var module = contents.Sections
                    .SelectMany(section => section.Modules)
                    .FirstOrDefault(item =>
                        string.Equals(item.ModuleType, "assign", StringComparison.OrdinalIgnoreCase) &&
                        (string.Equals(item.ModuleId, effectiveAssignmentId, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(item.InstanceId, effectiveAssignmentId, StringComparison.OrdinalIgnoreCase)));
                if (!string.IsNullOrWhiteSpace(module?.InstanceId))
                {
                    effectiveAssignmentId = module.InstanceId!;
                }
            }
            catch
            {
                // Fall back to the identifier supplied by the caller.
            }
        }

        var currentUserExternalId = (await currentUserIdGateway.GetCurrentUserIdAsync(cancellationToken)).ToString();

        // 3. Resolve student name (best effort)
        string studentName = request.StudentId;
        try
        {
            var page = await participantsGateway.GetCourseParticipantsAsync(
                currentUserExternalId, request.CourseId,
                ParticipantStatusFilter.Active,
                page: 0, pageSize: 200,
                studentsOnly: false, includeEmail: false,
                groupId: null, cancellationToken: cancellationToken);
            var student = page.Participants.FirstOrDefault(p => p.UserId == request.StudentId);
            if (student is not null) studentName = student.FullName;
        }
        catch { /* fallback to ID */ }

        // 4. Fetch current grade (best effort)
        AssignmentExistingGrade? existing = null;
        try
        {
            existing = await gradeReadGateway.GetExistingGradeAsync(
                currentUserExternalId, effectiveAssignmentId, request.StudentId, cancellationToken);
        }
        catch { /* partial data ok */ }

        // 5. Build confirmation text (includes numeric grade for security)
        var gradeLabel = request.ProposedGrade.ToString("F2", CultureInfo.InvariantCulture);
        var confirmationText =
            $"CONFIRMAR NOTA {gradeLabel}";

        // 6. Risks
        var risks = new List<string>
        {
            $"Esta ação lançará a nota {gradeLabel} para {studentName}.",
            "Nota lançada via API Moodle é imediata e visível ao estudante.",
            "O sistema registrará esta operação em auditoria.",
            $"Escopo obrigatório: {RequiredScope}.",
            "Esta ação requer feature flag AssignmentGradeWriteEnabled ativa."
        };
        if (existing?.HasGrade == true)
            risks.Add($"ATENÇÃO: nota atual é {existing.Grade:F2}. Será substituída pela nova nota {gradeLabel}.");
        if (!string.IsNullOrWhiteSpace(request.FeedbackText))
            risks.Add("O feedback informado será publicado junto com a nota.");

        var expiresAt = DateTimeOffset.UtcNow.Add(PendingActionExpiration);

        var preview = new IndividualGradePreview(
            AssignmentId: effectiveAssignmentId,
            StudentId: request.StudentId,
            StudentFullName: studentName,
            CourseId: request.CourseId,
            ProposedGrade: request.ProposedGrade,
            GradeMax: existing?.GradeMax,
            PreviousGrade: existing?.HasGrade == true ? existing.Grade : null,
            PreviousFeedback: existing?.Feedback,
            ConfirmationText: confirmationText,
            Risks: risks,
            ExpiresAt: expiresAt);

        var payload = new IndividualGradePayload(
            CourseId: request.CourseId,
            AssignmentId: effectiveAssignmentId,
            StudentId: request.StudentId,
            StudentFullName: studentName,
            ProposedGrade: request.ProposedGrade,
            FeedbackText: request.FeedbackText ?? string.Empty,
            JustificationText: request.JustificationText,
            PreviousGrade: existing?.HasGrade == true ? existing.Grade : null,
            RequiredScope: RequiredScope);

        var pendingResponse = await pendingActions.CreatePendingActionAsync(
            toolName: CommitToolName,
            riskLevel: ToolRiskLevel.CriticalHumanConfirmedWrite,
            payload: payload,
            preview: preview,
            confirmationText: confirmationText,
            expiresIn: PendingActionExpiration,
            courseId: long.TryParse(request.CourseId, out var cid) ? cid : null,
            cancellationToken: cancellationToken);

        return new IndividualGradePrepareResult(
            PendingActionId: pendingResponse.PendingActionId,
            Status: "pending",
            Preview: preview);
    }
}


/// <summary>
/// Resultado do lançamento de nota individual.
/// </summary>
public sealed record IndividualGradeSendResult(
    string Status,
    Guid PendingActionId,
    string AssignmentId,
    string StudentId,
    decimal LaunchedGrade,
    string? AuditId,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Confirma o lançamento de nota individual previamente preparado.
/// Segue o mesmo padrão de GradingLaunchCommands: GetByIdAsync â†’ ConfirmAsync â†’ Deserialize â†’ SaveGrade â†’ Audit.
/// </summary>
public sealed record ConfirmIndividualGradeCommand(
    Guid PendingActionId,
    string ConfirmationText) : IRequest<IndividualGradeSendResult>;

public sealed class ConfirmIndividualGradeCommandHandler(
    IPendingMoodleActionRepository pendingActions,
    IActionConfirmationService confirmations,
    IMoodleAssignmentGradingGateway gradingGateway,
    IMoodleAuditLogRepository auditLogs,
    IOptions<AssignmentWriteFeatureOptions> features)
    : IRequestHandler<ConfirmIndividualGradeCommand, IndividualGradeSendResult>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string CommitToolName = "confirmar_lancamento_nota";
    private const string MoodleFunction = "mod_assign_save_grade";

    public async Task<IndividualGradeSendResult> Handle(
        ConfirmIndividualGradeCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Feature flag re-check at execution time
        if (!features.Value.AssignmentGradeWriteEnabled)
            throw new InvalidOperationException(
                "O lançamento de notas individuais está desabilitado (AssignmentGradeWriteEnabled=false).");

        // 2. Load pending action (same pattern as GradingLaunchCommands)
        var action = await pendingActions.GetByIdAsync(request.PendingActionId, cancellationToken)
            ?? throw new InvalidOperationException("Ação pendente não encontrada ou expirada.");

        // 3. Confirm (throws InvalidOperationException on any validation failure)
        var confirmation = await confirmations.ConfirmAsync(
            request.PendingActionId,
            request.ConfirmationText,
            requiredScope: "moodle.write.assignments.grade",
            cancellationToken);

        // 4. Deserialize payload
        var payload = JsonSerializer.Deserialize<IndividualGradePayload>(action.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Payload de nota individual inválido.");

        if (confirmation.Status == "already_confirmed")
        {
            return new IndividualGradeSendResult(
                "already_confirmed",
                request.PendingActionId,
                payload.AssignmentId,
                payload.StudentId,
                payload.ProposedGrade,
                confirmation.AuditId,
                ["Esta ação já foi confirmada e não será executada novamente."]);
        }

        var userExternalId = action.CreatedByMoodleUserId?.ToString(CultureInfo.InvariantCulture)
            ?? action.CreatedBySubject;

        // 5. Launch grade to Moodle
        AssignmentGradeWriteResult writeResult;
        try
        {
            writeResult = await gradingGateway.SaveGradeAsync(
                userExternalId: userExternalId,
                request: new AssignmentGradeWriteRequest(
                    AssignmentId: payload.AssignmentId,
                    StudentId: payload.StudentId,
                    Grade: payload.ProposedGrade,
                    FeedbackText: payload.FeedbackText,
                    AttemptNumber: -1,
                    AddAttempt: false,
                    ApplyToAll: false,
                    WorkflowState: "graded"),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordAuditAsync(action, payload, "grade_failed",
                new { error = ex.GetType().Name }, ex.GetType().Name, ex.Message, cancellationToken);
            await auditLogs.SaveChangesAsync(cancellationToken);

            return new IndividualGradeSendResult(
                Status: "failed",
                PendingActionId: request.PendingActionId,
                AssignmentId: payload.AssignmentId,
                StudentId: payload.StudentId,
                LaunchedGrade: payload.ProposedGrade,
                AuditId: confirmation.AuditId,
                Warnings: [ex.Message]);
        }

        // 6. Audit success
        await RecordAuditAsync(action, payload,
            writeResult.Success ? "grade_launched" : "grade_partial",
            writeResult, null,
            writeResult.Success ? null : $"MoodleStatus={writeResult.MoodleStatus}",
            cancellationToken);
        await auditLogs.SaveChangesAsync(cancellationToken);

        var warnings = new List<string>();
        if (!writeResult.Success)
            warnings.Add($"Nota lançada com status parcial (MoodleStatus={writeResult.MoodleStatus}). Verificar no Moodle.");

        return new IndividualGradeSendResult(
            Status: writeResult.Success ? "launched" : "partial",
            PendingActionId: request.PendingActionId,
            AssignmentId: payload.AssignmentId,
            StudentId: payload.StudentId,
            LaunchedGrade: payload.ProposedGrade,
            AuditId: confirmation.AuditId,
            Warnings: warnings);
    }

    private Task RecordAuditAsync(
        PendingMoodleAction action,
        IndividualGradePayload payload,
        string status,
        object responseSummary,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken) =>
        auditLogs.AddAsync(new MoodleAuditLog
        {
            CorrelationId = action.CorrelationId,
            ToolName = CommitToolName,
            RiskLevel = ToolRiskLevel.CriticalHumanConfirmedWrite,
            ActorSubject = action.CreatedBySubject,
            ActorEmail = action.CreatedByEmail,
            ActorMoodleUserId = action.CreatedByMoodleUserId,
            CourseId = action.CourseId,
            MoodleFunction = MoodleFunction,
            RequestSanitizedJson = AuditPayloadSanitizer.SerializeSanitized(new
            {
                payload.AssignmentId,
                payload.StudentId,
                payload.CourseId,
                payload.ProposedGrade,
                payload.JustificationText,
                previousGrade = payload.PreviousGrade
            }),
            ResponseSummaryJson = AuditPayloadSanitizer.SerializeSanitized(responseSummary),
            Status = status,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        }, cancellationToken);
}
