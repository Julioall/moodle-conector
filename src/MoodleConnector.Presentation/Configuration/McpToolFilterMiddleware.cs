using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MoodleConnector.Presentation.Configuration;

public class McpToolFilterMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<McpToolFilterMiddleware> _logger;

    public McpToolFilterMiddleware(RequestDelegate next, ILogger<McpToolFilterMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only intercept requests to /mcp that are likely JSON-RPC responses (POST)
        if (context.Request.Path != "/mcp" || context.Request.Method != HttpMethods.Post)
        {
            await _next(context);
            return;
        }

        var policy = context.RequestServices.GetService<IMcpToolExposurePolicy>();
        if (policy == null)
        {
            await _next(context);
            return;
        }

        var originalBodyStream = context.Response.Body;
        using var memoryStream = new MemoryStream();
        context.Response.Body = memoryStream;

        try
        {
            await _next(context);
            
            memoryStream.Position = 0;
            var responseBody = await new StreamReader(memoryStream).ReadToEndAsync();
            var contentType = context.Response.ContentType ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(responseBody) &&
                (string.IsNullOrWhiteSpace(contentType) ||
                 contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase) ||
                 responseBody.TrimStart().StartsWith("{") ||
                 responseBody.TrimStart().StartsWith("[")))
            {
                // Only attempt JSON-based filtering. Do not attempt to parse or
                // rewrite SSE (`text/event-stream`) payloads here — the test
                // harness/clients are responsible for parsing `data:` frames.
                var filteredBody = FilterToolsList(responseBody, policy);

                var bytes = Encoding.UTF8.GetBytes(filteredBody);
                context.Response.ContentLength = bytes.Length;
                await originalBodyStream.WriteAsync(bytes, 0, bytes.Length);
            }
            else
            {
                memoryStream.Position = 0;
                await memoryStream.CopyToAsync(originalBodyStream);
            }
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }

    private string FilterToolsList(string json, IMcpToolExposurePolicy policy)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node == null) return json;

            // Simple check: is this a tools/list result?
            var toolsArray = node["result"]?["tools"]?.AsArray();
            if (toolsArray == null) return json;

            var methodsWithMetadata = GetMethodsWithMetadata();

            for (int i = toolsArray.Count - 1; i >= 0; i--)
            {
                var tool = toolsArray[i];
                if (tool == null) continue;

                var toolName = tool["name"]?.ToString();
                if (toolName != null)
                {
                    methodsWithMetadata.TryGetValue(toolName, out var metadata);
                    if (!policy.ShouldExpose(toolName, metadata))
                    {
                        toolsArray.RemoveAt(i);
                        _logger.LogInformation("Filtered tool '{ToolName}' from MCP tools/list due to exposure policy.", toolName);
                    }
                }
            }

            return node.ToJsonString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse and filter MCP tools list JSON.");
            return json; // Fallback to returning unfiltered on error
        }
    }

    private string FilterEventStreamToolsList(string eventStream, IMcpToolExposurePolicy policy)
    {
        var outputLines = new List<string>();
        var lines = eventStream.Split(new[] { '\r', '\n' }, StringSplitOptions.None);

        foreach (var line in lines)
        {
            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var json = line.Substring("data:".Length).Trim();
                if (string.IsNullOrWhiteSpace(json))
                {
                    outputLines.Add(line);
                    continue;
                }

                try
                {
                    var filteredJson = FilterToolsList(json, policy);
                    outputLines.Add($"data: {filteredJson}");
                }
                catch
                {
                    outputLines.Add(line);
                }
            }
            else
            {
                outputLines.Add(line);
            }
        }

        return string.Join(Environment.NewLine, outputLines);
    }

    private static System.Collections.Generic.Dictionary<string, MoodleToolMetadataAttribute>? _metadataCache;
    private static readonly object _cacheLock = new object();

    private System.Collections.Generic.Dictionary<string, MoodleToolMetadataAttribute> GetMethodsWithMetadata()
    {
        lock (_cacheLock)
        {
            if (_metadataCache != null) return _metadataCache;

            _metadataCache = new System.Collections.Generic.Dictionary<string, MoodleToolMetadataAttribute>(StringComparer.OrdinalIgnoreCase);

            // Scan all loaded assemblies to find MCP tool methods and their metadata
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch
                {
                    continue; // skip dynamic / reflection-only assemblies
                }

                    foreach (var type in types)
                    {
                        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                        foreach (var method in methods)
                        {
                            try
                            {
                                var cad = method.GetCustomAttributesData();
                                var mcptoolAttrData = cad.FirstOrDefault(a => a.AttributeType.Name == "McpServerToolAttribute");
                                if (mcptoolAttrData != null)
                                {
                                    // try to find the Name property (either constructor arg or named arg)
                                    string? toolName = null;
                                    if (mcptoolAttrData.ConstructorArguments.Count > 0)
                                    {
                                        toolName = mcptoolAttrData.ConstructorArguments[0].Value as string;
                                    }
                                    if (string.IsNullOrEmpty(toolName))
                                    {
                                        var named = mcptoolAttrData.NamedArguments.FirstOrDefault(na => na.MemberName == "Name");
                                        toolName = named.TypedValue.Value as string;
                                    }

                                    if (!string.IsNullOrEmpty(toolName))
                                    {
                                        var metaDataAttr = cad.FirstOrDefault(a => a.AttributeType.Name == "MoodleToolMetadataAttribute");
                                        if (metaDataAttr != null)
                                        {
                                            var metadata = new MoodleToolMetadataAttribute();
                                            foreach (var na in metaDataAttr.NamedArguments)
                                            {
                                                switch (na.MemberName)
                                                {
                                                    case "Family": metadata.Family = na.TypedValue.Value as string ?? string.Empty; break;
                                                    case "Classification": metadata.Classification = na.TypedValue.Value as string ?? string.Empty; break;
                                                    case "Kind": metadata.Kind = na.TypedValue.Value as string ?? string.Empty; break;
                                                    case "CanonicalOperation": metadata.CanonicalOperation = na.TypedValue.Value as string ?? string.Empty; break;
                                                    case "Structural": metadata.Structural = na.TypedValue.Value is bool b && b; break;
                                                }
                                            }
                                            _metadataCache[toolName] = metadata;
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                // Ignore attribute resolution errors for third-party assemblies
                            }
                        }
                    }
            }

            return _metadataCache;
        }
    }
}
