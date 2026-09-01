using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Domain;
using MoodleConnector.Infrastructure;
using MoodleConnector.Infrastructure.Scorm;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class MoodleScormReaderTests
{
    [Fact]
    public async Task ReadAsync_BaixaPacoteAutenticadoEExtraiManifestoSCOETexto()
    {
        var fileGateway = new FileGateway(CreatePackage());
        var reader = CreateReader(fileGateway, new RestClient());

        var result = await reader.ReadAsync("user-1", "101", null, CancellationToken.None);

        Assert.Equal("101", result.CourseId);
        Assert.Equal("42", result.ScormId);
        Assert.Equal("Treinamento", result.Name);
        Assert.Equal(2, result.Scos.Count);
        Assert.Contains(result.Scos, sco => sco.Title == "Aula 1" && sco.Available && sco.Text!.Contains("Olá aluno", StringComparison.Ordinal));
        Assert.DoesNotContain("alert", result.Scos[0].Text!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("index.html", result.Scos[0].LaunchPath);
        Assert.Contains(result.Files, file => file.Path == "index.html" && file.MimeType == "text/html");
        Assert.Equal("https://moodle.example/pluginfile.php/42/package.zip", fileGateway.Url);
    }

    [Fact]
    public async Task ReadAsync_RejeitaPacoteSemManifesto()
    {
        var reader = CreateReader(new FileGateway(CreatePackage(includeManifest: false)), new RestClient());

        var error = await Assert.ThrowsAsync<MoodleApiException>(() => reader.ReadAsync("user-1", "101", null, CancellationToken.None));

        Assert.Equal("scorm_manifest_missing", error.ErrorCode);
    }

    private static MoodleScormReader CreateReader(FileGateway fileGateway, RestClient restClient) => new(
        Options.Create(new MoodleApiOptions()),
        Options.Create(new GradingLimitsOptions { MaxFileSizeMb = 10, MaxTextCharsPerSubmission = 10_000 }),
        new CoursesGateway(),
        new CredentialsProvider(),
        new FunctionCatalog(),
        restClient,
        fileGateway);

    private static byte[] CreatePackage(bool includeManifest = true)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (includeManifest)
            {
                var manifest = archive.CreateEntry("imsmanifest.xml");
                using var writer = new StreamWriter(manifest.Open(), Encoding.UTF8);
                writer.Write("<manifest identifier=\"manifest-1\" version=\"1.2\" xmlns:adlcp=\"urn:adlcp\"><organizations><organization><title>Treinamento</title><item identifier=\"i1\" identifierref=\"r1\"><title>Aula 1</title></item><item identifier=\"i2\" identifierref=\"r2\"><title>Aula 2</title></item></organization></organizations><resources><resource identifier=\"r1\" href=\"index.html\" adlcp:scormType=\"sco\"/><resource identifier=\"r2\" href=\"lesson.html\" adlcp:scormType=\"sco\"/></resources></manifest>");
            }
            var page = archive.CreateEntry("index.html");
            using (var writer = new StreamWriter(page.Open(), Encoding.UTF8))
                writer.Write("<html><body><h1>Olá aluno</h1><script>alert('secret')</script></body></html>");
            var lesson = archive.CreateEntry("lesson.html");
            using (var writer = new StreamWriter(lesson.Open(), Encoding.UTF8))
                writer.Write("<p>Segunda aula &amp; prática.</p>");
        }
        return stream.ToArray();
    }

    private sealed class CoursesGateway : IMoodleCoursesGateway
    {
        public Task<PagedCourses> GetMyCoursesAsync(string userExternalId, int limit, int page, CancellationToken cancellationToken) => Task.FromResult(new PagedCourses([], 0, page, limit));
        public Task<IReadOnlyList<CourseSummary>> SearchMyCoursesAsync(string userExternalId, string query, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CourseSummary>>([]);
        public Task<CourseSummary?> GetMyCourseAsync(string userExternalId, string courseId, CancellationToken cancellationToken) =>
            Task.FromResult<CourseSummary?>(new CourseSummary("101", null, null, "Curso", null, null, null, null, null, true, null, null, null, null, null, null));
    }

    private sealed class CredentialsProvider : IMoodleConnectorCredentialsProvider
    {
        public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MoodleConnectorCredentials("client", "connection", "default", "https://moodle.example", "user", "password", "default", false));
    }

    private sealed class FunctionCatalog : IMoodleFunctionCatalog
    {
        public Task<MoodleFunctionProfile> GetCurrentAsync(bool forceRefresh, CancellationToken cancellationToken) =>
            Task.FromResult(new MoodleFunctionProfile("connection", "default", null, null, null,
                [new MoodleFunctionDescriptor("mod_scorm_get_scorms_by_courses", MoodleFunctionRisk.Read, true)], DateTimeOffset.UtcNow));
    }

    private sealed class RestClient : IMoodleRestClient
    {
        public Task<JsonElement> CallAsync(MoodleConnectorCredentials connection, string functionName, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken) => CallAsync(connection, functionName, parameters, false, cancellationToken);
        public Task<JsonElement> CallAsync(MoodleConnectorCredentials connection, string functionName, IReadOnlyDictionary<string, object?> parameters, bool allowServiceToken, CancellationToken cancellationToken) =>
            Task.FromResult(JsonSerializer.Deserialize<JsonElement>("{\"scorms\":[{\"id\":42,\"name\":\"Treinamento\",\"version\":\"1.2\",\"reference\":\"package.zip\",\"packageurl\":\"https://moodle.example/pluginfile.php/42/package.zip\"}]}")!);
    }

    private sealed class FileGateway(byte[] package) : IMoodleSubmissionFileGateway
    {
        public string? Url { get; private set; }
        public Task<SubmissionFileDownloadResult> DownloadFileAsync(string userExternalId, string fileUrl, string filename, long maxBytes, CancellationToken cancellationToken)
        {
            Url = fileUrl;
            return Task.FromResult(new SubmissionFileDownloadResult(filename, "application/zip", package.Length, "sha", package, false));
        }
    }
}
