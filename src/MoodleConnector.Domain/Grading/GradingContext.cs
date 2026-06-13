namespace MoodleConnector.Domain.Grading;

/// <summary>
/// Contexto mínimo necessário para que um serviço de análise sugira nota e feedback.
/// Imutável após montagem; bloqueadores indicam o que impede análise confiável.
/// </summary>
public sealed class GradingContext
{
    private GradingContext()
    {
    }

    public Guid GradingItemId { get; private init; }

    public Guid BatchId { get; private init; }

    public string CourseId { get; private init; } = string.Empty;

    public string AssignmentId { get; private init; } = string.Empty;

    public string? SubmissionId { get; private init; }

    public string StudentId { get; private init; } = string.Empty;

    public string? AssignmentStatement { get; private init; }

    public string? Criteria { get; private init; }

    public string? RubricDescription { get; private init; }

    public decimal? MaxGrade { get; private init; }

    public string? GradeScale { get; private init; }

    public string? SubmissionText { get; private init; }

    public IReadOnlyList<GradingFileInfo> AttachedFiles { get; private init; } = [];

    public string? CourseMaterials { get; private init; }

    public string? TeacherInstructions { get; private init; }

    public IReadOnlyList<string> Blockers { get; private init; } = [];

    public bool HasMinimumContext =>
        Blockers.Count == 0 &&
        (!string.IsNullOrWhiteSpace(SubmissionText) || AttachedFiles.Count > 0);

    public static GradingContext Build(
        Guid gradingItemId,
        Guid batchId,
        string courseId,
        string assignmentId,
        string? submissionId,
        string studentId,
        string? assignmentStatement,
        string? criteria,
        string? rubricDescription,
        decimal? maxGrade,
        string? gradeScale,
        string? submissionText,
        IReadOnlyList<GradingFileInfo>? attachedFiles,
        string? courseMaterials,
        string? teacherInstructions)
    {
        var blockers = new List<string>();

        if (string.IsNullOrWhiteSpace(criteria) && string.IsNullOrWhiteSpace(rubricDescription))
        {
            blockers.Add("Critérios ou rubrica não informados. Não é possível sugerir nota fundamentada.");
        }

        if (string.IsNullOrWhiteSpace(submissionText) && (attachedFiles is null || attachedFiles.Count == 0))
        {
            blockers.Add("Submissão não disponível. Não há conteúdo legível para análise.");
        }

        if (maxGrade is null && string.IsNullOrWhiteSpace(gradeScale))
        {
            blockers.Add("Escala de nota não identificada. Não é possível calcular nota sugerida.");
        }

        return new GradingContext
        {
            GradingItemId = gradingItemId,
            BatchId = batchId,
            CourseId = courseId,
            AssignmentId = assignmentId,
            SubmissionId = submissionId,
            StudentId = studentId,
            AssignmentStatement = assignmentStatement,
            Criteria = criteria,
            RubricDescription = rubricDescription,
            MaxGrade = maxGrade,
            GradeScale = gradeScale,
            SubmissionText = submissionText,
            AttachedFiles = attachedFiles ?? [],
            CourseMaterials = courseMaterials,
            TeacherInstructions = teacherInstructions,
            Blockers = blockers
        };
    }
}

public sealed record GradingFileInfo(
    string FileName,
    string? MimeType,
    long? FileSizeBytes,
    string? Sha256,
    string? ExtractedText,
    bool IsSupported);
