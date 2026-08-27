namespace MoodleConnector.Domain.Grading;

public sealed class AssistedGradingItem
{
    private AssistedGradingItem()
    {
    }

    public Guid Id { get; private init; } = Guid.NewGuid();

    public Guid BatchId { get; private init; }

    public long CourseId { get; private init; }

    public long AssignmentId { get; private init; }

    public long? SubmissionId { get; private init; }

    public long MoodleUserId { get; private init; }

    public int? AttemptNumber { get; private init; }

    public GradingItemStatus Status { get; private set; } = GradingItemStatus.Pending;

    /// <summary>
    /// Checkpoint da etapa interna mais recente. O campo não representa uma
    /// decisão acadêmica; serve apenas para retomada idempotente do worker.
    /// </summary>
    public string ProcessingStage { get; private set; } = GradingProcessingStage.Pending;

    public DateTimeOffset? ProcessingStageUpdatedAt { get; private set; }

    public decimal? SuggestedGrade { get; private set; }

    public decimal? FinalGrade { get; private set; }

    public decimal? Confidence { get; private set; }

    public string? DraftFeedback { get; private set; }

    public string? PrivateNotesToTeacher { get; private set; }

    public string? FinalFeedback { get; private set; }

    public string? TeacherDecision { get; private set; }

    public string? ReviewNotes { get; private set; }

    public GradingReviewStatus ReviewStatus { get; private set; } = GradingReviewStatus.NotReviewed;

    public string? ReviewedBySubject { get; private set; }

    public long? ReviewedByMoodleUserId { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public GradingCommitStatus CommitStatus { get; private set; } = GradingCommitStatus.NotReady;

    public string? CommitError { get; private set; }

    public string? IdempotencyKey { get; private set; }

    /// <summary>
    /// Identidade do contexto canônico usado na última pré-validação deste item.
    /// O payload do contexto permanece nos artifacts; estes campos permitem
    /// detectar divergência entre worker, revisão e lançamento sem duplicar texto.
    /// </summary>
    public int? ContextVersion { get; private set; }

    public string? ContextHash { get; private set; }

    public string? ContextStatus { get; private set; }

    /// <summary>
    /// Identificador efêmero do worker que está processando este item.
    /// O lease é um mecanismo de coordenação e nunca substitui autorização.
    /// Não contém credenciais nem dados do estudante.
    /// </summary>
    public string? LeaseOwner { get; private set; }

    public DateTimeOffset? LeaseUntil { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTimeOffset? NextAttemptAt { get; private set; }

    public string? LastErrorCode { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public static AssistedGradingItem Create(
        Guid batchId,
        long courseId,
        long assignmentId,
        long? submissionId,
        long moodleUserId,
        int? attemptNumber)
    {
        if (batchId == Guid.Empty)
        {
            throw new ArgumentException("O lote e obrigatorio.", nameof(batchId));
        }

        if (courseId <= 0 || assignmentId <= 0 || moodleUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(courseId), "Curso, tarefa e estudante devem ser identificadores Moodle positivos.");
        }

        return new AssistedGradingItem
        {
            BatchId = batchId,
            CourseId = courseId,
            AssignmentId = assignmentId,
            SubmissionId = submissionId,
            MoodleUserId = moodleUserId,
            AttemptNumber = attemptNumber
        };
    }

    public void SetDraft(
        decimal? suggestedGrade,
        decimal? confidence,
        string draftFeedback,
        string? privateNotesToTeacher = null,
        decimal? maxGrade = null)
    {
        if (suggestedGrade < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(suggestedGrade), "A nota sugerida nao pode ser negativa.");
        }

        if (suggestedGrade is not null && maxGrade is not null &&
            (maxGrade <= 0 || suggestedGrade > maxGrade))
        {
            throw new ArgumentOutOfRangeException(nameof(suggestedGrade), "A nota sugerida excede a nota maxima da atividade.");
        }

        if (confidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), "A confianca deve ficar entre 0 e 1.");
        }

