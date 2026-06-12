namespace MoodleConnector.Domain;

public sealed record CourseGroupSummary(
    string GroupId,
    string CourseId,
    string Name,
    string? IdNumber);
