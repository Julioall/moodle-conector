namespace MoodleConnector.Domain;

public sealed record ForumWriteResult(
    bool Success,
    string MoodleFunction,
    string MoodleStatus,
    string? DiscussionId,
    string? PostId,
    IReadOnlyList<string> Warnings);
