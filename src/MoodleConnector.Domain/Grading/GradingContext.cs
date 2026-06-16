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

        if (string.IsNullOrWhiteSpace(criteria) &&
            string.IsNullOrWhiteSpace(rubricDescription) &&
            string.IsNullOrWhiteSpace(assignmentStatement))
        {
            blockers.Add("Critérios, rubrica ou enunciado não informados. Análise baseada apenas no conteúdo da submissão.");
        }

        if (string.IsNullOrWhiteSpace(submissionText))
        {
            if (attachedFiles is null || attachedFiles.Count == 0)
            {
                blockers.Add("Submissão não disponível. Não há conteúdo legível para análise.");
            }
            else
            {
                var fileNames = string.Join(", ", attachedFiles.Select(f => f.FileName));
                if (attachedFiles.Any(f => string.IsNullOrWhiteSpace(f.ExtractedText) && f.IsSupported))
                {
                    blockers.Add($"Submissão sem conteúdo legível. Não foi possível extrair texto dos arquivos suportados ({fileNames}). Motivos comuns: PDF escaneado, arquivo sem texto extraível, corrompido ou protegido por senha.");
                }
                else if (attachedFiles.Any(f => !f.IsSupported))
                {
                    blockers.Add($"Submissão sem conteúdo legível. Os arquivos anexados ({fileNames}) estão em formatos não suportados para extração de texto.");
                }
                else
                {
                    blockers.Add($"Submissão sem conteúdo legível. Os arquivos anexados ({fileNames}) apresentam ausência real de dados mínimos de texto para análise.");
                }
            }
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
