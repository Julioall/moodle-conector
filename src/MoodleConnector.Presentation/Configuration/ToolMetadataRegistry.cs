using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MoodleConnector.Presentation.Configuration;

/// <summary>
/// Deterministic registry of MCP tool metadata built once at startup.
/// Scans loaded assemblies for McpServerTool + MoodleToolMetadataAttribute
/// using CustomAttributeData to avoid loading external attribute types.
/// </summary>
public sealed class ToolMetadataRegistry
{
    private readonly Dictionary<string, MoodleToolMetadataAttribute> _map;

    public ToolMetadataRegistry()
    {
        _map = new Dictionary<string, MoodleToolMetadataAttribute>(StringComparer.OrdinalIgnoreCase);
        ScanAssemblies();
    }

    private void ScanAssemblies()
    {
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
                continue;
            }

            foreach (var type in types)
            {
                MethodInfo[] methods;
                try
                {
                    methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                }
                catch
                {
                    continue;
                }

                foreach (var method in methods)
                {
                    try
                    {
                        var cad = method.GetCustomAttributesData();
                        var mcptoolAttrData = cad.FirstOrDefault(a => a.AttributeType.Name == "McpServerToolAttribute");
                        if (mcptoolAttrData != null)
                        {
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

                            if (!string.IsNullOrEmpty(toolName) && !_map.ContainsKey(toolName))
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

                                    _map[toolName] = metadata;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // ignore reflection/attribute errors
                    }
                }
            }
        }
    }

    public bool TryGet(string toolName, out MoodleToolMetadataAttribute? metadata)
    {
        if (toolName is null)
        {
            metadata = null;
            return false;
        }

        return _map.TryGetValue(toolName, out metadata!);
    }

    /// <summary>
    /// Register metadata discovered from a specific tool container type.
    /// This allows deterministic association of MCP tool names to metadata
    /// at the moment the tools are registered, avoiding reflection per-request
    /// and preventing reliance on assembly scanning order.
    /// </summary>
    public void RegisterFromType(Type toolContainerType)
    {
        if (toolContainerType == null) return;

        MethodInfo[] methods;
        try
        {
            methods = toolContainerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        }
        catch
        {
            return;
        }

        foreach (var method in methods)
        {
            try
            {
                var cad = method.GetCustomAttributesData();
                var mcptoolAttrData = cad.FirstOrDefault(a => a.AttributeType.Name == "McpServerToolAttribute");
                if (mcptoolAttrData != null)
                {
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

                    if (!string.IsNullOrEmpty(toolName) && !_map.ContainsKey(toolName))
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

                            _map[toolName] = metadata;
                        }
                    }
                }
            }
            catch
            {
                // ignore individual method failures
            }
        }
    }
}
