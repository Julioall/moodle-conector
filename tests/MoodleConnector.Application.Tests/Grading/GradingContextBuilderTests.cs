using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.Grading;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Tests.Grading;

public sealed class GradingContextBuilderTests
{
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
            new HeuristicAssignmentContextSelectionService());

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
        Assert.Equal("Priorize clareza.", context.TeacherInstructions);
        Assert.Equal(1, repository.ListArtifactsCalls);
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
            new HeuristicAssignmentContextSelectionService());

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
            new HeuristicAssignmentContextSelectionService());

        var context = await sut.BuildAsync(
            item,
            new GradingContextOptions(IncludeSubmissionFiles: false, IncludeCourseMaterials: true),
            CancellationToken.None);

        Assert.Equal(16m, context.MaxGrade);
        Assert.Contains("gerenciamento de eventos", context.Criteria, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ações corretivas", context.Criteria, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Escala de nota", context.Blockers[0], StringComparison.OrdinalIgnoreCase);
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
            new HeuristicAssignmentContextSelectionService());

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
    public async Task BuildAsync_QuandoArquivosNaoIncluidos_NaoConsultaArtefatos()
    {
        var repository = new FakeGradingReviewRepository();
        var batch = AssistedGradingBatch.Create(10, [501], "teacher-1", 321, totalItems: 1);
        var item = AssistedGradingItem.Create(batch.Id, 10, 501, 9001, 101, 0);
        var sut = new GradingContextBuilder(
            repository,
            Options.Create(new GradingLimitsOptions()),
            new HeuristicAssignmentContextSelectionService());

        var context = await sut.BuildAsync(
            item,
            new GradingContextOptions(IncludeSubmissionFiles: false),
            CancellationToken.None);

        Assert.Empty(context.AttachedFiles);
        Assert.Null(context.SubmissionText);
        Assert.Equal(0, repository.ListArtifactsCalls);
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

        public Task SaveChangesAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
