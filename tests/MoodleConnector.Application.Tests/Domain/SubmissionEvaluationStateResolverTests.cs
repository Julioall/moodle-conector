using MoodleConnector.Domain;

namespace MoodleConnector.Application.Tests.Domain;

/// <summary>
/// Testes de regressão pedagógica para o SubmissionEvaluationStateResolver.
/// Os casos de fixture FIEG/SENAI (courseId 33446, assignmentIds 117485/117487)
/// são modelados com os dados de evidência correspondentes, sem dependência de rede.
/// </summary>
public sealed class SubmissionEvaluationStateResolverTests
{
    // =========================================================================
    // Atividade com nota — courseId 33446, assignmentId 117485
    // =========================================================================

    [Fact]
    public void Fixture_FIEG_Atividade_Com_Nota_StudentId440750_Deve_Ser_GradedNumeric()
    {
        // studentId 440750 → GRADED_NUMERIC (possui nota numérica)
        var evidence = new SubmissionEvaluationEvidence(
            HasSubmission: true,
            GradeRaw: 42m,
            GradedDateGraded: DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(),
            Feedback: "Boa entrega.",
            ReviewEvidenceAvailable: true);

        var state = SubmissionEvaluationStateResolver.Resolve(evidence);

        Assert.Equal(SubmissionEvaluationState.GradedNumeric, state);
        Assert.False(SubmissionEvaluationStateResolver.NeedsGrading(state));
    }

    [Fact]
    public void Fixture_FIEG_Atividade_Com_Nota_StudentId440752_Deve_Ser_AwaitingGrading()
    {
        // studentId 440752 → AWAITING_GRADING (entrega sem nota nem feedback)
        var evidence = new SubmissionEvaluationEvidence(
            HasSubmission: true,
            GradeRaw: null,
            GradedDateGraded: null,
            Feedback: null,
            ReviewEvidenceAvailable: true);

        var state = SubmissionEvaluationStateResolver.Resolve(evidence);

        Assert.Equal(SubmissionEvaluationState.AwaitingGrading, state);
        Assert.True(SubmissionEvaluationStateResolver.NeedsGrading(state));
    }

    [Fact]
    public void Fixture_FIEG_Atividade_Com_Nota_StudentId440739_Deve_Ser_NotSubmitted()
    {
        // studentId 440739 → NOT_SUBMITTED (hasSubmission = false)
        var evidence = new SubmissionEvaluationEvidence(
            HasSubmission: false,
            GradeRaw: null,
            GradedDateGraded: null,
            Feedback: null,
            ReviewEvidenceAvailable: false);

        var state = SubmissionEvaluationStateResolver.Resolve(evidence);

        Assert.Equal(SubmissionEvaluationState.NotSubmitted, state);
        Assert.False(SubmissionEvaluationStateResolver.NeedsGrading(state));
    }

    // =========================================================================
    // Atividade somente com feedback — courseId 33446, assignmentId 117487
    // =========================================================================

    [Fact]
    public void Fixture_FIEG_Atividade_Feedback_StudentId440752_Deve_Ser_ReviewedWithFeedback()
    {
        // studentId 440752 → REVIEWED_WITH_FEEDBACK (feedback presente, sem nota numérica)
        var evidence = new SubmissionEvaluationEvidence(
            HasSubmission: true,
            GradeRaw: null,
            GradedDateGraded: DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeSeconds(),
            Feedback: "Excelente argumentação.",
            ReviewEvidenceAvailable: true);

        var state = SubmissionEvaluationStateResolver.Resolve(evidence);

        Assert.Equal(SubmissionEvaluationState.ReviewedWithFeedback, state);
        Assert.False(SubmissionEvaluationStateResolver.NeedsGrading(state));
    }

    [Fact]
    public void Fixture_FIEG_Atividade_Feedback_ComDataGradedESemFeedbackTexto_Deve_Ser_ReviewedWithFeedback()
    {
        // Caso onde apenas a data de correção está presente (feedback pode estar em outro campo)
        var evidence = new SubmissionEvaluationEvidence(
            HasSubmission: true,
            GradeRaw: null,
            GradedDateGraded: DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(),
            Feedback: string.Empty,
            ReviewEvidenceAvailable: true);

        var state = SubmissionEvaluationStateResolver.Resolve(evidence);

        Assert.Equal(SubmissionEvaluationState.ReviewedWithFeedback, state);
    }

    [Fact]
    public void Fixture_FIEG_Atividade_Feedback_StudentId440739_Deve_Ser_AwaitingGrading()
    {
        // studentId 440739 na atividade feedback → AWAITING_GRADING
        var evidence = new SubmissionEvaluationEvidence(
            HasSubmission: true,
            GradeRaw: null,
            GradedDateGraded: null,
            Feedback: null,
            ReviewEvidenceAvailable: true);

        var state = SubmissionEvaluationStateResolver.Resolve(evidence);

        Assert.Equal(SubmissionEvaluationState.AwaitingGrading, state);
        Assert.True(SubmissionEvaluationStateResolver.NeedsGrading(state));
    }

    // =========================================================================
    // Estado UNKNOWN — sem evidência suficiente
    // =========================================================================

