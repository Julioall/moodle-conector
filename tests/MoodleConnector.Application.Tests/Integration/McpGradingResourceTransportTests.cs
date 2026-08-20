using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace MoodleConnector.Application.Tests.Integration;

public sealed class McpGradingResourceTransportTests : IClassFixture<McpTestWebApplicationFactory>
{
    private readonly McpTestWebApplicationFactory _factory;

    public McpGradingResourceTransportTests(McpTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ResourcesListAndRead_ExposeTheReviewAppUri()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Mcp-Api-Key", await RegisterClientAsync(client));
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        var initialize = await SendAsync(client, "initialize", new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "resource-test", ["version"] = "1.0" }
        }, "resource-init");
        var sessionId = initialize.SessionId;

        await SendAsync(client, "notifications/initialized", new JsonObject(), null, sessionId);

        var listed = await SendAsync(client, "resources/list", new JsonObject(), "resource-list", sessionId);
        var resources = listed.Body?["result"]?["resources"]?.AsArray();
        Assert.NotNull(resources);
        Assert.Contains(resources!, resource =>
            resource?["uri"]?.GetValue<string>() == "ui://grading-review/v2/app.html");

        var read = await SendAsync(client, "resources/read", new JsonObject
        {
            ["uri"] = "ui://grading-review/v2/app.html"
        }, "resource-read", sessionId);
        var contents = read.Body?["result"]?["contents"]?.AsArray();
        Assert.NotNull(contents);
        Assert.Contains(contents!, content =>
            content?["uri"]?.GetValue<string>() == "ui://grading-review/v2/app.html" &&
            content?["mimeType"]?.GetValue<string>() == "text/html;profile=mcp-app" &&
            content?["text"]?.ToString().Contains("id=\"app\"", StringComparison.Ordinal) == true);

        var legacyRead = await SendAsync(client, "resources/read", new JsonObject
        {
            ["uri"] = "ui://grading-review/app.html"
        }, "resource-read-legacy", sessionId);
        var legacyContents = legacyRead.Body?["result"]?["contents"]?.AsArray();
        Assert.NotNull(legacyContents);
        Assert.Contains(legacyContents!, content =>
            content?["uri"]?.GetValue<string>() == "ui://grading-review/app.html" &&
            content?["mimeType"]?.GetValue<string>() == "text/html;profile=mcp-app");
    }

    private static async Task<(JsonNode? Body, string? SessionId)> SendAsync(
        HttpClient client,
        string method,
        JsonNode @params,
        string? id,
        string? sessionId = null)
    {
        var requestBody = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = @params
        };
        if (id is not null)
        {
            requestBody["id"] = id;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            request.Headers.Add("Mcp-Session-Id", sessionId);
        }

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var session = response.Headers.TryGetValues("Mcp-Session-Id", out var sessionValues)
            ? sessionValues.FirstOrDefault()
            : null;
        return (ParseResponse(body), session);
    }

    private static JsonNode? ParseResponse(string body)
    {
        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            var data = body
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                .Select(line => line["data:".Length..].Trim())
                .FirstOrDefault(line => line.Length > 0);
            return data is null ? null : JsonNode.Parse(data);
        }
    }

    private async Task<string> RegisterClientAsync(HttpClient client)
    {
        var payload = """
        {
          "clientId": "resource-test-client",
          "moodleAlias": "default",
          "moodleBaseUrl": "https://moodle.tests",
          "moodleUsername": "usuario.teste",
          "moodlePassword": "senha.teste",
          "moodleTarget": "default",
          "isDefault": true,
          "canWrite": true
        }
        """;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/admin/connector-clients/register")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Admin-Api-Key", "admin-tests-key");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(body);
        return json?["apiKey"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Test registration did not return an API key.");
    }
}
