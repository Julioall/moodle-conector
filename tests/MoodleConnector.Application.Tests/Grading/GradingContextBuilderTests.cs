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

        public Task SaveChangesAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