    [Fact]
    public void SemSubmissao_HasSubmissionNull_Deve_Ser_Unknown()
    {
        // hasSubmission null → UNKNOWN (sem dados suficientes para classificar)
        var evidence = new SubmissionEvaluationEvidence(
            HasSubmission: null,
            GradeRaw: null,
            GradedDateGraded: null,
            Feedback: null,
            ReviewEvidenceAvailable: false);

        var state = SubmissionEvaluationStateResolver.Resolve(evidence);

        Assert.Equal(SubmissionEvaluationState.Unknown, state);
        Assert.False(SubmissionEvaluationStateResolver.NeedsGrading(state));
    }

    [Fact]
    public void Submissao_EntregueSemNotaMesmoSemLeituraDeGradebook_Deve_Ser_AwaitingGrading()
    {
        // Uma submissão entregue sem nota, data de correção ou feedback ainda
        // precisa entrar na fila, mesmo quando o gradebook não foi lido.
        var evidence = new SubmissionEvaluationEvidence(
            HasSubmission: true,
            GradeRaw: null,
            GradedDateGraded: null,
            Feedback: null,
            ReviewEvidenceAvailable: false);

        var state = SubmissionEvaluationStateResolver.Resolve(evidence);

        Assert.Equal(SubmissionEvaluationState.AwaitingGrading, state);
        Assert.True(SubmissionEvaluationStateResolver.NeedsGrading(state));
    }

    // =========================================================================
    // Validação de seleção de lote — submissões que NÃO devem entrar no lote
    // =========================================================================

    [Theory]
    [InlineData(SubmissionEvaluationState.GradedNumeric)]
    [InlineData(SubmissionEvaluationState.ReviewedWithFeedback)]
    [InlineData(SubmissionEvaluationState.NotSubmitted)]
    [InlineData(SubmissionEvaluationState.Unknown)]
    public void NeedsGrading_DeveRetornarFalse_ParaEstadosQueNaoEntramNoBatch(SubmissionEvaluationState state)
    {
        // Somente AwaitingGrading deve entrar no lote de correção
        Assert.False(SubmissionEvaluationStateResolver.NeedsGrading(state));
    }

    [Fact]
    public void NeedsGrading_DeveRetornarTrue_ApenasParaAwaitingGrading()
    {
        Assert.True(SubmissionEvaluationStateResolver.NeedsGrading(SubmissionEvaluationState.AwaitingGrading));
    }

    // =========================================================================
    // Precedência de regras — nota prevalece sobre feedback
    // =========================================================================

    [Fact]
    public void ComNotaEFeedback_DevePrioritizarGradedNumeric()
    {
        // Quando há nota numérica E feedback, o estado deve ser GradedNumeric
        var evidence = new SubmissionEvaluationEvidence(
            HasSubmission: true,
            GradeRaw: 10m,
            GradedDateGraded: DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(),
            Feedback: "Bom trabalho.",
            ReviewEvidenceAvailable: true);

        var state = SubmissionEvaluationStateResolver.Resolve(evidence);

        Assert.Equal(SubmissionEvaluationState.GradedNumeric, state);
    }

    [Fact]
    public void StatusGradedSemNota_DeveSerReviewedWithFeedback()
    {
        var state = SubmissionEvaluationStateResolver.Resolve(new SubmissionEvaluationEvidence(
            HasSubmission: true,
            GradeRaw: null,
            GradedDateGraded: null,
            Feedback: null,
            ReviewEvidenceAvailable: false,
            GradingStatus: "graded"));

        Assert.Equal(SubmissionEvaluationState.ReviewedWithFeedback, state);
        Assert.False(SubmissionEvaluationStateResolver.NeedsGrading(state));
    }

    [Fact]
    public void GraderPositivoEmAtividadeSemNota_DeveSerReviewedWithFeedback()
    {
        var state = SubmissionEvaluationStateResolver.Resolve(new SubmissionEvaluationEvidence(
            HasSubmission: true,
            GradeRaw: null,
            GradedDateGraded: null,
            Feedback: null,
            ReviewEvidenceAvailable: true,
            GraderId: 317295,
            GradeTimeModified: 1787075877,
            SubmissionTimeModified: 1787075000));

        Assert.Equal(SubmissionEvaluationState.ReviewedWithFeedback, state);
        Assert.False(SubmissionEvaluationStateResolver.NeedsGrading(state));
    }

    [Fact]
    public void GraderMenosUmEmAtividadeSemNota_DeveSerAwaitingGrading()
    {
        var state = SubmissionEvaluationStateResolver.Resolve(new SubmissionEvaluationEvidence(
            HasSubmission: true,
            GradeRaw: null,
            GradedDateGraded: null,
            Feedback: null,
            ReviewEvidenceAvailable: true,
            GraderId: -1,
            GradeTimeModified: 0,
            SubmissionTimeModified: 1787221494));

        Assert.Equal(SubmissionEvaluationState.AwaitingGrading, state);
        Assert.True(SubmissionEvaluationStateResolver.NeedsGrading(state));
    }

    [Fact]
    public void ArgumentNullException_QuandoEvidenciaEhNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SubmissionEvaluationStateResolver.Resolve(null!));
    }
}
