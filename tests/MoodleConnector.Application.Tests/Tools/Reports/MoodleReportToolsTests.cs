using System.IO.Compression;
using System.Text.Json;
using MediatR;
using ModelContextProtocol.Protocol;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Reports.Queries;
using MoodleConnector.Presentation.Tools.Reports;

namespace MoodleConnector.Application.Tests.Tools.Reports;

public sealed class MoodleReportToolsTests
{
    [Fact]
    public async Task GeraRelatorioEstruturadoDeNotas()
    {
        var report = CreateReport();
        var tool = CreateTool(report);

        var result = await tool.GerarRelatorioNotasCursoAsync("101");

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("ok", structured.GetProperty("status").GetString());
        Assert.Equal("101", structured.GetProperty("data").GetProperty(nameof(GenerateCourseGradesReportResult.CourseId)).GetString());
        Assert.Equal(2, structured.GetProperty("data").GetProperty(nameof(GenerateCourseGradesReportResult.TotalStudents)).GetInt32());
        Assert.False(result.IsError);
    }

    [Fact]
    public async Task AnexaArquivoExcelAoResultadoMcp()
    {
        var tool = CreateTool(CreateReport());

        var result = await tool.ExportarRelatorioNotasExcelAsync("101");

        Assert.False(result.IsError);
        Assert.Equal(2, result.Content.Count);
        var resourceBlock = Assert.IsType<EmbeddedResourceBlock>(result.Content[1]);
        var resource = Assert.IsType<BlobResourceContents>(resourceBlock.Resource);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", resource.MimeType);

        using var archive = new ZipArchive(new MemoryStream(resource.DecodedData.ToArray()), ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("xl/workbook.xml"));
        Assert.NotNull(archive.GetEntry("xl/worksheets/sheet1.xml"));
    }

    [Fact]
    public async Task Marca_freshness_live_quando_snapshot_nao_tem_heads()
    {
        var snapshot = new CourseReadSnapshot(
            "101",
            Activities: null,
            Students: null,
            Groups: null,
            Submissions: null,
            Gradebook: null,
            new CourseReadSnapshotMetadata(
                [MoodleSnapshotDatasets.Students, MoodleSnapshotDatasets.Gradebook],
                [MoodleSnapshotDatasets.Students, MoodleSnapshotDatasets.Gradebook],
                [],
                [],
                OldestUpdatedAt: null,
                NewestUpdatedAt: null,
                SkewSeconds: null,
                IsComplete: false,
                RefreshQueued: true));
        var tool = CreateTool(CreateReport(), new FakeSnapshotCoordinator(snapshot));

        var result = await tool.GerarRelatorioNotasCursoAsync("101");

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var freshness = structured.GetProperty("freshness");
        Assert.Equal("live", freshness.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, freshness.GetProperty("snapshotAt").ValueKind);
        Assert.Equal(0, freshness.GetProperty("recordCount").GetInt32());
        Assert.True(freshness.GetProperty("refreshQueued").GetBoolean());
    }

    private static MoodleReportTools CreateTool(
        GenerateCourseGradesReportResult report,
        IMoodleCourseReadSnapshotCoordinator? snapshotCoordinator = null) =>
        new(
            new FakeMediator(report),
            new FakeSelection(),
            new FakeUserResolver(),
            snapshotCoordinator: snapshotCoordinator);

    private static GenerateCourseGradesReportResult CreateReport() =>
        new(
            "101",
            new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero),
            2,
            1,
            1,
            75m,
            [
                new CourseGradeReportStudentRow("1", "Ana", null, 75m, 100m, 75m, "75,00", "com_nota"),
                new CourseGradeReportStudentRow("2", "Bruno", null, null, null, null, null, "sem_nota")
            ],
            "A metrica usa a nota total do curso retornada pelo Moodle.");

    private sealed class FakeSelection : IMoodleConnectionSelection
    {
        public string? Alias { get; set; }
    }

    private sealed class FakeUserResolver : IMoodleUserResolver
    {
        public Task<long?> ResolveMoodleUserIdAsync(CancellationToken cancellationToken) =>
            Task.FromResult<long?>(42);
    }

    private sealed class FakeSnapshotCoordinator(CourseReadSnapshot snapshot)
        : IMoodleCourseReadSnapshotCoordinator
    {
        public Task<CourseReadSnapshot?> ReadAsync(
            CourseReadSnapshotRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CourseReadSnapshot?>(snapshot);
    }

    private sealed class FakeMediator(GenerateCourseGradesReportResult report) : IMediator
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult((TResponse)(object)report);

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            Task.FromResult<object?>(report);

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => Task.CompletedTask;

        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            AsyncEnumerable.Empty<object?>();
    }
}
