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
            RubricOrCriteria: "Identifica riscos fisicos no ambiente industrial",
            TeacherInstructions: null,
            SubmissionText: "Resposta do estudante sobre riscos fisicos.",
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
            RubricOrCriteria: "Identifica riscos fisicos no ambiente; Descreve medidas preventivas adequadas; Utiliza normas tecnicas como referencia",
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
        Assert.True(result.CriterionAnalysis.Count >= 3, $"Expected >= 3 criteria, got {result.CriterionAnalysis.Count}");
        Assert.Empty(result.Blocks);

        // Feedback deve ser natural, sem frases genericas
        Assert.DoesNotContain("evidenciou o aspecto esperado", result.FeedbackToStudent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Pontos positivos identificados", result.FeedbackToStudent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("parecer preliminar assistido", result.FeedbackToStudent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", result.FeedbackToStudent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("o texto aborda elementos relacionados", result.FeedbackToStudent, StringComparison.OrdinalIgnoreCase);

        // Novo formato humanizado: saudação + nota sugerida
        Assert.StartsWith("Olá", result.FeedbackToStudent);
        Assert.Contains("Nota sugerida:", result.FeedbackToStudent);
    }

    [Fact]
    public async Task AnalyzeAsync_CriteriosComBarraVertical_ParseaCorretamente()
    {
        var request = new GradingAnalysisRequest(
            AssignmentName: "SA 02",
            MaxGrade: 100m,
            ActivityDescription: null,
            RubricOrCriteria: "Identifica riscos fisicos no ambiente | Descreve medidas preventivas adequadas | Utiliza normas tecnicas como referencia | Propoe acoes corretivas coerentes",
            TeacherInstructions: null,
            SubmissionText: "Resposta com conteudo relevante sobre os criterios propostos.",
            FileHashes: []);

        var result = await _sut.AnalyzeAsync(request, CancellationToken.None);

        Assert.Equal(AnalysisStatus.Draft, result.AnalysisStatus);
        Assert.True(result.CriterionAnalysis.Count >= 4, $"Expected >= 4 criteria, got {result.CriterionAnalysis.Count}");
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

    // ========================
    // Validações de Qualidade Pedagógica
    // ========================

    [Fact]
    public async Task AnalyzeAsync_CriteriosComMenosDe3PalavrasUteis_SaoRejeitados()
    {
        var request = new GradingAnalysisRequest(
            AssignmentName: "SA 07",
            MaxGrade: 10m,
            ActivityDescription: null,
            RubricOrCriteria: "ITIL; Etapa 1; língua portuguesa; Identifica riscos fisicos no ambiente industrial",
            TeacherInstructions: null,
            SubmissionText: "O estudante identificou riscos fisicos relevantes no ambiente industrial.",
            FileHashes: []);

        var result = await _sut.AnalyzeAsync(request, CancellationToken.None);

        // Fragmentos com <3 palavras úteis devem ter sido rejeitados
        Assert.All(result.CriterionAnalysis, c =>
        {
            var usefulWords = c.CriterionText.Split([' ', ',', ';', '.', ':'], StringSplitOptions.RemoveEmptyEntries)
                .Count(w => w.Length > 3);
            Assert.True(usefulWords >= 3, $"Criterio fragmentado nao filtrado: '{c.CriterionText}'");
        });
    }

    [Fact]
    public async Task AnalyzeAsync_FragmentosIsolados_NaoGeramCriterios()
    {
        var request = new GradingAnalysisRequest(
            AssignmentName: "SA 08",
            MaxGrade: 10m,
            ActivityDescription: null,
            RubricOrCriteria: "Etapa 1; ITIL; língua; avaliação",
            TeacherInstructions: null,
            SubmissionText: "O estudante apresentou um plano detalhado.",
            FileHashes: []);

        var result = await _sut.AnalyzeAsync(request, CancellationToken.None);

        // Todos os critérios fornecidos são fragmentos (<3 palavras úteis) → 0 critérios parseados
        Assert.Empty(result.CriterionAnalysis);
    }

    [Fact]
    public async Task AnalyzeAsync_FeedbackNaoContemFrasesGenericasProibidas()
    {
        var request = new GradingAnalysisRequest(
            AssignmentName: "SA 09",
            MaxGrade: 10m,
            ActivityDescription: null,
            RubricOrCriteria: "Identifica riscos fisicos no ambiente; Descreve medidas preventivas adequadas; Utiliza normas tecnicas como referencia",
            TeacherInstructions: null,
            SubmissionText: "O estudante identificou riscos fisicos e descreveu medidas preventivas usando normas tecnicas.",
            FileHashes: []);

        var result = await _sut.AnalyzeAsync(request, CancellationToken.None);

        Assert.NotNull(result.FeedbackToStudent);
        var feedback = result.FeedbackToStudent!;

        // Frases genéricas proibidas
        Assert.DoesNotContain("evidenciou o aspecto esperado na resolucao", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("parecer preliminar assistido", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cobertura parcial", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Revisao recomendada", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Pontos positivos identificados", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Aspectos para desenvolvimento", feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_FeedbackContemMelhoriasConcretasQuandoHaLacunas()
    {
        var request = new GradingAnalysisRequest(
            AssignmentName: "SA 10",
            MaxGrade: 10m,
            ActivityDescription: null,
            RubricOrCriteria: "Identifica riscos fisicos no ambiente industrial; Elabora plano de contingencia estruturado; Apresenta cronograma de implementacao detalhado",
            TeacherInstructions: null,
            SubmissionText: "O aluno identificou riscos fisicos basicos.",
            FileHashes: []);

        var result = await _sut.AnalyzeAsync(request, CancellationToken.None);

        Assert.NotNull(result.FeedbackToStudent);
        // Quando há lacunas, feedback deve conter orientação de melhoria
        Assert.Contains("melhorar", result.FeedbackToStudent, StringComparison.OrdinalIgnoreCase);
        // Novo formato: deve ter saudação
        Assert.StartsWith("Olá", result.FeedbackToStudent);
    }

    [Fact]
    public async Task AnalyzeAsync_FeedbackEmFormatoParagrafoNaoLista()
    {
        var request = new GradingAnalysisRequest(
            AssignmentName: "SA 11",
            MaxGrade: 10m,
            ActivityDescription: null,
            RubricOrCriteria: "Identifica riscos fisicos no ambiente; Descreve medidas preventivas adequadas; Utiliza normas tecnicas como referencia",
            TeacherInstructions: null,
            SubmissionText: "O estudante identificou riscos fisicos e propôs medidas preventivas baseadas em normas tecnicas.",
            FileHashes: []);

        var result = await _sut.AnalyzeAsync(request, CancellationToken.None);

        Assert.NotNull(result.FeedbackToStudent);
        // Feedback não deve ter formatação tipo markdown bold
        Assert.DoesNotContain("**Pontos", result.FeedbackToStudent);
        Assert.DoesNotContain("**Aspectos", result.FeedbackToStudent);
        // Feedback humanizado usa "- " para pontos positivos, mas não para estrutura mecânica
        Assert.DoesNotContain("o texto aborda elementos relacionados", result.FeedbackToStudent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_EvidenciaCitaElementosReaisDaEntrega()
    {
        var request = new GradingAnalysisRequest(
            AssignmentName: "SA 12",
            MaxGrade: 10m,
            ActivityDescription: null,
            RubricOrCriteria: "Elabora plano de gerenciamento de eventos de TI; Identifica incidentes e problemas criticos",
            TeacherInstructions: null,
            SubmissionText: "O plano de gerenciamento apresenta eventos de TI e descreve incidentes criticos com problemas de infraestrutura.",
            FileHashes: []);

        var result = await _sut.AnalyzeAsync(request, CancellationToken.None);

        // Evidências devem conter palavras-chave reais da entrega
        var allEvidence = string.Join(" ", result.CriterionAnalysis
            .Select(c => c.EvidenceFound ?? "")
            .Where(e => !string.IsNullOrWhiteSpace(e)));
        Assert.Contains("gerenciamento", allEvidence, StringComparison.OrdinalIgnoreCase);
    }

    // ========================
    // Validações de Feedback Humanizado
    // ========================

    [Fact]
    public async Task AnalyzeAsync_FeedbackHumanizado_NaoContemPalavrasChaveSoltas()
    {
        var request = new GradingAnalysisRequest(
            AssignmentName: "Envio SAP 01 - Etapa 1",
            MaxGrade: 49m,
            ActivityDescription: null,
            RubricOrCriteria: "Elaborar um plano de gerenciamento de eventos de TI; Indicar eventos que podem impactar a operacao; Relacionar o plano as orientacoes da ITIL; Adequar o texto conforme norma culta da lingua portuguesa",
            TeacherInstructions: null,
            SubmissionText: "O plano de gerenciamento de eventos de TI apresenta os principais eventos " +
                            "que podem impactar a operacao da empresa, incluindo falhas de hardware, " +
                            "problemas de rede e incidentes de seguranca. O plano segue as orientacoes da ITIL " +
                            "para monitoramento e tratamento de eventos.",
            FileHashes: []);

        var result = await _sut.AnalyzeAsync(request, CancellationToken.None);

        Assert.NotNull(result.FeedbackToStudent);
        var feedback = result.FeedbackToStudent!;

        // Regra 3: Sem frases repetitivas de palavras-chave
        Assert.DoesNotContain("o texto aborda elementos relacionados", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("demonstrando compreensao do aspecto", feedback, StringComparison.OrdinalIgnoreCase);

        // Regra 4: Sem palavras-chave soltas entre parenteses
        Assert.DoesNotMatch(@"\([a-z]+,\s*[a-z]+\)", feedback);

        // Regra 1: Saudação direta
        Assert.StartsWith("Olá", feedback);

        // Regra 12: Nota sugerida no final
        Assert.Contains("Nota sugerida:", feedback);
        Assert.Matches(@"Nota sugerida:\s+\d+", feedback);

        // Regra 5: Sem detalhes internos
        Assert.DoesNotContain("C1", feedback);
        Assert.DoesNotContain("C2", feedback);
        Assert.DoesNotContain("criterion", feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_FeedbackHumanizado_TomAdequadoANota()
    {
        // Cenário com nota baixa — não deve ter elogios genéricos
        var request = new GradingAnalysisRequest(
            AssignmentName: "SA 13",
            MaxGrade: 100m,
            ActivityDescription: null,
            RubricOrCriteria: "Elaborar plano de gerenciamento de eventos de TI; Indicar eventos que impactam operacao; Relacionar com ITIL; Organizar texto com norma culta",
            TeacherInstructions: null,
            SubmissionText: "Entrega breve sem muito conteudo.",
            FileHashes: []);

        var result = await _sut.AnalyzeAsync(request, CancellationToken.None);

        Assert.NotNull(result.FeedbackToStudent);
        var feedback = result.FeedbackToStudent!;

        // Regra 15: Sem elogios genéricos para nota baixa
        Assert.DoesNotContain("excelente trabalho", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bom trabalho", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bom desenvolvimento", feedback, StringComparison.OrdinalIgnoreCase);

        // Regra 14: Sem tom punitivo
        Assert.DoesNotContain("insuficiente", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inadequado", feedback, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ruim", feedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_FeedbackHumanizado_ContemPontosPositivosEMelhorias()
    {
        var request = new GradingAnalysisRequest(
            AssignmentName: "SA 14",
            MaxGrade: 49m,
            ActivityDescription: null,
            RubricOrCriteria: "Elaborar plano de gerenciamento de eventos de TI; Indicar eventos que impactam operacao; Relacionar com ITIL; Organizar texto com norma culta",
            TeacherInstructions: null,
            SubmissionText: "O estudante elaborou um plano de gerenciamento de eventos de TI. " +
                            "O texto indica alguns eventos mas nao relaciona com ITIL.",
            FileHashes: []);

        var result = await _sut.AnalyzeAsync(request, CancellationToken.None);

        Assert.NotNull(result.FeedbackToStudent);
        var feedback = result.FeedbackToStudent!;

        // Deve ter pontos positivos reais
        Assert.Contains("pontos positivos", feedback, StringComparison.OrdinalIgnoreCase);

        // Deve ter orientação de melhoria
        Assert.Contains("melhorar", feedback, StringComparison.OrdinalIgnoreCase);

        // Deve ter parágrafo de fechamento
        Assert.Contains("De forma geral", feedback);
    }
}
