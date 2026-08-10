using System.Net;
using System.Net.Http.Json;
using System.Linq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Integration;

public class McpJwtClaimsIntegrationTests : IClassFixture<McpTestWebApplicationFactory>
{
    internal const string JwtIssuer = "https://oauth.tests";
    internal const string JwtAudience = "https://oauth.tests/mcp";
    internal static readonly SymmetricSecurityKey JwtSigningKey = new(Encoding.UTF8.GetBytes("0123456789ABCDEF0123456789ABCDEF"));

    private readonly McpTestWebApplicationFactory _factory;

    public McpJwtClaimsIntegrationTests(McpTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Deve_retornar_401_quando_api_key_estiver_ausente()
    {
        var auditSink = _factory.Services.GetRequiredService<TestAuthorizationAuditSink>();
        auditSink.Clear();
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("missing_api_key", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(auditSink.Requests, request => request.Reason == "missing_api_key");
    }

    [Fact]
    public async Task Deve_retornar_400_quando_payload_mcp_for_invalido()
    {
        var apiKey = await RegisterClientAsync(canWrite: true);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Mcp-Api-Key", apiKey);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_mcp_request", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JSON-RPC 2.0", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deve_listar_tools_mcp_com_aliases_em_ingles()
    {
        var apiKey = await RegisterClientAsync(canWrite: true);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Mcp-Api-Key", apiKey);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        var sessionId = await InitializeMcpSessionAsync(client);
        await NotifyInitializedAsync(client, sessionId);

        var payload = """
        {
          "jsonrpc": "2.0",
          "id": "tools-1",
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

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("list_my_courses", body, StringComparison.Ordinal);
        Assert.Contains("list_my_courses", body, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"search\"", body, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"fetch\"", body, StringComparison.Ordinal);
        Assert.Contains("search_courses", body, StringComparison.Ordinal);
        Assert.Contains("search_courses", body, StringComparison.Ordinal);
        Assert.Contains("get_course", body, StringComparison.Ordinal);
        Assert.Contains("get_course", body, StringComparison.Ordinal);
        Assert.Contains("list_course_participants", body, StringComparison.Ordinal);
        Assert.Contains("list_course_participants", body, StringComparison.Ordinal);
        Assert.Contains("list_course_students", body, StringComparison.Ordinal);
        Assert.Contains("list_course_students", body, StringComparison.Ordinal);
        Assert.Contains("list_course_groups", body, StringComparison.Ordinal);
        Assert.Contains("list_course_groups", body, StringComparison.Ordinal);
        Assert.Contains("get_group_members", body, StringComparison.Ordinal);
        Assert.Contains("get_group_members", body, StringComparison.Ordinal);
        Assert.Contains("list_course_contents", body, StringComparison.Ordinal);
        Assert.Contains("list_course_contents", body, StringComparison.Ordinal);
        Assert.Contains("get_course_module", body, StringComparison.Ordinal);
        Assert.Contains("get_course_module", body, StringComparison.Ordinal);
        Assert.Contains("list_course_resources", body, StringComparison.Ordinal);
        Assert.Contains("list_course_resources", body, StringComparison.Ordinal);
        Assert.Contains("list_course_files", body, StringComparison.Ordinal);
        Assert.Contains("list_course_files", body, StringComparison.Ordinal);
        Assert.Contains("list_course_pages", body, StringComparison.Ordinal);
        Assert.Contains("list_course_pages", body, StringComparison.Ordinal);
        Assert.Contains("list_course_urls", body, StringComparison.Ordinal);
        Assert.Contains("list_course_urls", body, StringComparison.Ordinal);
        Assert.Contains("audit_course_structure", body, StringComparison.Ordinal);
        Assert.Contains("audit_course_structure", body, StringComparison.Ordinal);
        Assert.Contains("list_course_activities", body, StringComparison.Ordinal);
        Assert.Contains("list_course_activities", body, StringComparison.Ordinal);
        Assert.Contains("get_course_activity", body, StringComparison.Ordinal);
        Assert.Contains("get_course_activity", body, StringComparison.Ordinal);
        Assert.Contains("list_course_assignments", body, StringComparison.Ordinal);
        Assert.Contains("list_course_assignments", body, StringComparison.Ordinal);
        Assert.Contains("get_assignment", body, StringComparison.Ordinal);
        Assert.Contains("get_assignment", body, StringComparison.Ordinal);
        Assert.Contains("list_course_quizzes", body, StringComparison.Ordinal);
        Assert.Contains("list_course_quizzes", body, StringComparison.Ordinal);
        Assert.Contains("get_quiz", body, StringComparison.Ordinal);
        Assert.Contains("get_quiz", body, StringComparison.Ordinal);
        Assert.Contains("list_course_scorms", body, StringComparison.Ordinal);
        Assert.Contains("list_course_scorms", body, StringComparison.Ordinal);
        Assert.Contains("list_activity_deadlines", body, StringComparison.Ordinal);
        Assert.Contains("list_activity_deadlines", body, StringComparison.Ordinal);
        Assert.Contains("list_assignment_submissions", body, StringComparison.Ordinal);
        Assert.Contains("list_assignment_submissions", body, StringComparison.Ordinal);
        Assert.Contains("get_student_submission", body, StringComparison.Ordinal);
        Assert.Contains("get_student_submission", body, StringComparison.Ordinal);
        Assert.Contains("list_pending_submissions", body, StringComparison.Ordinal);
        Assert.Contains("list_pending_submissions", body, StringComparison.Ordinal);
        Assert.Contains("list_late_submissions", body, StringComparison.Ordinal);
        Assert.Contains("list_late_submissions", body, StringComparison.Ordinal);
        Assert.Contains("list_submissions_awaiting_grading", body, StringComparison.Ordinal);
        Assert.Contains("list_submissions_awaiting_grading", body, StringComparison.Ordinal);
        Assert.Contains("get_submission_status", body, StringComparison.Ordinal);
        Assert.Contains("get_submission_status", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProfileB_ShouldHaveSameToolsAsProfileA()
    {
        var toolsA = await GetToolsListAsync(_factory, "Full");
        var toolsB = await GetToolsListAsync(_factory, "FullWithCoursesSkill");

        Assert.Equal(toolsA.Count, toolsB.Count);
        Assert.Equal(toolsA.OrderBy(name => name), toolsB.OrderBy(name => name));
    }

    [Fact]
    public async Task ProfileC_ShouldHideCourseWrapperToolsAndRetainStructuralAndUnrelatedTools()
    {
        var toolsFull = await GetToolsListAsync(_factory, "Full");
        var toolsC = await GetToolsListAsync(_factory, "SkillCoursesOptimized");

        Assert.True(toolsC.Count < toolsFull.Count);
        Assert.DoesNotContain("list_my_courses", toolsC);
        Assert.DoesNotContain("get_course", toolsC);
        Assert.Contains("search", toolsC);
        Assert.Contains("audit_course_structure", toolsC);
        Assert.Contains("create_assisted_grading_batch", toolsC);
    }

    [Theory]
    [InlineData("list_my_courses", """{"limite":5,"pagina":1,"moodleAlias":"goias"}""")]
    [InlineData("search_courses", """{"termo":"32786","limite":5,"moodleAlias":"goias"}""")]
    [InlineData("get_course", """{"courseId":"32786","moodleAlias":"goias"}""")]
    [InlineData("list_course_contents", """{"courseId":"32786","moodleAlias":"goias"}""")]
    public async Task ToolsCall_DeveConverterFalhaDeConexaoEmErroEstruturado(
        string toolName,
        string argumentsJson)
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMoodleUserResolver>();
                services.AddScoped<IMoodleUserResolver, ConnectionNotFoundMoodleUserResolver>();
            });
        });
        var apiKey = await RegisterClientAsync(canWrite: false, factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Mcp-Api-Key", apiKey);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        var sessionId = await InitializeMcpSessionAsync(client);
        await NotifyInitializedAsync(client, sessionId);
        var payload = $$"""
        {
          "jsonrpc": "2.0",
          "id": "stable-error-{{toolName}}",
          "method": "tools/call",
          "params": {
            "name": "{{toolName}}",
            "arguments": {{argumentsJson}}
          }
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
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"isError\":true", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(MoodleErrorContract.ConnectionNotFound, body, StringComparison.Ordinal);
        Assert.Contains("\"auditId\":\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"auditId\":null", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INVALID_ARGUMENT", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deve_aceitar_api_key_quando_jwt_e_api_key_estiverem_habilitados()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["McpServerSecurity:RequireJwt"] = "true",
                    ["McpServerSecurity:RequireApiKey"] = "true",
                    ["OAuth:Issuer"] = JwtIssuer,
                    ["OAuth:Audience"] = JwtAudience
                });
            });
        });

