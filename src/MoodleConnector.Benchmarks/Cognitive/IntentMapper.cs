using System;
using System.Collections.Generic;

namespace MoodleConnector.Benchmarks.Cognitive;

/// <summary>
/// Maps MCP tool names to canonical benchmark intent strings.
/// The scorer uses tool names observed in ToolInvocations to determine IntentAccuracy,
/// since the agent reports its chosen tool — not an intent label.
/// </summary>
public static class IntentMapper
{
    private static readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase)
    {
        // courses.list
        { "moodle_list_my_courses",             "courses.list" },
        { "moodle_courses_list",                "courses.list" },
        { "moodle_get_enrolled_courses",        "courses.list" },

        // courses.search
        { "moodle_search_courses",              "courses.search" },
        { "moodle_courses_search",              "courses.search" },
        { "moodle_find_course",                 "courses.search" },
        { "moodle_course_search",               "courses.search" },

        // courses.details
        { "moodle_get_course_by_id",            "courses.details" },
        { "moodle_courses_get_by_id",           "courses.details" },
        { "moodle_get_course_details",          "courses.details" },
        { "moodle_course_details",              "courses.details" },
        { "moodle_get_course_info",             "courses.details" },

        // courses.structure
        { "moodle_get_course_contents",         "courses.structure" },
        { "moodle_courses_get_contents",        "courses.structure" },
        { "moodle_course_contents",             "courses.structure" },
        { "moodle_list_course_contents",        "courses.structure" },

        // Generic read — intent resolved from parameters, mapped as ambiguous
        { "moodle_execute_read",                "generic.read" },
        { "moodle_read",                        "generic.read" },

        // Discovery / core
        { "moodle_get_site_info",               "core.site_info" },
        { "moodle_list_capabilities",           "core.capabilities" },
        { "moodle_get_capabilities",            "core.capabilities" },
    };

    /// <summary>
    /// Resolves the canonical intent from a tool name.
    /// Returns null if the tool is not known (potential hallucination).
    /// </summary>
    public static string? Resolve(string toolName)
    {
        if (_map.TryGetValue(toolName, out var intent))
            return intent;

        // Try prefix-based heuristics for tools not in the explicit map
        if (toolName.Contains("list_my_courses", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("enrolled_courses", StringComparison.OrdinalIgnoreCase))
            return "courses.list";

        if (toolName.Contains("search_course", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("find_course", StringComparison.OrdinalIgnoreCase))
            return "courses.search";

        if (toolName.Contains("course_content", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("course_structure", StringComparison.OrdinalIgnoreCase))
            return "courses.structure";

        return null; // Unknown — may be hallucination
    }

    /// <summary>
    /// Returns true if the tool name is known in any profile's tool manifest.
    /// Tools returning null from Resolve are candidates for HallucinationDetected.
    /// </summary>
    public static bool IsKnownTool(string toolName)
        => Resolve(toolName) != null;
}
