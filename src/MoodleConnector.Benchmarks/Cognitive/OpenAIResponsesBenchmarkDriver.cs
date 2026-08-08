using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using OpenAI.Chat;

namespace MoodleConnector.Benchmarks.Cognitive;

/// <summary>
/// Benchmark driver using the MCP Streamable HTTP transport (POST /mcp).
/// The MoodleConnector server uses .WithHttpTransport() which implements
/// the modern Streamable HTTP protocol — NOT the legacy SSE transport.
///
/// Protocol summary:
///   - Initialize: POST /mcp  { jsonrpc, id, method:"initialize", params:{ ... } }
///     → response contains { result: { ... } }
///     → server returns Mcp-Session-Id header
///   - Subsequent requests: POST /mcp with Mcp-Session-Id header
///   - tools/list: POST /mcp { jsonrpc, id, method:"tools/list" }
///   - tools/call:  POST /mcp { jsonrpc, id, method:"tools/call", params:{ name, arguments } }
/// </summary>
public sealed class OpenAIResponsesBenchmarkDriver : IBenchmarkAgentDriver
{
    private readonly ChatClient _chatClient;
    private readonly HttpClient _mcpClient;
    private string? _sessionId;
    private bool _initialized;
    private BenchmarkScorer _scorer = new();
    private HashSet<string> _knownToolNames = new(StringComparer.OrdinalIgnoreCase);

    private const string McpEndpoint = "/mcp";
    public static readonly string CommitSha = ResolveCommitSha();
    public const string BenchmarkVersion = "1.0.0";

    public OpenAIResponsesBenchmarkDriver(ChatClient chatClient, HttpClient mcpClient)
    {
        _chatClient = chatClient;
        _mcpClient = mcpClient;
    }

    // ------------------------------------------------------------------
    // Static helpers
    // ------------------------------------------------------------------