        SuggestedGrade = suggestedGrade;
        Confidence = confidence;
        DraftFeedback = draftFeedback.Trim();
        PrivateNotesToTeacher = string.IsNullOrWhiteSpace(privateNotesToTeacher) ? null : privateNotesToTeacher.Trim();
        Status = GradingItemStatus.DraftReady;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void BlockAnalysis(string reason)
    {
        var message = string.IsNullOrWhiteSpace(reason)
            ? "Nao foi possivel processar a submissao para correcao assistida."
            : reason.Trim();

        SuggestedGrade = null;
        Confidence = 0m;
        DraftFeedback = message;
        PrivateNotesToTeacher = message;
        Status = GradingItemStatus.Blocked;
        CommitStatus = GradingCommitStatus.NotReady;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marca o item como tendo passado na pré-validação diagnóstica e pronto
    /// para análise pela IA. Não gera nota, feedback nem critérios heurísticos.
    /// O tutor deve usar o fluxo de IA para gerar nota e feedback.
    /// </summary>
    public void MarkAwaitingAiAnalysis(string? diagnosticNotes)
    {
        SuggestedGrade = null;
        Confidence = null;
        DraftFeedback = null;
        PrivateNotesToTeacher = string.IsNullOrWhiteSpace(diagnosticNotes) ? null : diagnosticNotes.Trim();
        Status = GradingItemStatus.AwaitingAiAnalysis;
        CommitStatus = GradingCommitStatus.NotReady;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAnalysisFailed(string error)
    {
        var message = string.IsNullOrWhiteSpace(error)
            ? "Falha desconhecida ao processar a correcao assistida."
            : error.Trim();

        SuggestedGrade = null;
        Confidence = 0m;
        DraftFeedback = message;
        PrivateNotesToTeacher = message;
        Status = GradingItemStatus.Failed;
        CommitStatus = GradingCommitStatus.NotReady;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyTeacherReview(
        decimal? finalGrade,
        string finalFeedback,
        string reviewedBySubject,
        long? reviewedByMoodleUserId,
        string? teacherDecision = null,
        string? reviewNotes = null,
        decimal? maxGrade = null)
    {
        if (finalGrade < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(finalGrade), "A nota final nao pode ser negativa.");
        }

        if (finalGrade is not null && maxGrade is not null &&
            (maxGrade <= 0 || finalGrade > maxGrade))
        {
            throw new ArgumentOutOfRangeException(nameof(finalGrade), "A nota final excede a nota maxima da atividade.");
        }

        if (string.IsNullOrWhiteSpace(finalFeedback))
        {
            throw new ArgumentException("O feedback final revisado e obrigatorio.", nameof(finalFeedback));
        }

        if (string.IsNullOrWhiteSpace(reviewedBySubject))
        {
            throw new ArgumentException("O revisor e obrigatorio.", nameof(reviewedBySubject));
        }

        FinalGrade = finalGrade;
        FinalFeedback = finalFeedback.Trim();
        TeacherDecision = string.IsNullOrWhiteSpace(teacherDecision) ? null : teacherDecision.Trim();
        ReviewNotes = string.IsNullOrWhiteSpace(reviewNotes) ? null : reviewNotes.Trim();
        ReviewStatus = GradingReviewStatus.Reviewed;
        ReviewedBySubject = reviewedBySubject.Trim();
        ReviewedByMoodleUserId = reviewedByMoodleUserId;
        ReviewedAt = DateTimeOffset.UtcNow;
        Status = GradingItemStatus.ReadyToCommit;
        CommitStatus = GradingCommitStatus.Pending;
        IdempotencyKey ??= MoodleConnector.Domain.IdempotencyKey.New().ToString();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCommitSucceeded()
    {
        Status = GradingItemStatus.Committed;
        CommitStatus = GradingCommitStatus.Succeeded;
        CommitError = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCommitFailed(string error)
    {
        Status = GradingItemStatus.Failed;
        CommitStatus = GradingCommitStatus.Failed;
        CommitError = string.IsNullOrWhiteSpace(error) ? "Falha desconhecida no commit." : error.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCommitExecutionUnknown(string error)
    {
        Status = GradingItemStatus.Failed;
        CommitStatus = GradingCommitStatus.ExecutionUnknown;
        CommitError = string.IsNullOrWhiteSpace(error)
            ? "O Moodle pode ter aplicado a escrita; reconcilie o item antes de tentar novamente."
            : error.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordContextSnapshot(GradingContextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.ItemId != Id || snapshot.BatchId != BatchId)
        {
            throw new InvalidOperationException("O snapshot de contexto nao pertence ao item de correcao informado.");
        }

        var computedHash = GradingContextSnapshot.ComputeHash(snapshot);
        if (!string.Equals(computedHash, snapshot.ContextHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A integridade do snapshot de contexto nao pode ser confirmada.");
        }

        ContextVersion = snapshot.Version;
        ContextHash = snapshot.ContextHash;
        ContextStatus = snapshot.ContextStatus;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkProcessingStage(string stage, DateTimeOffset? at = null)
    {
        if (!GradingProcessingStage.IsKnown(stage))
        {
            throw new ArgumentException("A etapa de processamento nao e reconhecida.", nameof(stage));
        }

        var timestamp = at ?? DateTimeOffset.UtcNow;
        ProcessingStage = stage;
        ProcessingStageUpdatedAt = timestamp;
        UpdatedAt = timestamp;
    }

    public void ResolveCommitExecutionUnknown(bool applied)
    {
        if (CommitStatus != GradingCommitStatus.ExecutionUnknown)
        {
            throw new InvalidOperationException($"O item não está em execução desconhecida: {CommitStatus}.");
        }

        if (applied)
        {
            MarkCommitSucceeded();
            return;
        }

        Status = GradingItemStatus.ReadyToCommit;
        CommitStatus = GradingCommitStatus.Pending;
        CommitError = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Tenta reservar o item para uma etapa de processamento interno.
    /// Itens já concluídos não podem ser reclamados novamente. Um lease ativo
    /// de qualquer worker bloqueia a operação até expirar ou ser liberado.
    /// </summary>
    public bool TryAcquireLease(string workerId, DateTimeOffset now, TimeSpan leaseDuration)
    {
        ValidateLeaseArguments(workerId, leaseDuration);
        if (Status != GradingItemStatus.Pending ||
            NextAttemptAt is { } nextAttemptAt && nextAttemptAt > now ||
            LeaseUntil is { } leaseUntil && leaseUntil > now)
        {
            return false;
        }

        AttemptCount++;
        LeaseOwner = workerId.Trim();
        LeaseUntil = now.Add(leaseDuration);
        NextAttemptAt = null;
        UpdatedAt = now;
        return true;
    }

    public bool RenewLease(string workerId, DateTimeOffset now, TimeSpan leaseDuration)
    {
        ValidateLeaseArguments(workerId, leaseDuration);
        if (Status != GradingItemStatus.Pending ||
            !string.Equals(LeaseOwner, workerId.Trim(), StringComparison.Ordinal) ||
            LeaseUntil is not { } leaseUntil || leaseUntil <= now)
        {
            return false;
        }

        LeaseUntil = now.Add(leaseDuration);
        UpdatedAt = now;
        return true;
    }

    public bool ReleaseLease(
        string workerId,
        DateTimeOffset now,
        string? errorCode = null,
        DateTimeOffset? nextAttemptAt = null)
    {
        if (string.IsNullOrWhiteSpace(workerId) ||
            !string.Equals(LeaseOwner, workerId.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        LeaseOwner = null;
        LeaseUntil = null;
        LastErrorCode = string.IsNullOrWhiteSpace(errorCode)
            ? null
            : errorCode.Trim()[..Math.Min(120, errorCode.Trim().Length)];
        NextAttemptAt = nextAttemptAt;
        UpdatedAt = now;
        return true;
    }

    public bool RecoverExpiredLease(DateTimeOffset now)
    {
        if (Status != GradingItemStatus.Pending ||
            LeaseUntil is not { } leaseUntil ||
            leaseUntil > now)
        {
            return false;
        }

        LeaseOwner = null;
        LeaseUntil = null;
        NextAttemptAt = now;
        UpdatedAt = now;
        return true;
    }

    private static void ValidateLeaseArguments(string workerId, TimeSpan leaseDuration)
    {
        if (string.IsNullOrWhiteSpace(workerId))
        {
            throw new ArgumentException("O worker do item e obrigatorio.", nameof(workerId));
        }

        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "A duracao do lease deve ser positiva.");
        }
    }
}
