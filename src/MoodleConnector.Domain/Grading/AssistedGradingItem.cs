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
        string? privateNotesToTeacher = null)
    {
        if (suggestedGrade < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(suggestedGrade), "A nota sugerida nao pode ser negativa.");
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
        string? reviewNotes = null)
    {
        if (finalGrade < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(finalGrade), "A nota final nao pode ser negativa.");
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
}
