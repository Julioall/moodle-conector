using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using OpenAI.Chat;

namespace MoodleConnector.Benchmarks.Cognitive;

public sealed class OpenAIResponsesBenchmarkDriver : IBenchmarkAgentDriver
{
    private readonly ChatClient _chatClient;
    private readonly HttpClient _mcpClient;
    private string _postEndpoint = string.Empty;
    private HttpResponseMessage? _sseResponse;
    private StreamReader? _sseReader;
    private readonly BenchmarkScorer _scorer = new();

    public OpenAIResponsesBenchmarkDriver(ChatClient chatClient, HttpClient mcpClient)
    {
        _chatClient = chatClient;
        _mcpClient = mcpClient;
    }

    public async Task<CognitiveTrace> RunAsync(BenchmarkTask task, BenchmarkProfile profile, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        // 1. Fetch available tools from MCP
        var (mcpTools, toolSchemaTokens) = await FetchMcpToolsAsync(cancellationToken);
        
        var chatTools = new List<ChatTool>();
        foreach (var mtool in mcpTools)
        {
            var name = mtool["name"]?.ToString() ?? string.Empty;
            var description = mtool["description"]?.ToString() ?? string.Empty;
            var inputSchema = mtool["inputSchema"]?.AsObject();
            if (inputSchema != null)
            {
                var schemaBytes = JsonSerializer.SerializeToUtf8Bytes(inputSchema);
                var functionTool = ChatTool.CreateFunctionTool(name, description, BinaryData.FromBytes(schemaBytes));
                chatTools.Add(functionTool);
            }
        }

        // 2. Setup Conversation
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are MoodleConnector, a helpful assistant. Use tools to interact with Moodle."),
            new UserChatMessage(task.Prompt)
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0.0f
        };
        foreach (var tool in chatTools)
        {
            options.Tools.Add(tool);
        }

        string? selectedTool = null;
        string? selectedConnection = null;
        Dictionary<string, object> arguments = new();
        string resultContent = string.Empty;
        var toolInvocations = new List<ToolInvocationTrace>();
        var aggregatedPromptTokens = 0;
        var aggregatedCompletionTokens = 0;

        // 3. Agent Loop
        for (int i = 0; i < 5; i++) // Max 5 iterations
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
                    {
                        arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(argsStr) ?? new();
                    }

                    selectedConnection ??= ExtractConnectionFromArguments(arguments) ?? InferConnectionFromPrompt(task.Prompt);

