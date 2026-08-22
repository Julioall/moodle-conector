using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MoodleConnector.Presentation.Configuration;
using MoodleConnector.Presentation.Security;
using Xunit;

namespace MoodleConnector.Application.Tests.Integration;

public class ToolExposureValidationTests : IClassFixture<McpTestWebApplicationFactory>
{
    private readonly McpTestWebApplicationFactory _factory;

    public ToolExposureValidationTests(McpTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProfileC_RemovedSet_Equals_MetadataDrivenExpected()
    {
        // Tools for Profile A and B (should be identical)
        var toolsA = await GetToolsListAsync(_factory, "Full");
        var toolsB = await GetToolsListAsync(_factory, "FullWithCoursesSkill");

        Assert.Equal(toolsA.OrderBy(x => x), toolsB.OrderBy(x => x));

        // Tools for Profile C (optimized)
        var toolsC = await GetToolsListAsync(_factory, "SkillCoursesOptimized");

        Assert.True(toolsC.Count < toolsB.Count, "ProfileC should have fewer tools than ProfileB");

        // Ensure moodle_execute_read remains exposed
        Assert.Contains("moodle_execute_read", toolsC, StringComparer.OrdinalIgnoreCase);

        // Ensure controlled write primitives are present
        Assert.Contains("moodle_prepare_write", toolsC, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("moodle_confirm_write", toolsC, StringComparer.OrdinalIgnoreCase);

        // Ensure unrelated universal function listing remains exposed
        Assert.Contains("moodle_list_functions", toolsC, StringComparer.OrdinalIgnoreCase);

        // Compute expected hidden set from the ToolMetadataRegistry used by the app instance
        var customFactoryC = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MCP_EXPOSURE_PROFILE"] = "SkillCoursesOptimized",
                    ["McpServerSecurity:RequireApiKey"] = "true",
                    ["McpServerSecurity:RequireJwt"] = "false"
                });
            });
        });

        var registry = customFactoryC.Services.GetService<ToolMetadataRegistry>();
        Assert.NotNull(registry);

        var expectedHidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in registry!.Entries)
        {
            var name = entry.Key;
            var md = entry.Value;
            if (string.Equals(md.Family, "courses", StringComparison.OrdinalIgnoreCase)
                && (string.Equals(md.Classification, "R1", StringComparison.OrdinalIgnoreCase) || string.Equals(md.Classification, "R2", StringComparison.OrdinalIgnoreCase))
                && md.Structural == false)
            {
                expectedHidden.Add(name);
            }
        }

        var removed = toolsA.Except(toolsC, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Assert the removed set exactly matches the metadata-driven expected set
        Assert.Equal(expectedHidden.OrderBy(x => x), removed.OrderBy(x => x));
    }

    [Fact]
    public async Task Production_exposes_only_the_registered_surface_and_keeps_controlled_writes_explicit()
    {
        var tools = await GetToolsListAsync(_factory, "Production");

        Assert.Equal(108, tools.Count);
        Assert.Contains("moodle_execute_read", tools, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("moodle_prepare_write", tools, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("moodle_confirm_write", tools, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("list_all_gradable_submissions", tools, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("list_gradable_submissions", tools, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("search", tools, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fetch", tools, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("generate_course_grades_report", tools, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("export_course_grades_excel", tools, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("resolve_planner_tags", tools, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("create_tasks_for_references", tools, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("prepare_demo_action", tools, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("confirm_demo_action", tools, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("future_unregistered_tool", tools, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Write_permissions_delegate_the_coarse_scope_required_by_the_controlled_write_flow()
    {
        var scopes = ToolAuthorizationMapping.ScopesForPermissions([
            "tool.assignments.grade",
            "tool.messages.send"
        ]);

        Assert.Contains(MoodleScopePolicies.WriteAny, scopes);
        Assert.Contains(MoodleScopePolicies.WriteAssignmentsGrade, scopes);
        Assert.Contains(MoodleScopePolicies.WriteMessages, scopes);
    }

    [Theory]
    [InlineData("list_tasks")]
    [InlineData("list_agenda_events")]
    public async Task Portal_list_tools_accept_empty_arguments_without_invalid_argument(string toolName)
    {
        var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MCP_EXPOSURE_PROFILE"] = "Production",
                    ["McpServerSecurity:RequireApiKey"] = "true",
                    ["McpServerSecurity:RequireJwt"] = "false"
                });
            });
        });

        var apiKey = await RegisterClientAsync(canWrite: true, customFactory);
        var client = customFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Mcp-Api-Key", apiKey);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        var sessionId = await InitializeMcpSessionAsync(client);
        await NotifyInitializedAsync(client, sessionId);

        var payload = $$"""
        {
          "jsonrpc": "2.0",
          "id": "portal-list-{{toolName}}",
          "method": "tools/call",
          "params": {
            "name": "{{toolName}}",
            "arguments": {}
          }
        }
        """;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(sessionId))
            request.Headers.Add("Mcp-Session-Id", sessionId);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("INVALID_ARGUMENT", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unexpected_connector_error", body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyList<string>> GetToolsListAsync(WebApplicationFactory<Program> factory, string exposureProfile)
    {
        var customFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MCP_EXPOSURE_PROFILE"] = exposureProfile,
                    ["McpServerSecurity:RequireApiKey"] = "true",
                    ["McpServerSecurity:RequireJwt"] = "false"
                });
            });
        });

        var client = customFactory.CreateClient();
        var apiKey = await RegisterClientAsync(canWrite: true, customFactory);
        client.DefaultRequestHeaders.Add("X-Mcp-Api-Key", apiKey);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        var sessionId = await InitializeMcpSessionAsync(client);
        await NotifyInitializedAsync(client, sessionId);

        var toolsClient = customFactory.CreateClient();
        toolsClient.DefaultRequestHeaders.Add("X-Mcp-Api-Key", apiKey);
        toolsClient.DefaultRequestHeaders.Accept.Clear();
        toolsClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        toolsClient.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        var payload = """
        {
          "jsonrpc": "2.0",
          "id": "tools-list-profile",
          "method": "tools/list",
          "params": {}
        }
        """;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            request.Headers.Add("Mcp-Session-Id", sessionId);
        }

        var response = await toolsClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        var parsed = ParseMcpResponseBody(body);
        var tools = parsed?["result"]?["tools"]?.AsArray();
        if (tools == null)
        {
            return Array.Empty<string>();
        }

        var list = tools.Select(t => t? ["name"]?.ToString() ?? string.Empty)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        return list;
    }

    private static JsonNode? ParseMcpResponseBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            var dataLines = body
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                .Select(line => line.Substring("data:".Length).Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            if (dataLines.Length == 0)
            {
                return null;
            }

            var combined = string.Join(string.Empty, dataLines);
            return JsonNode.Parse(combined);
        }
    }

        private async Task<string> RegisterClientAsync(bool canWrite, WebApplicationFactory<Program>? factory = null)
        {
                var client = (factory ?? _factory).CreateClient();
                var payload = $$"""
                {
                    "clientId": "integration-{{Guid.NewGuid():N}}",
                    "moodleAlias": "default",
                    "moodleBaseUrl": "https://moodle.tests",
                    "moodleUsername": "usuario.teste",
                    "moodlePassword": "senha.teste",
                    "moodleTarget": "default",
                    "isDefault": true,
                    "canWrite": {{canWrite.ToString().ToLowerInvariant()}}
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

                var marker = "\"apiKey\":\"";
                var start = body.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                Assert.True(start >= 0, "Resposta do cadastro nao retornou apiKey.");
                start += marker.Length;
                var end = body.IndexOf('"', start);
                Assert.True(end > start, "Formato de apiKey invalido no payload de cadastro.");

                return body[start..end];
        }

        private static async Task<string?> InitializeMcpSessionAsync(HttpClient client)
        {
                var initializePayload = BuildInitializePayload("init-1");

                var response = await client.PostAsync("/mcp", new StringContent(initializePayload, Encoding.UTF8, "application/json"));
                response.EnsureSuccessStatusCode();

                if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionIdValues))
                {
                        return sessionIdValues.FirstOrDefault();
                }

                return null;
        }

        private static string BuildInitializePayload(string id)
        {
                return $$"""
                {
                    "jsonrpc": "2.0",
                    "id": "{{id}}",
                    "method": "initialize",
                    "params": {
                        "protocolVersion": "2024-11-05",
                        "capabilities": {},
                        "clientInfo": {
                            "name": "integration-tests",
                            "version": "1.0.0"
                        }
                    }
                }
                """;
        }

        private static async Task NotifyInitializedAsync(HttpClient client, string? sessionId)
        {
                var payload = """
                {
                    "jsonrpc": "2.0",
                    "method": "notifications/initialized",
                    "params": {}
                }
                """;

                using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
                {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };

                if (!string.IsNullOrWhiteSpace(sessionId))
                {
                        request.Headers.Add("Mcp-Session-Id", sessionId);
                }

                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
        }
}
