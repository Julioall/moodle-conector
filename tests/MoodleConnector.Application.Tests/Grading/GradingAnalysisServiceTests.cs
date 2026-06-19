using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;

namespace MoodleConnector.Application.Tests.Grading;

public sealed class GradingAnalysisServiceTests
{
    private readonly StructuredGradingAnalysisService _sut = new();

    [Fact]
    public async Task AnalyzeAsync_SubmissaoVazia_RetornaBlockedEmptySubmission()
    {
        var request = new GradingAnalysisRequest(
            AssignmentName: "SA 01",
            MaxGrade: 10m,
            ActivityDescription: "Descricao da atividade.",
            RubricOrCriteria: "Criterio A; Criterio B",
            TeacherInstructions: null,
            SubmissionText: "",
            FileHashes: []);

        var result = await _sut.AnalyzeAsync(request, CancellationToken.None);

        Assert.Equal(AnalysisStatus.BlockedEmptySubmission, result.AnalysisStatus);
        Assert.Null(result.SuggestedGrade);
        Assert.Equal(0m, result.Confidence);
        Assert.NotEmpty(result.Blocks);
    }

    [Fact]
    public async Task AnalyzeAsync_SemCriteriosNemDescricao_GeraRascunhoComBaixaConfianca()
    {
        var request = new GradingAnalysisRequest(
            AssignmentName: "SA 01",
            MaxGrade: 10m,
            ActivityDescription: null,
            RubricOrCriteria: null,
            TeacherInstructions: null,
            SubmissionText: "O estudante respondeu a atividade com um texto abrangente.",
            FileHashes: []);

        var result = await _sut.AnalyzeAsync(request, CancellationToken.None);

        Assert.Equal(AnalysisStatus.Draft, result.AnalysisStatus);
        Assert.Null(result.SuggestedGrade);
        Assert.True(result.Confidence > 0m);
        Assert.True(result.Confidence < 0.3m);
        Assert.NotEmpty(result.FeedbackToStudent!);
        Assert.Contains("Revisao manual obrigatoria", result.PrivateNotesToTeacher, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Blocks);
    }

    [Fact]
    public async Task AnalyzeAsync_SemMaxGrade_GeraRascunhoSemNotaSugerida()
    {
        var request = new GradingAnalysisRequest(
            AssignmentName: "SA 01",
            MaxGrade: 0m,
            ActivityDescription: null,
            RubricOrCriteria: "Criterio A",
            TeacherInstructions: null,
            SubmissionText: "Resposta do estudante.",
            FileHashes: []);

        var result = await _sut.AnalyzeAsync(request, CancellationToken.None);

        Assert.Equal(AnalysisStatus.Draft, result.AnalysisStatus);
        Assert.Null(result.SuggestedGrade);
        Assert.NotEmpty(result.FeedbackToStudent!);
        Assert.Contains("Escala de nota nao identificada", result.PrivateNotesToTeacher, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Blocks);
    }