                    // Invoke tool on MCP Server
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
                break; // Finished
            }
        }

        stopwatch.Stop();

        // 4. Trace Generation
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
            MoodleCalls: selectedTool != null ? toolInvocations.Count : 0,
            LatencyMs: stopwatch.ElapsedMilliseconds,
            PromptTokens: aggregatedPromptTokens,
            CompletionTokens: aggregatedCompletionTokens,
            TotalTokens: aggregatedPromptTokens + aggregatedCompletionTokens,
            ToolSchemaTokens: toolSchemaTokens
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

    public static string? ExtractConnectionFromArguments(Dictionary<string, object> arguments)
    {
        if (arguments.TryGetValue("moodleAlias", out var alias) && alias is string aliasText && !string.IsNullOrWhiteSpace(aliasText))
        {
            return aliasText;
        }

        if (arguments.TryGetValue("alias", out var alias2) && alias2 is string alias2Text && !string.IsNullOrWhiteSpace(alias2Text))
        {
            return alias2Text;
        }

        if (arguments.TryGetValue("connectionRef", out var connectionRef) && connectionRef is string connectionRefText && !string.IsNullOrWhiteSpace(connectionRefText))
        {
            return connectionRefText;
        }

        return null;
    }

    private static string? InferConnectionFromPrompt(string prompt)
    {
        if (prompt.Contains("SENAI", StringComparison.OrdinalIgnoreCase) || prompt.Contains("senai", StringComparison.OrdinalIgnoreCase))
        {
            return "senai";
        }

        if (prompt.Contains("FIEG", StringComparison.OrdinalIgnoreCase) || prompt.Contains("fieg", StringComparison.OrdinalIgnoreCase))
        {
            return "fieg";
        }

        return null;
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_postEndpoint != null) return;
        
        var request = new HttpRequestMessage(HttpMethod.Get, "/mcp/sse");
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
        
        _sseResponse = await _mcpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        try
        {
            if (!_sseResponse.IsSuccessStatusCode)
            {
                var body = await _sseResponse.Content.ReadAsStringAsync(ct);
                throw new Exception($"HTTP {_sseResponse.StatusCode}: {body}");
            }
            _sseResponse.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"MCP Connection failed (SSE GET /mcp/sse): {ex.Message}");
        }
        
        var stream = await _sseResponse.Content.ReadAsStreamAsync(ct);
        _sseReader = new StreamReader(stream);
        
        while (!_sseReader.EndOfStream)
        {
            var line = await _sseReader.ReadLineAsync(ct);
            Console.WriteLine($"[SSE] read line: {line}");
            if (line != null && line.StartsWith("event: endpoint"))
            {
                var dataLine = await _sseReader.ReadLineAsync(ct);
                Console.WriteLine($"[SSE] read data line: {dataLine}");
                if (dataLine != null && dataLine.StartsWith("data: "))
                {
                    _postEndpoint = dataLine.Substring("data: ".Length).Trim();
                    
                    var uri = new Uri(_postEndpoint, UriKind.RelativeOrAbsolute);
                    var qs = System.Web.HttpUtility.ParseQueryString(uri.IsAbsoluteUri ? uri.Query : new Uri(new Uri("http://localhost"), _postEndpoint).Query);
                    var sessionId = qs["sessionId"];
                    Console.WriteLine($"[SSE] Endpoint: {_postEndpoint}");
                    Console.WriteLine($"[SSE] Parsed SessionId: {sessionId}");
                    if (!string.IsNullOrEmpty(sessionId))
                    {
                        _mcpClient.DefaultRequestHeaders.Remove("Mcp-Session-Id");
                        _mcpClient.DefaultRequestHeaders.Add("Mcp-Session-Id", sessionId);
                    }
                    
                    // Fire and forget a background task to consume the rest of the stream
                    // so the server doesn't block on writing.
                    _ = Task.Run(async () => 
                    {
                        try { while (!ct.IsCancellationRequested && await _sseReader.ReadLineAsync(ct) != null) { } } 
                        catch { } 
                    });
                    
                    return;
                }
            }
        }
        
        throw new InvalidOperationException("Failed to get endpoint from MCP SSE stream.");
    }

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
    }

    private async Task<(JsonArray tools, int schemaTokens)> FetchMcpToolsAsync(CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken);
        
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Guid.NewGuid().ToString(),
            ["method"] = "tools/list"
        };

        try
        {
            var response = await _mcpClient.PostAsJsonAsync(_postEndpoint, request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
            var tools = responseJson?["result"]?["tools"]?.AsArray() ?? new JsonArray();
            var schemaTokens = tools.Sum(tool => EstimateTokens(tool["inputSchema"]?.ToJsonString() ?? string.Empty));
            return (tools, schemaTokens);
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"MCP Fetch Tools failed (POST {_postEndpoint}): {ex.Message}");
        }
    }

    private async Task<string> CallMcpToolAsync(string name, string argumentsJson, CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken);

        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Guid.NewGuid().ToString(),
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = name,
                ["arguments"] = JsonNode.Parse(argumentsJson)
            }
        };

        try
        {
            var response = await _mcpClient.PostAsJsonAsync(_postEndpoint, request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            return responseJson;
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"MCP Tool Call failed (POST {_postEndpoint}): {ex.Message}");
        }
    }
}