    private static string ResolveCommitSha()
    {
        var envSha = Environment.GetEnvironmentVariable("GIT_COMMIT_SHA")
            ?? Environment.GetEnvironmentVariable("GITHUB_SHA");
        if (!string.IsNullOrWhiteSpace(envSha)) return envSha.Trim();

        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse HEAD")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            var sha = proc?.StandardOutput.ReadToEnd().Trim() ?? string.Empty;
            proc?.WaitForExit();
            return sha;
        }
        catch { return string.Empty; }
    }

    private static string ComputeManifestHash(JsonArray tools)
    {
        var json = tools.ToJsonString();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    // ------------------------------------------------------------------
    // MCP Streamable HTTP initialization
    // ------------------------------------------------------------------

    /// <summary>
    /// Initializes the MCP session via Streamable HTTP.
    /// After this call, _sessionId is set and all subsequent requests
    /// include the Mcp-Session-Id header.
    /// </summary>
    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = "init-1",
            ["method"] = "initialize",
            ["params"] = new JsonObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JsonObject
                {
                    ["tools"] = new JsonObject()
                },
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = "MoodleBench",
                    ["version"] = BenchmarkVersion
                }
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, McpEndpoint)
        {
            Content = JsonContent.Create(request)
        };

        // Apply session ID if we have one from a previous attempt
        if (!string.IsNullOrEmpty(_sessionId))
            httpRequest.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);

        var response = await _mcpClient.SendAsync(httpRequest, ct);

        // Capture session ID from response headers
        if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionValues))
        {
            _sessionId = sessionValues.FirstOrDefault();
            // Update default headers for subsequent requests
            _mcpClient.DefaultRequestHeaders.Remove("Mcp-Session-Id");
            if (!string.IsNullOrEmpty(_sessionId))
                _mcpClient.DefaultRequestHeaders.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new Exception($"MCP initialize failed: HTTP {(int)response.StatusCode} — {body}");
        }

        // Send initialized notification
        var notification = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/initialized"
        };
        using var notifRequest = new HttpRequestMessage(HttpMethod.Post, McpEndpoint)
        {
            Content = JsonContent.Create(notification)
        };
        // Best effort — server may return 202 Accepted or 200
        _ = await _mcpClient.SendAsync(notifRequest, ct);

        _initialized = true;
    }

    // ------------------------------------------------------------------
    // MCP tool fetch
    // ------------------------------------------------------------------

    private async Task<(JsonArray tools, int schemaTokens)> FetchMcpToolsAsync(CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);

        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Guid.NewGuid().ToString(),
            ["method"] = "tools/list"
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, McpEndpoint)
        {
            Content = JsonContent.Create(request)
        };

        var response = await _mcpClient.SendAsync(httpRequest, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new Exception($"MCP tools/list failed: HTTP {(int)response.StatusCode} — {body}");
        }

        // Handle both streaming (202 + SSE body) and direct JSON responses
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

        JsonObject? responseJson;
        if (contentType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            // Streamable HTTP can return SSE events — read first data event
            var streamBody = await response.Content.ReadAsStringAsync(ct);
            responseJson = ParseFirstSseJson(streamBody);
        }
        else
        {
            responseJson = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
        }

        var tools = responseJson?["result"]?["tools"]?.AsArray() ?? new JsonArray();
        var schemaTokens = tools.Sum(t => EstimateTokens(t?["inputSchema"]?.ToJsonString() ?? string.Empty));
        return (tools, schemaTokens);
    }

    // ------------------------------------------------------------------
    // MCP tool call
    // ------------------------------------------------------------------

    private async Task<string> CallMcpToolAsync(string name, string argumentsJson, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);

        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Guid.NewGuid().ToString(),
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = name,
                ["arguments"] = JsonNode.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson)
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, McpEndpoint)
        {
            Content = JsonContent.Create(request)
        };

        var response = await _mcpClient.SendAsync(httpRequest, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new Exception($"MCP tools/call failed for '{name}': HTTP {(int)response.StatusCode} — {body}");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (contentType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            var streamBody = await response.Content.ReadAsStringAsync(ct);
            var parsed = ParseFirstSseJson(streamBody);
            return parsed?.ToJsonString() ?? streamBody;
        }

        return await response.Content.ReadAsStringAsync(ct);
    }

    // ------------------------------------------------------------------
    // Main benchmark run
    // ------------------------------------------------------------------

    public async Task<CognitiveTrace> RunAsync(BenchmarkTask task, BenchmarkProfile profile, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        // 1. Fetch tools (initializes session on first call per driver instance)
        var (mcpTools, toolSchemaTokens) = await FetchMcpToolsAsync(cancellationToken);
        var toolManifestHash = ComputeManifestHash(mcpTools);

        // Rebuild known tool names and scorer (first call per profile)
        if (_knownToolNames.Count == 0)
        {
            foreach (var t in mcpTools)
            {
                var n = t?["name"]?.ToString();
                if (!string.IsNullOrEmpty(n)) _knownToolNames.Add(n);
            }
            _scorer = new BenchmarkScorer(_knownToolNames);
        }

        // 2. Build ChatTools from MCP manifest
        var chatTools = new List<ChatTool>();
        foreach (var mtool in mcpTools)
        {
            var name = mtool?["name"]?.ToString() ?? string.Empty;
            var description = mtool?["description"]?.ToString() ?? string.Empty;
            var inputSchema = mtool?["inputSchema"]?.AsObject();
            if (!string.IsNullOrEmpty(name) && inputSchema != null)
            {
                var schemaBytes = JsonSerializer.SerializeToUtf8Bytes(inputSchema);
                chatTools.Add(ChatTool.CreateFunctionTool(name, description, BinaryData.FromBytes(schemaBytes)));
            }
        }

        // 3. Setup conversation
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are MoodleConnector, a helpful assistant. Use tools to interact with Moodle."),
            new UserChatMessage(task.Prompt)
        };

        var options = new ChatCompletionOptions { Temperature = 0.0f };
        foreach (var tool in chatTools)
            options.Tools.Add(tool);

        string? selectedTool = null;
        string? selectedConnection = null;
        var arguments = new Dictionary<string, object>();
        string resultContent = string.Empty;
        var toolInvocations = new List<ToolInvocationTrace>();
        var aggregatedPromptTokens = 0;
        var aggregatedCompletionTokens = 0;

        // 4. Agent loop (max 5 turns)
        for (int i = 0; i < 5; i++)
        {
            var completion = await _chatClient.CompleteChatAsync(messages, options, cancellationToken);
            messages.Add(new AssistantChatMessage(completion));

            aggregatedPromptTokens += completion.Value.Usage?.InputTokenCount ?? 0;
            aggregatedCompletionTokens += completion.Value.Usage?.OutputTokenCount ?? 0;

            if (completion.Value.FinishReason == ChatFinishReason.ToolCalls)
            {
                var toolCall = completion.Value.ToolCalls.FirstOrDefault();
                if (toolCall != null)
                {
                    selectedTool ??= toolCall.FunctionName;
                    var argsStr = toolCall.FunctionArguments?.ToString() ?? "{}";
                    if (arguments.Count == 0 && !string.IsNullOrWhiteSpace(argsStr))
                        arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(argsStr) ?? new();

                    selectedConnection ??= ExtractConnectionFromArguments(arguments)
                        ?? InferConnectionFromPrompt(task.Prompt);

                    var toolStart = Stopwatch.StartNew();
                    var toolResult = await CallMcpToolAsync(toolCall.FunctionName, argsStr, cancellationToken);
                    toolStart.Stop();
                    resultContent = toolResult;

                    toolInvocations.Add(new ToolInvocationTrace(
                        ToolName: toolCall.FunctionName,
                        ArgumentsJson: argsStr,
                        ToolResult: toolResult,
                        LatencyMs: toolStart.ElapsedMilliseconds,
                        PromptTokens: completion.Value.Usage?.InputTokenCount ?? 0,
                        CompletionTokens: completion.Value.Usage?.OutputTokenCount ?? 0,
                        TotalTokens: completion.Value.Usage?.TotalTokenCount ?? 0
                    ));

                    messages.Add(new ToolChatMessage(toolCall.Id, toolResult));
                }
            }
            else
            {
                break;
            }
        }

        stopwatch.Stop();

        // 5. Build traces
        var routing = new RoutingTrace(
            SelectedSkill: "moodle-core",
            SelectedIntent: selectedTool ?? "none",
            SelectedOperation: selectedTool ?? "none",
            SelectedConnection: selectedConnection,
            Arguments: arguments,
            ToolInvocations: toolInvocations
        );

        var execution = new ExecutionTrace(
            ConnectionId: Guid.Empty,
            RegistryOperation: selectedTool ?? "none",
            PolicyDecision: "Allowed",
            MoodleCalls: toolInvocations.Count,
            LatencyMs: stopwatch.ElapsedMilliseconds,
            PromptTokens: aggregatedPromptTokens,
            CompletionTokens: aggregatedCompletionTokens,
            TotalTokens: aggregatedPromptTokens + aggregatedCompletionTokens,
            ToolSchemaTokens: toolSchemaTokens,
            ToolManifestHash: toolManifestHash,
            BenchmarkVersion: BenchmarkVersion,
            CommitSha: CommitSha
        );

        var scoring = _scorer.Score(task, routing, execution, resultContent);

        return new CognitiveTrace(
            TaskId: task.Id,
            Profile: profile,
            Prompt: task.Prompt,
            Model: profile.ModelName,
            Routing: routing,
            Execution: execution,
            ResultContent: resultContent,
            Scoring: scoring
        );
    }

    // ------------------------------------------------------------------
    // Static utilities
    // ------------------------------------------------------------------

    public static string? ExtractConnectionFromArguments(Dictionary<string, object> arguments)
    {
        foreach (var key in new[] { "moodleAlias", "alias", "connectionRef", "connection" })
        {
            if (arguments.TryGetValue(key, out var val) && val is string s && !string.IsNullOrWhiteSpace(s))
                return s;
        }
        return null;
    }

    private static string? InferConnectionFromPrompt(string prompt)
    {
        if (prompt.Contains("SENAI", StringComparison.OrdinalIgnoreCase)) return "senai";
        if (prompt.Contains("FIEG",  StringComparison.OrdinalIgnoreCase)) return "fieg";
        return null;
    }

    private static int EstimateTokens(string text)
        => string.IsNullOrWhiteSpace(text) ? 0 : Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));

    /// <summary>
    /// Parses the first JSON object from a text/event-stream body.
    /// SSE lines are: "data: {...}" or "event: message\ndata: {...}"
    /// </summary>
    private static JsonObject? ParseFirstSseJson(string sseBody)
    {
        foreach (var line in sseBody.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("data:", StringComparison.Ordinal))
            {
                var json = trimmed.Substring("data:".Length).Trim();
                if (!string.IsNullOrWhiteSpace(json) && json != "[DONE]")
                {
                    try { return JsonSerializer.Deserialize<JsonObject>(json); }
                    catch { /* try next line */ }
                }
            }
        }
        return null;
    }
}