    [Fact]
    public async Task AnalyzeAsync_SubmissaoComCriterios_RetornaDraftComNotaSugerida()
    {
        var request = new GradingAnalysisRequest(
            AssignmentName: "SA 01",
            MaxGrade: 10m,
            ActivityDescription: "Analise de riscos em ambiente industrial.",
            RubricOrCriteria: "Identifica riscos fisicos; Descreve medidas preventivas; Utiliza normas tecnicas",
            TeacherInstructions: "Linguagem acolhedora.",
            SubmissionText: "O estudante identificou os principais riscos fisicos no ambiente industrial. " +
                            "Foram descritas diversas medidas preventivas conforme as normas tecnicas vigentes. " +
                            "A abordagem foi clara e bem estruturada.",
            FileHashes: ["abc123", "def456"]);

        var result = await _sut.AnalyzeAsync(request, CancellationToken.None);

        Assert.Equal(AnalysisStatus.Draft, result.AnalysisStatus);
        Assert.NotNull(result.SuggestedGrade);
        Assert.True(result.SuggestedGrade >= 0m);
        Assert.True(result.SuggestedGrade <= 10m);
        Assert.True(result.Confidence > 0m);
        Assert.NotEmpty(result.FeedbackToStudent!);
        Assert.NotEmpty(result.PrivateNotesToTeacher!);
        Assert.Equal(4, result.CriterionAnalysis.Count);
        Assert.Empty(result.Blocks);

        // Feedback nao deve expor dados PII ou tokens
        Assert.DoesNotContain("token", result.FeedbackToStudent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_CriteriosComBarraVertical_ParseaCorretamente()
    {
        var request = new GradingAnalysisRequest(
            AssignmentName: "SA 02",
            MaxGrade: 100m,
            ActivityDescription: null,
            RubricOrCriteria: "Criterio 1 | Criterio 2 | Criterio 3 | Criterio 4",
            TeacherInstructions: null,
            SubmissionText: "Resposta com conteudo relevante sobre os criterios propostos.",
            FileHashes: []);

        var result = await _sut.AnalyzeAsync(request, CancellationToken.None);

        Assert.Equal(AnalysisStatus.Draft, result.AnalysisStatus);
        Assert.Equal(4, result.CriterionAnalysis.Count);
        Assert.All(result.CriterionAnalysis, c =>
        {
            Assert.NotNull(c.CriterionId);
            Assert.NotNull(c.CriterionText);
            Assert.NotNull(c.MaxPoints);
            Assert.NotNull(c.SuggestedPoints);
        });
    }

    [Fact]
    public async Task AnalyzeAsync_SubmissaoSemCoberturaDeCriterios_MarcarParaRevisao()
    {
        var request = new GradingAnalysisRequest(
            AssignmentName: "SA 03",
            MaxGrade: 10m,
            ActivityDescription: null,
            RubricOrCriteria: "Identificacao de riscos quimicos especificos",
            TeacherInstructions: null,
            SubmissionText: "O aluno fez uma entrega genérica sem mencionar nenhum aspecto especifico.",
            FileHashes: []);

        var result = await _sut.AnalyzeAsync(request, CancellationToken.None);

        Assert.Equal(AnalysisStatus.Draft, result.AnalysisStatus);
        var criterion = Assert.Single(result.CriterionAnalysis);
        Assert.True(criterion.TeacherReviewRequired);
    }

    [Fact]
    public async Task AnalyzeAsync_BaixaConfianca_IncluiObservacaoPrivada()
    {
        var request = new GradingAnalysisRequest(
            AssignmentName: "SA 04",
            MaxGrade: 10m,
            ActivityDescription: null,
            RubricOrCriteria: "Descrever riscos fisicos; Apresentar medidas preventivas; Relacionar normas tecnicas",
            TeacherInstructions: null,
            SubmissionText: "Resposta curta.",
            FileHashes: []);

        var result = await _sut.AnalyzeAsync(request, CancellationToken.None);

        Assert.Equal(AnalysisStatus.Draft, result.AnalysisStatus);
        Assert.True(result.Confidence < 0.5m);
        Assert.Contains("Baixa confianca", result.PrivateNotesToTeacher, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_ComDescricaoComoFallback_DelegaParaIA()
    {
        var request = new GradingAnalysisRequest(
            AssignmentName: "SA 05",
            MaxGrade: 10m,
            ActivityDescription: "O aluno deve elaborar um plano de continuidade; considerar riscos operacionais; propor estratégias de mitigação. O aluno devera escrever uma redacao.",
            RubricOrCriteria: null,
            TeacherInstructions: null,
            SubmissionText: "O estudante elaborou um plano de continuidade abordando os riscos operacionais " +
                            "e propondo estrategias de mitigacao para garantir a resiliencia do negocio.",
            FileHashes: []);

        var result = await _sut.AnalyzeAsync(request, CancellationToken.None);

        Assert.Equal(AnalysisStatus.Draft, result.AnalysisStatus);
        // No cenário Approximate com MaxGrade disponível, agora gera critérios e nota por critério
        Assert.NotNull(result.SuggestedGrade);
        Assert.True(result.SuggestedGrade >= 0m);
        Assert.True(result.SuggestedGrade <= 10m);
        Assert.True(result.Confidence > 0m);
        Assert.True(result.Confidence <= 0.5m);
        Assert.Contains("INSTRUCAO PARA A IA", result.PrivateNotesToTeacher, StringComparison.OrdinalIgnoreCase);
        // Agora gera critérios parseados do enunciado quando MaxGrade está disponível
        Assert.NotEmpty(result.CriterionAnalysis);
        Assert.Empty(result.Blocks);
    }

    [Fact]
    public async Task AnalyzeAsync_ComDescricaoMuitoPequena_BloqueiaAnalise()
    {
        var request = new GradingAnalysisRequest(
            AssignmentName: "SA 06",
            MaxGrade: 0m,
            ActivityDescription: "Atividade sobre riscos fisicos. Valor da atividade: 16 pontos.",
            RubricOrCriteria: null,
            TeacherInstructions: null,
            SubmissionText: "O estudante identificou riscos fisicos e propôs medidas preventivas adequadas ao contexto.",
            FileHashes: []);

        var result = await _sut.AnalyzeAsync(request, CancellationToken.None);

        Assert.Equal(AnalysisStatus.BlockedMissingCriteria, result.AnalysisStatus);
        Assert.Null(result.SuggestedGrade);
        Assert.NotEmpty(result.Blocks);
        Assert.Contains("insuficiente", result.PrivateNotesToTeacher, StringComparison.OrdinalIgnoreCase);
    }
}

