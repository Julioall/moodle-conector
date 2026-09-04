using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.Grading;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Tests.Grading;

public sealed class GradingContextBuilderTests
{
    [Fact]
    public async Task BuildAsync_ComResourceOriginal_NaoBloqueiaQuandoExtracaoFalha()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        repository.Artifacts.Add(new GradingArtifact(
            Guid.NewGuid(),
            item.Id,
            "submission_file",
            "resposta.rtf",
            "text/rtf",
            "hash-rtf",
            SizeBytes: 4028,
            ExtractionStatus: ExtractionStatus.Failed,
            ExtractedTextRef: null,
            SummaryRef: "extract_failed",
            CreatedAt: DateTimeOffset.UtcNow,
            SourceUrl: "https://moodle.example/pluginfile.php/1/resposta.rtf"));

        var sut = new GradingContextBuilder(
            repository,
            Options.Create(new GradingLimitsOptions()),
            new HeuristicAssignmentContextSelectionService(),
            new FakeMoodleAssignmentSettingsGateway(),
            new HeuristicCriteriaGenerationService());

        var context = await sut.BuildAsync(
            item,
            new GradingContextOptions(IncludeRubric: false, IncludeSubmissionFiles: true),
            CancellationToken.None);

        var file = Assert.Single(context.AttachedFiles);
        Assert.True(file.OriginalResourceAvailable);
        Assert.DoesNotContain(
            context.Blockers,
            blocker => blocker.Contains("Submissão sem conteúdo legível", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildAsync_UsaSomenteTextoExtraidoJaPersistido()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        repository.Artifacts.AddRange(
        [
            new GradingArtifact(
                Guid.NewGuid(),
                item.Id,
                "submission_file",
                "relatorio.pdf",
                "application/pdf",
                "hash-1",
                SizeBytes: 1024,
                ExtractionStatus: "succeeded",
                ExtractedTextRef: "Texto extraido salvo localmente com conteudo suficiente.",
                SummaryRef: null,
                DateTimeOffset.UtcNow),
            new GradingArtifact(
                Guid.NewGuid(),
                item.Id,
                "submission_file",
                "imagem.png",
                "image/png",
                "hash-2",
                SizeBytes: 2048,
                ExtractionStatus: "failed",
                ExtractedTextRef: null,
                SummaryRef: null,
                DateTimeOffset.UtcNow)
        ]);
        var sut = new GradingContextBuilder(
            repository,
            Options.Create(new GradingLimitsOptions
            {
                MaxFilesPerSubmission = 1,
                MaxTextCharsPerSubmission = 12
            }),
            new HeuristicAssignmentContextSelectionService(),
            new FakeMoodleAssignmentSettingsGateway(),
            new HeuristicCriteriaGenerationService());

        var context = await sut.BuildAsync(
            item,
            new GradingContextOptions(IncludeSubmissionFiles: true, TeacherInstructions: "Priorize clareza."),
            CancellationToken.None);

        Assert.Equal(item.Id, context.GradingItemId);
        var file = Assert.Single(context.AttachedFiles);
        Assert.Equal("relatorio.pdf", file.FileName);
        Assert.Equal("Texto extrai", file.ExtractedText);
        Assert.Equal(file.ExtractedText, context.SubmissionText);
        Assert.True(file.IsSupported);
        Assert.Equal(2, context.ArtifactReferences.Count);
        Assert.Contains(context.ArtifactReferences, artifact => artifact.ArtifactId == file.ArtifactId);
        Assert.Contains(context.ArtifactReferences, artifact => artifact.ExtractionStatus == "failed");
        Assert.Equal("Priorize clareza.", context.TeacherInstructions);
        Assert.Equal(1, repository.ListArtifactsCalls);
    }

    [Fact]
    public async Task BuildAsync_IgnoraDiagnosticoAntigoQuandoContextoFoiRecuperado()
    {
        var repository = new FakeGradingReviewRepository();
        var item = AssistedGradingItem.Create(Guid.NewGuid(), 10, 501, 9001, 101, 0);
        repository.Artifacts.AddRange(
        [
            new GradingArtifact(
                Guid.NewGuid(),
                item.Id,
                "assignment_context",
                "assignment-9001",
                null,
                null,
                null,
                ExtractionStatus.Failed,
                null,
                "context_fetch_failed",
                DateTimeOffset.UtcNow),
            new GradingArtifact(
                Guid.NewGuid(),
                item.Id,
                "assignment_context",
                "orientacoes.pdf",
                "application/pdf",
                "hash-context",
                100,
                ExtractionStatus.Succeeded,
                "Enunciado recuperado com orientacoes suficientes.",
                "section:1;distance:1",
                DateTimeOffset.UtcNow)
        ]);
        var sut = new GradingContextBuilder(
            repository,
            Options.Create(new GradingLimitsOptions()),
            new HeuristicAssignmentContextSelectionService(),
            new FakeMoodleAssignmentSettingsGateway(),
            new HeuristicCriteriaGenerationService());

        var context = await sut.BuildAsync(
            item,
            new GradingContextOptions(IncludeCourseMaterials: true),
            CancellationToken.None);

        Assert.DoesNotContain(context.Blockers, blocker => blocker.Contains("context_fetch_failed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Enunciado recuperado", context.AssignmentStatement);
    }

    [Fact]
    public async Task BuildAsync_SelecionaMelhorArtefatoDeContextoComoEnunciado()
    {
        var repository = new FakeGradingReviewRepository();
        var item = AssistedGradingItem.Create(Guid.NewGuid(), 29972, 101112, 1178546, 356968, 0);
        repository.Artifacts.AddRange(
        [
            new GradingArtifact(
                Guid.NewGuid(),
                item.Id,
                "assignment_context",
                "Calendario do curso.pdf",
                "application/pdf",
                "sha-calendar",
                SizeBytes: 100,
                ExtractionStatus: "succeeded",
                ExtractedTextRef: "Datas gerais e recados administrativos.",
                SummaryRef: null,
                CreatedAt: DateTimeOffset.UtcNow),
            new GradingArtifact(
                Guid.NewGuid(),
                item.Id,
                "assignment_context",
                "Orientacoes SAP 01 - Etapa 1.pdf",
                "application/pdf",
                "sha-enunciado",
                SizeBytes: 200,
                ExtractionStatus: "succeeded",
                ExtractedTextRef: "Enunciado da atividade SAP 01 etapa 1 com criterios de entrega.",
                SummaryRef: null,
                CreatedAt: DateTimeOffset.UtcNow)
        ]);
        var sut = new GradingContextBuilder(
            repository,
            Options.Create(new GradingLimitsOptions()),
            new HeuristicAssignmentContextSelectionService(),
            new FakeMoodleAssignmentSettingsGateway(),
            new HeuristicCriteriaGenerationService());

        var context = await sut.BuildAsync(
            item,
            new GradingContextOptions(IncludeSubmissionFiles: false, IncludeCourseMaterials: true),
            CancellationToken.None);

        Assert.Contains("Enunciado da atividade SAP 01", context.AssignmentStatement);
        Assert.Contains("Orientacoes SAP 01", context.CourseMaterials);
        Assert.DoesNotContain("Datas gerais", context.AssignmentStatement);
    }

    [Fact]
    public async Task BuildAsync_ExtraiCriteriosENotaMaximaDoContextoSelecionado()
    {
        var repository = new FakeGradingReviewRepository();
        var item = AssistedGradingItem.Create(Guid.NewGuid(), 29972, 101112, 1178546, 356968, 0);
        repository.Artifacts.Add(new GradingArtifact(
            Guid.NewGuid(),
            item.Id,
            "assignment_context",
            "Enunciado SAP 01 - Etapa 1.pdf",
            "application/pdf",
            "sha-enunciado",
            SizeBytes: 300,
            ExtractionStatus: "succeeded",
            ExtractedTextRef:
                """
                Enunciado da atividade SAP 01 - Etapa 1.
                Valor: 16 pontos.
                Critérios de avaliação:
                - Descrever o gerenciamento de eventos de TI.
                - Apresentar exemplos de incidentes e problemas.
                - Propor ações corretivas coerentes.
                """,
            SummaryRef: null,
            CreatedAt: DateTimeOffset.UtcNow));
        var sut = new GradingContextBuilder(
            repository,
            Options.Create(new GradingLimitsOptions()),
            new HeuristicAssignmentContextSelectionService(),
            new FakeMoodleAssignmentSettingsGateway(),
            new HeuristicCriteriaGenerationService());

        var context = await sut.BuildAsync(
            item,
            new GradingContextOptions(IncludeSubmissionFiles: false, IncludeCourseMaterials: true),
            CancellationToken.None);

        Assert.Equal(16m, context.MaxGrade);
        Assert.Contains("gerenciamento de eventos", context.Criteria, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ações corretivas", context.Criteria, StringComparison.OrdinalIgnoreCase);
        // Sem submissao incluida, o blocker de submissao e esperado
        Assert.DoesNotContain(context.Blockers, b => b.Contains("Escala", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(context.Blockers, b => b.Contains("Critérios", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildAsync_ExtraiCriteriosEValorDeSapComCriteriosNaMesmaLinha()
    {
        var repository = new FakeGradingReviewRepository();
        var item = AssistedGradingItem.Create(Guid.NewGuid(), 29972, 101112, 1178546, 356968, 0);
        repository.Artifacts.Add(new GradingArtifact(
            Guid.NewGuid(),
            item.Id,
            "assignment_context",
            "SAP 01.pdf",
            "application/pdf",
            "sha-sap-01",
            SizeBytes: 4317,
            ExtractionStatus: "succeeded",
            ExtractedTextRef:
                """
                Situação de Aprendizagem 01
                Envio SAP 01 - Etapa 1
                Valor da atividade: 49 pontos
                Critérios de avaliação: organização do plano de gerenciamento; descrição do gerenciamento de eventos, incidentes e problemas; aderência às boas práticas de ITIL; clareza na proposta de ações corretivas.
                Produto esperado: Plano de Gerenciamento de Eventos, Incidentes e Problemas de TI.
                """,
            SummaryRef: null,
            CreatedAt: DateTimeOffset.UtcNow));
        var sut = new GradingContextBuilder(
            repository,
            Options.Create(new GradingLimitsOptions()),
            new HeuristicAssignmentContextSelectionService(),
            new FakeMoodleAssignmentSettingsGateway(),
            new HeuristicCriteriaGenerationService());

        var context = await sut.BuildAsync(
            item,
            new GradingContextOptions(IncludeSubmissionFiles: false, IncludeCourseMaterials: true),
            CancellationToken.None);

        Assert.Equal(49m, context.MaxGrade);
        Assert.Contains("organização do plano de gerenciamento", context.Criteria, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gerenciamento de eventos", context.Criteria, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ITIL", context.Criteria, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Produto esperado", context.Criteria, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_ComArtefatoDeRubrica_PopulaRubricDescriptionENotaMaxima()
    {
        var repository = new FakeGradingReviewRepository();
        var item = AssistedGradingItem.Create(Guid.NewGuid(), 29972, 101112, 1178546, 356968, 0);
        repository.Artifacts.Add(new GradingArtifact(
            Guid.NewGuid(),
            item.Id,
            "rubric",
            "Rubrica SAP 01.pdf",
            "application/pdf",
            "sha-rubric",
            SizeBytes: 500,
            ExtractionStatus: "succeeded",
            ExtractedTextRef:
                """
                Rubrica de avaliação SAP 01.
                Valor da atividade: 20 pontos.
                Critério 1: Clareza na descrição dos processos ITIL.
                Critério 2: Coerência das ações corretivas propostas.
                """,
            SummaryRef: null,
            CreatedAt: DateTimeOffset.UtcNow));
        var sut = new GradingContextBuilder(
            repository,
            Options.Create(new GradingLimitsOptions()),
            new HeuristicAssignmentContextSelectionService(),
            new FakeMoodleAssignmentSettingsGateway(),
            new HeuristicCriteriaGenerationService());

        var context = await sut.BuildAsync(
            item,
            new GradingContextOptions(IncludeSubmissionFiles: false, IncludeCourseMaterials: true),
            CancellationToken.None);

        Assert.NotNull(context.RubricDescription);
        Assert.Contains("Rubrica de avaliação", context.RubricDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(20m, context.MaxGrade);
        // Com rubricDescription preenchida, o blocker de critérios não deve disparar
        Assert.DoesNotContain(context.Blockers, b => b.Contains("Critérios", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildAsync_IncludeRubricFalse_NaoIncluiRubricaMesmoComMateriaisDeCurso()
    {
        var repository = new FakeGradingReviewRepository();
        var item = AssistedGradingItem.Create(Guid.NewGuid(), 29972, 101112, 1178546, 356968, 0);
        repository.Artifacts.AddRange(
        [
            new GradingArtifact(
                Guid.NewGuid(),
                item.Id,
                "rubric",
                "Rubrica.pdf",
                "application/pdf",
                "sha-rubric",
                SizeBytes: 200,
                ExtractionStatus: "succeeded",
                ExtractedTextRef: "Rubrica formal: clareza e coerencia.",
                SummaryRef: null,
                CreatedAt: DateTimeOffset.UtcNow),
            new GradingArtifact(
                Guid.NewGuid(),
                item.Id,
                "assignment_context",
                "Enunciado.pdf",
                "application/pdf",
                "sha-context",
                SizeBytes: 300,
                ExtractionStatus: "succeeded",
                ExtractedTextRef: "Enunciado da atividade com orientacoes suficientes para analise.",
                SummaryRef: null,
                CreatedAt: DateTimeOffset.UtcNow)
        ]);
        var sut = new GradingContextBuilder(
            repository,
            Options.Create(new GradingLimitsOptions()),
            new HeuristicAssignmentContextSelectionService(),
            new FakeMoodleAssignmentSettingsGateway(),
            new HeuristicCriteriaGenerationService());

        var context = await sut.BuildAsync(
            item,
            new GradingContextOptions(
                IncludeRubric: false,
                IncludeSubmissionFiles: false,
                IncludeCourseMaterials: true),
            CancellationToken.None);

        Assert.Null(context.RubricDescription);
        Assert.NotNull(context.AssignmentStatement);
        Assert.Contains("Enunciado da atividade", context.AssignmentStatement, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_SomenteRubrica_MantemEnunciadoSemTratarComoMaterialDeCurso()
    {
        var repository = new FakeGradingReviewRepository();
        var item = AssistedGradingItem.Create(Guid.NewGuid(), 29972, 101112, 1178546, 356968, 0);
        repository.Artifacts.AddRange(
        [
            new GradingArtifact(
                Guid.NewGuid(),
                item.Id,
                "rubric",
                "Rubrica.pdf",
                "application/pdf",
                "sha-rubric",
                SizeBytes: 200,
                ExtractionStatus: "succeeded",
                ExtractedTextRef: "Rubrica formal: clareza e coerencia.",
                SummaryRef: null,
                CreatedAt: DateTimeOffset.UtcNow),
            new GradingArtifact(
                Guid.NewGuid(),
                item.Id,
                "assignment_context",
                "Enunciado.pdf",
                "application/pdf",
                "sha-context",
                SizeBytes: 300,
                ExtractionStatus: "succeeded",
                ExtractedTextRef: "Enunciado da atividade com orientacoes suficientes para analise.",
                SummaryRef: null,
                CreatedAt: DateTimeOffset.UtcNow)
        ]);
        var sut = new GradingContextBuilder(
            repository,
            Options.Create(new GradingLimitsOptions()),
            new HeuristicAssignmentContextSelectionService(),
            new FakeMoodleAssignmentSettingsGateway(),
            new HeuristicCriteriaGenerationService());

        var context = await sut.BuildAsync(
            item,
            new GradingContextOptions(
                IncludeRubric: true,
                IncludeSubmissionFiles: false,
                IncludeCourseMaterials: false),
            CancellationToken.None);

        Assert.NotNull(context.RubricDescription);
        Assert.NotNull(context.AssignmentStatement);
        Assert.Null(context.CourseMaterials);
    }

    [Fact]
    public async Task BuildAsync_QuandoCriteriosNaoEstruturados_UsaEnunciadoComoFallbackDeCriterios()
    {
        var repository = new FakeGradingReviewRepository();
        var item = AssistedGradingItem.Create(Guid.NewGuid(), 29972, 101112, 1178546, 356968, 0);
        repository.Artifacts.Add(new GradingArtifact(
            Guid.NewGuid(),
            item.Id,
            "assignment_context",
            "Orientacoes SAP 02.pdf",
            "application/pdf",
            "sha-sap-02",
            SizeBytes: 200,
            ExtractionStatus: "succeeded",
            ExtractedTextRef:
                """
                Situação de Aprendizagem 02 - Etapa 2.
                O aluno deve elaborar um plano de continuidade de negócios para uma empresa de médio porte.
                Deve considerar os principais riscos operacionais e as estratégias de mitigação adequadas.
                Valor: 12 pontos.
                """,
            SummaryRef: null,
            CreatedAt: DateTimeOffset.UtcNow));
        var sut = new GradingContextBuilder(
            repository,
            Options.Create(new GradingLimitsOptions()),
            new HeuristicAssignmentContextSelectionService(),
            new FakeMoodleAssignmentSettingsGateway(),
            new HeuristicCriteriaGenerationService());

        var context = await sut.BuildAsync(
            item,
            new GradingContextOptions(IncludeSubmissionFiles: false, IncludeCourseMaterials: true),
            CancellationToken.None);

        // Sem header de critérios no texto, o enunciado completo deve ser usado como fallback
        Assert.NotNull(context.Criteria);
        Assert.Contains("plano de continuidade", context.Criteria, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(12m, context.MaxGrade);
        // Com criteria preenchida pelo fallback, o blocker de critérios não deve disparar
        Assert.DoesNotContain(context.Blockers, b => b.Contains("Critérios", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildAsync_QuandoArquivosNaoIncluidos_NaoConsultaArtefatos()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        var sut = new GradingContextBuilder(
            repository,
            Options.Create(new GradingLimitsOptions()),
            new HeuristicAssignmentContextSelectionService(),
            new FakeMoodleAssignmentSettingsGateway(),
            new HeuristicCriteriaGenerationService());

        var context = await sut.BuildAsync(
            item,
            new GradingContextOptions(IncludeRubric: false, IncludeSubmissionFiles: false),
            CancellationToken.None);

        Assert.Empty(context.AttachedFiles);
        Assert.Null(context.SubmissionText);
        Assert.Equal(0, repository.ListArtifactsCalls);
    }

    [Fact]
    public async Task BuildAsync_CriteriosContaminados_FallbackGeraCriteriosLimpos()
    {
        var repository = new FakeGradingReviewRepository();
        var item = AssistedGradingItem.Create(Guid.NewGuid(), 29972, 101112, 1178546, 356968, 0);
        repository.Artifacts.Add(new GradingArtifact(
            Guid.NewGuid(),
            item.Id,
            "assignment_context",
            "SAP 01.pdf",
            "application/pdf",
            "sha-sap-01",
            SizeBytes: 4317,
            ExtractionStatus: "succeeded",
            ExtractedTextRef:
                """
                à distância - individual Resultados esperados: elaboração, em documento de texto, de um plano de gerenciamento de eventos, incidentes e problemas de TI;
                indicação das boas práticas de ITIL aplicáveis ao cenário proposto;
                apresentação de ações corretivas coerentes com o plano.
                Produto esperado: Plano de Gerenciamento.
                """,
            SummaryRef: null,
            CreatedAt: DateTimeOffset.UtcNow));
        var sut = new GradingContextBuilder(
            repository,
            Options.Create(new GradingLimitsOptions()),
            new HeuristicAssignmentContextSelectionService(),
            settingsGateway: new FakeMoodleAssignmentSettingsGateway(),
            criteriaGenerationService: new HeuristicCriteriaGenerationService());

        var context = await sut.BuildAsync(
            item,
            new GradingContextOptions(IncludeSubmissionFiles: false, IncludeCourseMaterials: true),
            CancellationToken.None);

        // O critério contaminado "à distância - individual Resultados esperados..." NÃO deve aparecer
        if (!string.IsNullOrWhiteSpace(context.Criteria))
        {
            Assert.DoesNotContain("à distância - individual", context.Criteria);
        }

        // Os critérios devem ser claros e avaliáveis
        Assert.NotNull(context.Criteria);
        Assert.NotEmpty(context.Criteria);
    }

    [Fact]
    public async Task BuildAsync_CriteriosBons_NaoAcionaFallback()
    {
        var repository = new FakeGradingReviewRepository();
        var item = AssistedGradingItem.Create(Guid.NewGuid(), 29972, 101112, 1178546, 356968, 0);
        repository.Artifacts.Add(new GradingArtifact(
            Guid.NewGuid(),
            item.Id,
            "assignment_context",
            "Enunciado SAP 01.pdf",
            "application/pdf",
            "sha-enunciado",
            SizeBytes: 300,
            ExtractionStatus: "succeeded",
            ExtractedTextRef:
                """
                Critérios de avaliação: organização do plano de gerenciamento; descrição do gerenciamento de eventos, incidentes e problemas; aderência às boas práticas de ITIL; clareza na proposta de ações corretivas.
                Produto esperado: Plano.
                """,
            SummaryRef: null,
            CreatedAt: DateTimeOffset.UtcNow));

        // Usando FakeCriteriaGenerationService que registra chamadas
        var fakeGenerator = new FakeCriteriaGenerationService();
        var sut = new GradingContextBuilder(
            repository,
            Options.Create(new GradingLimitsOptions()),
            new HeuristicAssignmentContextSelectionService(),
            settingsGateway: new FakeMoodleAssignmentSettingsGateway(),
            criteriaGenerationService: fakeGenerator);

        var context = await sut.BuildAsync(
            item,
            new GradingContextOptions(IncludeSubmissionFiles: false, IncludeCourseMaterials: true),
            CancellationToken.None);

        // Os critérios heurísticos são bons, o fallback NÃO deveria ter sido acionado
        Assert.Equal(0, fakeGenerator.CallCount);
        Assert.NotNull(context.Criteria);
        Assert.Contains("organização do plano de gerenciamento", context.Criteria, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_ComFallback_PrivateNotesInformaCriteriosGerados()
    {
        var repository = new FakeGradingReviewRepository();
        var item = AssistedGradingItem.Create(Guid.NewGuid(), 29972, 101112, 1178546, 356968, 0);
        repository.Artifacts.Add(new GradingArtifact(
            Guid.NewGuid(),
            item.Id,
            "assignment_context",
            "SAP 01.pdf",
            "application/pdf",
            "sha-sap-01",
            SizeBytes: 200,
            ExtractionStatus: "succeeded",
            ExtractedTextRef:
                """
                à distância - individual Resultados esperados: elaboração, em documento de texto, de um plano de gerenciamento de eventos, incidentes e problemas de TI.
                Produto esperado: Plano de Gerenciamento.
                """,
            SummaryRef: null,
            CreatedAt: DateTimeOffset.UtcNow));
        var sut = new GradingContextBuilder(
            repository,
            Options.Create(new GradingLimitsOptions()),
            new HeuristicAssignmentContextSelectionService(),
            settingsGateway: new FakeMoodleAssignmentSettingsGateway(),
            criteriaGenerationService: new HeuristicCriteriaGenerationService());

        var context = await sut.BuildAsync(
            item,
            new GradingContextOptions(IncludeSubmissionFiles: false, IncludeCourseMaterials: true),
            CancellationToken.None);

        // As notas de geração de critérios devem ficar em CriteriaGenerationNotes (separadas de TeacherInstructions)
        Assert.Contains("gerados a partir do contexto", context.CriteriaGenerationNotes ?? "", StringComparison.OrdinalIgnoreCase);
        // TeacherInstructions deve permanecer null quando não há instrução real do professor
        Assert.Null(context.TeacherInstructions);
    }

    [Fact]
    public async Task BuildAsync_SemEscalaConhecida_NaoInventaCemPontos()
    {
        var repository = new FakeGradingReviewRepository();
        var item = AssistedGradingItem.Create(Guid.NewGuid(), 10, 501, 9001, 101, 0);
        repository.Artifacts.Add(new GradingArtifact(
            Guid.NewGuid(),
            item.Id,
            "assignment_context",
            "enunciado.txt",
            "text/plain",
            "sha-1",
            SizeBytes: 100,
            ExtractionStatus: "succeeded",
            ExtractedTextRef: "Elabore uma resposta fundamentada.",
            SummaryRef: null,
            CreatedAt: DateTimeOffset.UtcNow));

        var context = await new GradingContextBuilder(
            repository,
            Options.Create(new GradingLimitsOptions()),
            new HeuristicAssignmentContextSelectionService(),
            new FakeMoodleAssignmentSettingsGateway(),
            new HeuristicCriteriaGenerationService())
            .BuildAsync(item, new GradingContextOptions(IncludeCourseMaterials: true), CancellationToken.None);

        Assert.Null(context.MaxGrade);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("à distância - individual momento atividade", true)]
    [InlineData("modalidade a distancia individual", true)]
    [InlineData("Resultados esperados: elaboração de um plano de gerenciamento", true)]
    [InlineData("Produto esperado: Plano de Gerenciamento de TI", true)]
    [InlineData("Situação de Aprendizagem 01\nElaborar plano de TI", true)]
    [InlineData("elaborar um plano de gerenciamento de eventos de TI", false)]
    [InlineData("organização do plano de gerenciamento\ndescrição do gerenciamento de eventos\naderência às boas práticas", false)]
    public void AreCriteriaLowQuality_DetectaCorretamente(string? criteria, bool expectedLowQuality)
    {
        var result = GradingContextBuilder.AreCriteriaLowQuality(criteria);
        Assert.Equal(expectedLowQuality, result);
    }

    private sealed class FakeCriteriaGenerationService : ICriteriaGenerationService
    {
        public int CallCount { get; private set; }

        public Task<CriteriaGenerationResult> GenerateAsync(
            CriteriaGenerationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new CriteriaGenerationResult(
                Source: "fake",
                MaxPoints: request.MaxGrade,
                Confidence: 0.5m,
                Criteria: [new GeneratedCriterion("C1", "Critério fake para teste", request.MaxGrade, null)],
                Warnings: [],
                PrivateNotesToTeacher: "Critérios gerados por fake service."));
        }
    }

    private sealed class FakeGradingReviewRepository : IGradingReviewRepository
    {
        public List<GradingArtifact> Artifacts { get; } = [];

        public int ListArtifactsCalls { get; private set; }

        public Task AddBatchAsync(AssistedGradingBatch batch, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<AssistedGradingBatch?> GetBatchAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult<AssistedGradingBatch?>(null);

        public Task AddItemAsync(AssistedGradingItem item, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AddArtifactAsync(GradingArtifact artifact, CancellationToken cancellationToken)
        {
            Artifacts.Add(artifact);
            return Task.CompletedTask;
        }

        public Task AddEvidenceAsync(GradingEvidence evidence, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<AssistedGradingItem?> GetItemAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult<AssistedGradingItem?>(null);

        public Task<IReadOnlyList<AssistedGradingItem>> ListItemsByBatchAsync(
            Guid batchId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AssistedGradingItem>>([]);

        public Task<int> CountItemsByBatchAsync(Guid batchId, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<IReadOnlyList<GradingArtifact>> ListArtifactsByItemAsync(
            Guid gradingItemId,
            CancellationToken cancellationToken)
        {
            ListArtifactsCalls++;
            return Task.FromResult<IReadOnlyList<GradingArtifact>>(
                Artifacts.Where(artifact => artifact.GradingItemId == gradingItemId).ToArray());
        }

        public Task<IReadOnlyList<GradingEvidence>> ListEvidenceByItemAsync(
            Guid gradingItemId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<GradingEvidence>>([]);

        public Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByStatusAsync(
            GradingBatchStatus status, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AssistedGradingBatch>>(Array.Empty<AssistedGradingBatch>());
        public Task<IReadOnlyList<AssistedGradingBatch>> ListBatchesByCreatorAsync(string createdBySubject, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AssistedGradingBatch>>(Array.Empty<AssistedGradingBatch>());        public Task SaveChangesAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FakeMoodleAssignmentSettingsGateway : IMoodleAssignmentSettingsGateway
    {
        public Task<AssignmentSettingsSummary?> GetAssignmentSettingsAsync(
            string userExternalId, string courseId, string assignmentId, CancellationToken cancellationToken)
            => Task.FromResult<AssignmentSettingsSummary?>(null);
    }
}
