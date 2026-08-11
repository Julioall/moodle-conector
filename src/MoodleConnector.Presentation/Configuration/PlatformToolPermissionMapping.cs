namespace MoodleConnector.Presentation.Configuration;

internal static class PlatformToolPermissionMapping
{
    public static string For(string toolName, MoodleToolMetadataAttribute metadata)
    {
        var normalized = toolName.ToLowerInvariant();
        if (normalized.Contains("message", StringComparison.Ordinal) || metadata.Family == "messaging")
            return normalized.StartsWith("confirm_", StringComparison.Ordinal) || normalized.StartsWith("prepare_", StringComparison.Ordinal)
                ? "tool.messages.send" : "tool.messages.view";
        if (metadata.Family is "grading" or "assignments" || normalized.Contains("grade", StringComparison.Ordinal) || normalized.Contains("submission", StringComparison.Ordinal))
            return metadata.Kind.Contains("write", StringComparison.OrdinalIgnoreCase) || normalized.Contains("prepare", StringComparison.Ordinal) || normalized.Contains("confirm", StringComparison.Ordinal)
                ? "tool.assignments.grade" : "tool.assignments.view";
        if (metadata.Family == "courses" || normalized.Contains("course", StringComparison.Ordinal)) return "tool.assignments.view";
        if (metadata.Family == "students" || normalized.Contains("student", StringComparison.Ordinal)) return "tool.students.view";
        if (metadata.Family == "classroom-audit") return "tool.classroom.view";
        if (metadata.Family == "follow-up") return "tool.followup.view";
        if (metadata.Family is "monitor" or "reports" || normalized.Contains("report", StringComparison.Ordinal) || normalized.Contains("monitor", StringComparison.Ordinal)) return "tool.reports.view";
        if (normalized.Contains("forum", StringComparison.Ordinal)) return "tool.forums.view";
        if (metadata.Family is "memory" or "memory-document") return "tool.memory.manage";
        if (metadata.Family is "pedagogy") return "tool.pedagogy.view";
        if (metadata.Family is "discovery" or "infrastructure" or "demopendingaction") return "tool.connections.manage";
        return string.Empty;
    }
}