        var apiKey = await RegisterClientAsync(canWrite: true, factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Mcp-Api-Key", apiKey);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        var sessionId = await InitializeMcpSessionAsync(client);

        Assert.False(string.IsNullOrWhiteSpace(sessionId));
    }

    [Fact]
    public async Task Deve_aceitar_jwt_valido_mesmo_com_api_key_invalida_quando_ambos_estiverem_habilitados()
    {
        var factory = BuildJwtFactory(requireApiKey: true);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateJwt(connectorClientId: "jwt-client"));
        client.DefaultRequestHeaders.Add("X-Mcp-Api-Key", "api-key-invalida");
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        var sessionId = await InitializeMcpSessionAsync(client);

        Assert.False(string.IsNullOrWhiteSpace(sessionId));
    }

    [Fact]
    public async Task Deve_aceitar_jwt_valido_com_vinculo_moodle()
    {
        var factory = BuildJwtFactory(requireApiKey: false);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateJwt(connectorClientId: "jwt-client"));
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        var sessionId = await InitializeMcpSessionAsync(client);

        Assert.False(string.IsNullOrWhiteSpace(sessionId));
    }

    [Fact]
    public async Task Deve_rejeitar_jwt_valido_sem_vinculo_moodle()
    {
        var factory = BuildJwtFactory(requireApiKey: false);
        var auditSink = factory.Services.GetRequiredService<TestAuthorizationAuditSink>();
        auditSink.Clear();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", CreateJwt(connectorClientId: null, includeEmail: false));

        var response = await client.PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("moodle_connection_not_linked", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(auditSink.Requests, request => request.Reason == "moodle_connection_not_linked");
    }

    [Fact]
    public async Task Deve_rejeitar_jwt_invalido_quando_jwt_for_obrigatorio()
    {
        var factory = BuildJwtFactory(requireApiKey: false);
        var auditSink = factory.Services.GetRequiredService<TestAuthorizationAuditSink>();
        auditSink.Clear();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "token-invalido");

        var response = await client.PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("missing_or_invalid_jwt", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(auditSink.Requests, request => request.Reason == "missing_or_invalid_jwt");
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Contains("resource_metadata=", challenge.Parameter, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deve_rejeitar_api_key_invalida_quando_api_key_for_obrigatoria()
    {
        var auditSink = _factory.Services.GetRequiredService<TestAuthorizationAuditSink>();
        auditSink.Clear();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Mcp-Api-Key", "api-key-invalida");

        var response = await client.PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("invalid_api_key", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(auditSink.Requests, request => request.Reason == "invalid_api_key");
    }

    [Fact]
    public async Task Deve_permitir_descoberta_de_tools_sem_token()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["McpServerSecurity:RequireJwt"] = "true",
                    ["McpServerSecurity:RequireApiKey"] = "false",
                    ["OAuth:Issuer"] = JwtIssuer,
                    ["OAuth:Audience"] = JwtAudience
                });
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        var sessionId = await InitializeMcpSessionAsync(client);
        await NotifyInitializedAsync(client, sessionId);

        var payload = """
        {
          "jsonrpc": "2.0",
          "id": "tools-public-1",
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

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("list_my_courses", body, StringComparison.Ordinal);
        Assert.Contains("securitySchemes", body, StringComparison.Ordinal);
        Assert.Contains("oauth2", body, StringComparison.Ordinal);
        Assert.Contains("moodle-mcp-audience", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deve_aplicar_rate_limit_mcp_por_conector()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:McpPermitLimit"] = "1",
                    ["RateLimiting:WindowSeconds"] = "60"
                });
            });
        });

        var apiKey = await RegisterClientAsync(canWrite: true, factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Mcp-Api-Key", apiKey);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");

        var firstResponse = await client.PostAsync(
            "/mcp",
            new StringContent(BuildInitializePayload("rate-limit-1"), Encoding.UTF8, "application/json"));
        var secondResponse = await client.PostAsync(
            "/mcp",
            new StringContent(BuildInitializePayload("rate-limit-2"), Encoding.UTF8, "application/json"));

        firstResponse.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.TooManyRequests, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Deve_expor_metadata_oauth_para_descoberta_do_chatgpt()
    {
        var originalAppDomain = Environment.GetEnvironmentVariable("APP_DOMAIN");
        Environment.SetEnvironmentVariable("APP_DOMAIN", null);

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["McpServerSecurity:RequireJwt"] = "true",
                    ["McpServerSecurity:RequireApiKey"] = "false",
                    ["OAuth:Issuer"] = JwtIssuer,
                    ["OAuth:Audience"] = JwtAudience
                });
            });
        });

        try
        {
            var client = factory.CreateClient();

            var response = await client.GetAsync("/.well-known/oauth-protected-resource/mcp");
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);

            Assert.Equal("http://localhost/mcp", json.RootElement.GetProperty("resource").GetString());
            Assert.Equal(
                JwtIssuer,
                json.RootElement.GetProperty("authorization_servers")[0].GetString());
            Assert.Contains(
                json.RootElement.GetProperty("scopes_supported").EnumerateArray(),
                scope => scope.GetString() == "openid");
            Assert.Contains(
                json.RootElement.GetProperty("scopes_supported").EnumerateArray(),
                scope => scope.GetString() == "moodle-mcp-audience");
        }
        finally
        {
            Environment.SetEnvironmentVariable("APP_DOMAIN", originalAppDomain);
        }
    }

    [Fact]
    public async Task Deve_informar_metadata_oauth_no_desafio_quando_jwt_estiver_ausente()
    {
        var originalAppDomain = Environment.GetEnvironmentVariable("APP_DOMAIN");
        Environment.SetEnvironmentVariable("APP_DOMAIN", null);

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["McpServerSecurity:RequireJwt"] = "true",
                    ["McpServerSecurity:RequireApiKey"] = "false",
                    ["OAuth:Issuer"] = JwtIssuer,
                    ["OAuth:Audience"] = JwtAudience
                });
            });
        });

        try
        {
            var client = factory.CreateClient();

            var response = await client.PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var challenge = Assert.Single(response.Headers.WwwAuthenticate);
            Assert.Equal("Bearer", challenge.Scheme);
            Assert.Contains(
                "resource_metadata=\"http://localhost/.well-known/oauth-protected-resource/mcp\"",
                challenge.Parameter);
        }
        finally
        {
            Environment.SetEnvironmentVariable("APP_DOMAIN", originalAppDomain);
        }
    }

    [Fact]
    public async Task Deve_retornar_desafio_oauth_mcp_quando_tool_for_chamada_sem_jwt()
    {
        var originalAppDomain = Environment.GetEnvironmentVariable("APP_DOMAIN");
        Environment.SetEnvironmentVariable("APP_DOMAIN", null);

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["McpServerSecurity:RequireJwt"] = "true",
                    ["McpServerSecurity:RequireApiKey"] = "false",
                    ["OAuth:Issuer"] = JwtIssuer,
                    ["OAuth:Audience"] = JwtAudience
                });
            });
        });

        try
        {
            var client = factory.CreateClient();
            var payload = """
            {
              "jsonrpc": "2.0",
              "id": "call-auth-1",
              "method": "tools/call",
              "params": {
                "name": "list_my_courses",
                "arguments": {}
              }
            }
            """;

            var response = await client.PostAsync("/mcp", new StringContent(payload, Encoding.UTF8, "application/json"));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("\"isError\":true", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("mcp/www_authenticate", body, StringComparison.Ordinal);
            Assert.Contains("resource_metadata=\\\"http://localhost/.well-known/oauth-protected-resource/mcp\\\"", body);
            Assert.Contains("scope=\\\"openid", body);
            Assert.Contains("moodle-mcp-audience", body);
            Assert.Contains("moodle.write.assignments.grade", body);
        }
        finally
        {
            Environment.SetEnvironmentVariable("APP_DOMAIN", originalAppDomain);
        }
    }

    [Fact]
    public async Task Deve_expor_metadata_do_authorization_server_e_discovery_openid()
    {
        var client = _factory.CreateClient();

        var authorizationMetadataResponse = await client.GetAsync("/.well-known/oauth-authorization-server");
        authorizationMetadataResponse.EnsureSuccessStatusCode();
        var authorizationMetadataBody = await authorizationMetadataResponse.Content.ReadAsStringAsync();
        using var authorizationMetadata = JsonDocument.Parse(authorizationMetadataBody);

        var issuer = authorizationMetadata.RootElement.GetProperty("issuer").GetString();
        Assert.False(string.IsNullOrWhiteSpace(issuer));
        Assert.Equal("http://localhost/authorize", authorizationMetadata.RootElement.GetProperty("authorization_endpoint").GetString());
        Assert.Equal("http://localhost/token", authorizationMetadata.RootElement.GetProperty("token_endpoint").GetString());
        Assert.Equal("http://localhost/.well-known/jwks", authorizationMetadata.RootElement.GetProperty("jwks_uri").GetString());
        Assert.Contains(
            authorizationMetadata.RootElement.GetProperty("scopes_supported").EnumerateArray(),
            scope => scope.GetString() == "offline_access");

        var openIdDiscoveryResponse = await client.GetAsync("/.well-known/openid-configuration");
        openIdDiscoveryResponse.EnsureSuccessStatusCode();
        var openIdDiscoveryBody = await openIdDiscoveryResponse.Content.ReadAsStringAsync();
        using var openIdDiscovery = JsonDocument.Parse(openIdDiscoveryBody);

        Assert.Equal(
            issuer.TrimEnd('/'),
            openIdDiscovery.RootElement.GetProperty("issuer").GetString()?.TrimEnd('/'));
        Assert.Contains(
            openIdDiscovery.RootElement.GetProperty("code_challenge_methods_supported").EnumerateArray(),
            method => method.GetString() == "S256");

        var jwksResponse = await client.GetAsync("/.well-known/jwks");
        jwksResponse.EnsureSuccessStatusCode();
        var jwksBody = await jwksResponse.Content.ReadAsStringAsync();
        using var jwks = JsonDocument.Parse(jwksBody);

        Assert.NotEmpty(jwks.RootElement.GetProperty("keys").EnumerateArray());
    }

    [Fact]
    public async Task Deve_cadastrar_logar_e_conectar_moodle_com_cookie_local()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });
        var email = $"professor-{Guid.NewGuid():N}@example.com";
        var password = "senha-local-12345";

        var weakPasswordResponse = await client.PostAsJsonAsync("/api/account/register", new
        {
            name = "Professor Teste",
            email,
            password = "curta"
        });

        Assert.Equal(HttpStatusCode.BadRequest, weakPasswordResponse.StatusCode);

        var registerResponse = await client.PostAsJsonAsync("/api/account/register", new
        {
            name = "Professor Teste",
            email,
            password
        });

        registerResponse.EnsureSuccessStatusCode();

        var firstProfile = await client.GetFromJsonAsync<JsonElement>("/api/account/me");
        Assert.Equal(email, firstProfile.GetProperty("email").GetString());
        Assert.False(firstProfile.GetProperty("hasMoodleConnected").GetBoolean());

        var logoutResponse = await client.GetAsync("/auth/logout");
        logoutResponse.EnsureSuccessStatusCode();

        var loginResponse = await client.PostAsJsonAsync("/api/account/login", new
        {
            email,
            password
        });

        loginResponse.EnsureSuccessStatusCode();

        var connectResponse = await client.PostAsJsonAsync("/api/account/connect-moodle", new
        {
            moodleAlias = "Goias",
            moodleBaseUrl = "https://moodle.tests/ead?debug=true#fragment",
            moodleUsername = "professor.teste",
            moodlePassword = " senha-com-espaco-final ",
            isDefault = true,
            canWrite = true
        });

        connectResponse.EnsureSuccessStatusCode();
        var connectBody = await connectResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(connectBody.GetProperty("apiKey").GetString()));

        var connectedProfile = await client.GetFromJsonAsync<JsonElement>("/api/account/me");
        Assert.True(connectedProfile.GetProperty("hasMoodleConnected").GetBoolean());
        Assert.True(connectedProfile.GetProperty("hasApiKey").GetBoolean());
        var connection = Assert.Single(connectedProfile.GetProperty("moodleConnections").EnumerateArray());
        Assert.Equal("goias", connection.GetProperty("alias").GetString());
        Assert.Equal("https://moodle.tests/ead", connection.GetProperty("baseUrl").GetString());
        Assert.True(connection.GetProperty("canWrite").GetBoolean());
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
        Console.WriteLine("MCP response content type: " + response.Content.Headers.ContentType?.ToString());
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        // Avoid dumping potentially huge tool schemas to the test output; log only length for diagnostics.
        Console.WriteLine($"MCP raw body length: {body.Length}");

        var parsed = ParseMcpResponseBody(body);
        var tools = parsed?["result"]?["tools"]?.AsArray();
        if (tools == null)
        {
            return Array.Empty<string>();
        }

        var list = tools.Select(t => t? ["name"]?.ToString() ?? string.Empty)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        Console.WriteLine("MCP tools result: " + string.Join(",", list));
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

    private WebApplicationFactory<Program> BuildJwtFactory(bool requireApiKey)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["McpServerSecurity:RequireJwt"] = "true",
                    ["McpServerSecurity:RequireApiKey"] = requireApiKey.ToString().ToLowerInvariant(),
                    ["OAuth:Issuer"] = JwtIssuer,
                    ["OAuth:Audience"] = JwtAudience,
                    ["OAuth:RequireHttpsMetadata"] = "false"
                });
            });
        });
    }

    private static string CreateJwt(string? connectorClientId, bool includeEmail = true)
    {
        var claims = new List<Claim>
        {
            new("sub", "jwt-user-1"),
            new("scope", "openid profile email moodle-mcp-audience")
        };

        if (includeEmail)
        {
            claims.Add(new Claim("email", "teacher@example.com"));
        }

        if (!string.IsNullOrWhiteSpace(connectorClientId))
        {
            claims.Add(new Claim("connector_client_id", connectorClientId));
        }

        var token = new JwtSecurityToken(
            JwtIssuer,
            JwtAudience,
            claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(JwtSigningKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
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

public sealed class McpTestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["McpServerSecurity:RequireJwt"] = "false",
                ["McpServerSecurity:RequireApiKey"] = "true",
                ["UserClaims:UserIdClaim"] = "sub",
                ["UserClaims:MoodleUserIdClaim"] = "moodle_user_id",
                ["UserClaims:WritePermissionClaim"] = "scope",
                ["UserClaims:WritePermissionValue"] = "moodle.write",
                ["Postgres:ConnectionString"] = "Host=localhost;Database=unused_in_tests;Username=unused;Password=unused",
                ["ConnectorSecrets:EncryptionKeyBase64"] = "MDEyMzQ1Njc4OUFCQ0RFRjAxMjM0NTY3ODlBQkNERUY=",
                ["ConnectorSecrets:TokenCacheMinutes"] = "10",
                ["AdminApi:HeaderName"] = "X-Admin-Api-Key",
                ["AdminApi:ApiKey"] = "admin-tests-key",
                ["OAuth:RequireHttpsMetadata"] = "false",
                ["OAuth:ScopeName"] = "moodle-mcp-audience",
                ["MoodleApi:UseStubData"] = "true",
                ["MoodleApi:BaseUrl"] = "https://moodle.tests",
                ["MoodleProxy:UseStubData"] = "true"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ConnectorDbContext>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<DbContextOptions<ConnectorDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ConnectorDbContext>>();
            services.RemoveAll<IMcpConnectorClientResolver>();
            services.RemoveAll<IConnectorClientRegistrationService>();
            services.RemoveAll<IMoodleConnectorCredentialsProvider>();
            services.RemoveAll<IAuthorizationAuditService>();
            services.RemoveAll<IMoodleCredentialValidator>();

            var inMemoryProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();
            var databaseName = $"mcp-tests-{Guid.NewGuid():N}";
            services.AddDbContext<ConnectorDbContext>(options =>
            {
                options.UseInMemoryDatabase(databaseName);
                options.UseInternalServiceProvider(inMemoryProvider);
            });
            services.AddSingleton<TestConnectorClientStore>();
            services.AddSingleton<TestAuthorizationAuditSink>();
            services.AddScoped<IMcpConnectorClientResolver, InMemoryConnectorClientResolver>();
            services.AddScoped<IConnectorClientRegistrationService, InMemoryConnectorClientRegistrationService>();
            services.AddScoped<IMoodleConnectorCredentialsProvider, InMemoryMoodleConnectorCredentialsProvider>();
            services.AddScoped<IAuthorizationAuditService, InMemoryAuthorizationAuditService>();
            services.AddScoped<IMoodleCredentialValidator, AlwaysValidMoodleCredentialValidator>();
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = string.Empty;
                options.MetadataAddress = string.Empty;
                options.RequireHttpsMetadata = false;
                options.Configuration = new OpenIdConnectConfiguration
                {
                    Issuer = McpJwtClaimsIntegrationTests.JwtIssuer
                };
                options.TokenValidationParameters.IssuerSigningKey = McpJwtClaimsIntegrationTests.JwtSigningKey;
                options.TokenValidationParameters.ValidIssuer = McpJwtClaimsIntegrationTests.JwtIssuer;
                options.TokenValidationParameters.ValidAudience = McpJwtClaimsIntegrationTests.JwtAudience;
            });
        });
    }
}

internal sealed class TestAuthorizationAuditSink
{
    private readonly object _gate = new();
    private readonly List<AuthorizationFailureAuditRequest> _requests = [];

    public IReadOnlyList<AuthorizationFailureAuditRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToArray();
            }
        }
    }

    public void Add(AuthorizationFailureAuditRequest request)
    {
        lock (_gate)
        {
            _requests.Add(request);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _requests.Clear();
        }
    }
}

internal sealed class InMemoryAuthorizationAuditService(TestAuthorizationAuditSink sink) : IAuthorizationAuditService
{
    public Task RecordFailureAsync(AuthorizationFailureAuditRequest request, CancellationToken cancellationToken)
    {
        sink.Add(request);
        return Task.CompletedTask;
    }
}

internal sealed class TestConnectorClientStore
{
    public Dictionary<string, TestConnectorClient> ClientsById { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> ClientIdByApiKey { get; } = new(StringComparer.Ordinal);
}

internal sealed class TestConnectorClient
{
    public string ClientId { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public bool CanWrite { get; init; }
}

internal sealed class InMemoryConnectorClientRegistrationService(
    TestConnectorClientStore store,
    ConnectorDbContext dbContext) : IConnectorClientRegistrationService
{
    public async Task<RegisterConnectorClientResult> RegisterOrRotateAsync(RegisterConnectorClientRequest request, CancellationToken cancellationToken)
    {
        var clientId = request.ClientId.Trim();
        var replaced = store.ClientsById.ContainsKey(clientId);
        var apiKey = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        if (store.ClientsById.TryGetValue(clientId, out var previousClient))
        {
            store.ClientIdByApiKey.Remove(previousClient.ApiKey);
        }

        var client = new TestConnectorClient
        {
            ClientId = clientId,
            ApiKey = apiKey,
            CanWrite = request.CanWrite
        };

        store.ClientsById[clientId] = client;
        store.ClientIdByApiKey[apiKey] = clientId;

        var alias = string.IsNullOrWhiteSpace(request.MoodleAlias)
            ? "default"
            : request.MoodleAlias.Trim().ToLowerInvariant();
        var connectionId = $"{clientId}:{alias}";
        var entity = await dbContext.ConnectorClients.FindAsync([connectionId], cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (entity is null)
        {
            entity = new ConnectorClientCredentialEntity
            {
                Id = connectionId,
                ClientId = clientId,
                CreatedAtUtc = now
            };
            dbContext.ConnectorClients.Add(entity);
        }

        var shouldBeDefault = request.IsDefault ||
                              !await dbContext.ConnectorClients.AnyAsync(
                                  existing => existing.ClientId == clientId &&
                                              existing.IsActive &&
                                              existing.Id != connectionId,
                                  cancellationToken);
        if (shouldBeDefault)
        {
            var existingConnections = await dbContext.ConnectorClients
                .Where(existing => existing.ClientId == clientId && existing.Id != connectionId)
                .ToListAsync(cancellationToken);
            foreach (var existing in existingConnections)
            {
                existing.IsDefault = false;
            }
        }

        entity.ApiKeyHash = apiKey;
        entity.MoodleAlias = alias;
        entity.MoodleBaseUrl = NormalizeBaseUrl(request.MoodleBaseUrl);
        entity.MoodleUsernameEncrypted = request.MoodleUsername;
        entity.MoodlePasswordEncrypted = request.MoodlePassword;
        entity.MoodleTarget = string.IsNullOrWhiteSpace(request.MoodleTarget) ? "default" : request.MoodleTarget.Trim().ToLowerInvariant();
        entity.IsDefault = shouldBeDefault;
        entity.CanWrite = request.CanWrite;
        entity.IsActive = true;
        entity.UpdatedAtUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterConnectorClientResult(clientId, connectionId, alias, apiKey, replaced);
    }

    private static string NormalizeBaseUrl(string moodleBaseUrl)
    {
        var uri = new Uri(moodleBaseUrl.Trim(), UriKind.Absolute);
        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }
}

internal sealed class InMemoryConnectorClientResolver(TestConnectorClientStore store) : IMcpConnectorClientResolver
{
    public Task<ConnectorClientContext?> ResolveByApiKeyAsync(string apiKey, CancellationToken cancellationToken)
    {
        if (!store.ClientIdByApiKey.TryGetValue(apiKey, out var clientId) ||
            !store.ClientsById.TryGetValue(clientId, out var client))
        {
            return Task.FromResult<ConnectorClientContext?>(null);
        }

        return Task.FromResult<ConnectorClientContext?>(new ConnectorClientContext(client.ClientId, client.CanWrite));
    }
}

internal sealed class InMemoryMoodleConnectorCredentialsProvider(
    TestConnectorClientStore store,
    IHttpContextAccessor httpContextAccessor) : IMoodleConnectorCredentialsProvider
{
    public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken)
    {
        var clientId = httpContextAccessor.HttpContext?.User.FindFirst("connector_client_id")?.Value;
        var client = clientId is not null && store.ClientsById.TryGetValue(clientId, out var found)
            ? found
            : store.ClientsById.Values.FirstOrDefault();
        return Task.FromResult(new MoodleConnectorCredentials(
            client?.ClientId ?? "integration-client",
            $"{client?.ClientId ?? "integration-client"}:default",
            "default",
            "https://moodle.tests",
            "user",
            "pass",
            "default",
            client?.CanWrite ?? true));
    }
}

internal sealed class AlwaysValidMoodleCredentialValidator : IMoodleCredentialValidator
{
    public Task<bool> ValidateAsync(string moodleBaseUrl, string username, string password, CancellationToken cancellationToken)
    {
        return Task.FromResult(true);
    }
}

internal sealed class ConnectionNotFoundMoodleUserResolver : IMoodleUserResolver
{
    public Task<long?> ResolveMoodleUserIdAsync(CancellationToken cancellationToken) =>
        Task.FromException<long?>(new MoodleApiException(
            MoodleErrorContract.ConnectionNotFound,
            "Connection lookup failed in the integration-test boundary."));
}
