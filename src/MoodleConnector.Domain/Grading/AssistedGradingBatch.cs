namespace MoodleConnector.Domain.Grading;

public sealed class AssistedGradingBatch
{
    private AssistedGradingBatch()
    {
    }

    public Guid Id { get; private init; } = Guid.NewGuid();

    public long CourseId { get; private init; }

    public IReadOnlyList<long> AssignmentIds { get; private init; } = [];

    public string CreatedBySubject { get; private init; } = string.Empty;

    public long? CreatedByMoodleUserId { get; private init; }

    public GradingBatchStatus Status { get; private set; } = GradingBatchStatus.Pending;

    public int TotalItems { get; private init; }

    /// <summary>
    /// Instruções explícitas do professor que devem acompanhar o lote até o worker,
    /// sem depender do estado transitório da requisição de criação.
    /// </summary>
    public string? TeacherInstructions { get; private init; }

    /// <summary>
    /// Prioridade declarada para a futura fila durável. Enquanto a fila PostgreSQL
    /// não estiver habilitada, o valor é apenas persistido e não altera a ordem local.
    /// </summary>
    public string Priority { get; private init; } = "normal";

    public int ProcessedItems { get; private set; }

    public int ReadyItems { get; private set; }

    public int BlockedItems { get; private set; }

    public int FailedItems { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public static AssistedGradingBatch Create(
        long courseId,
        IReadOnlyCollection<long> assignmentIds,
        string createdBySubject,
        long? createdByMoodleUserId,
        int totalItems,
        string? teacherInstructions = null,
        string priority = "normal")
    {
        if (courseId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(courseId), "O curso deve ser um identificador Moodle positivo.");
        }

        if (assignmentIds.Count == 0)
        {
            throw new ArgumentException("Informe pelo menos uma tarefa.", nameof(assignmentIds));
        }

        if (string.IsNullOrWhiteSpace(createdBySubject))
        {
            throw new ArgumentException("O usuario criador e obrigatorio.", nameof(createdBySubject));
        }

        if (totalItems < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalItems), "O total de itens nao pode ser negativo.");
        }

        var normalizedPriority = string.IsNullOrWhiteSpace(priority)
            ? "normal"
            : priority.Trim().ToLowerInvariant();
        if (normalizedPriority is not ("low" or "normal" or "high"))
        {
            throw new ArgumentException(
                "A prioridade deve ser low, normal ou high.",
                nameof(priority));
        }

        var normalizedTeacherInstructions = string.IsNullOrWhiteSpace(teacherInstructions)
            ? null
            : teacherInstructions.Trim();
        if (normalizedTeacherInstructions is not null && normalizedTeacherInstructions.Length > 8000)
        {
            throw new ArgumentException(
                "As instrucoes do professor excedem o limite de 8000 caracteres.",
                nameof(teacherInstructions));
        }

        return new AssistedGradingBatch
        {
            CourseId = courseId,
            AssignmentIds = assignmentIds.Distinct().ToArray(),
            CreatedBySubject = createdBySubject.Trim(),
            CreatedByMoodleUserId = createdByMoodleUserId,
            TotalItems = totalItems,
            TeacherInstructions = normalizedTeacherInstructions,
            Priority = normalizedPriority
        };
    }

    public void UpdateCounters(
        int processedItems,
        int readyItems,
        int blockedItems,
        int failedItems)
    {
        if (processedItems < 0 || readyItems < 0 || blockedItems < 0 || failedItems < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processedItems), "Contadores de lote nao podem ser negativos.");
        }

        ProcessedItems = processedItems;
        ReadyItems = readyItems;
        BlockedItems = blockedItems;
        FailedItems = failedItems;
        Status = processedItems >= TotalItems
            ? GradingBatchStatus.ReadyForReview
            : GradingBatchStatus.Processing;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCompleted()
    {
        if (Status == GradingBatchStatus.Cancelled)
        {
            return;
        }

        Status = GradingBatchStatus.Completed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel()
    {
        if (Status is GradingBatchStatus.Completed or GradingBatchStatus.Cancelled)
        {
            return;
        }

        Status = GradingBatchStatus.Cancelled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
