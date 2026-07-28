using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Infrastructure;
using MoodleConnector.Infrastructure.MoodleApi;

namespace MoodleConnector.Application.Tests.Infrastructure.MoodleApi;

public sealed class MoodleRestClientTests
{
    [Fact]
    public async Task CallAsync_EnviaTokenNoCorpoEJamaisNaUrl()
    {
        var handler = new CapturingHandler("{\"courses\":[]}");
        using var client = new HttpClient(handler);
        var sut = new MoodleRestClient(
            client,
            new FakeTokenProvider("secret-token"),
            NullLogger<MoodleRestClient>.Instance);

        await sut.CallAsync(
            Connection(),
            "core_course_get_courses_by_field",
            new Dictionary<string, object?> { ["field"] = "id", ["value"] = "42" },
            CancellationToken.None);

        Assert.NotNull(handler.RequestUri);
        Assert.Equal("/webservice/rest/server.php", handler.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("secret-token", handler.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("wstoken=secret-token", handler.Body, StringComparison.Ordinal);
        Assert.Contains("wsfunction=core_course_get_courses_by_field", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallAsync_NormalizaErroEstruturadoDoMoodle()
    {
        var handler = new CapturingHandler(
            "{\"exception\":\"invalid_parameter_exception\",\"errorcode\":\"invalidparameter\",\"message\":\"ParÃ¢metro invÃ¡lido\"}",
            HttpStatusCode.BadRequest);
        using var client = new HttpClient(handler);
        var sut = new MoodleRestClient(
            client,
            new FakeTokenProvider("secret-token"),
            NullLogger<MoodleRestClient>.Instance);

        var error = await Assert.ThrowsAsync<MoodleApiException>(() => sut.CallAsync(
            Connection(), "core_course_get_courses_by_field", new Dictionary<string, object?>(), CancellationToken.None));

        Assert.Equal(MoodleErrorContract.ApiError, error.ErrorCode);
        Assert.Equal("invalidparameter", error.RemoteErrorCode);
        Assert.DoesNotContain("secret-token", error.Message, StringComparison.Ordinal);
    }

    private static MoodleConnectorCredentials Connection() => new(
        "client", "connection", "goias", "https://moodle.example", "user", "password", "goias", false);

    private sealed class FakeTokenProvider(string token) : IMoodleAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(
            MoodleConnectorCredentials connection,
            CancellationToken cancellationToken) => Task.FromResult(token);

        public void Invalidate(MoodleConnectorCredentials connection)
        {
        }
    }

    private sealed class CapturingHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
