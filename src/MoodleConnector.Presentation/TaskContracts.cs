namespace MoodleConnector.Presentation;

public sealed record TaskDto(Guid Id, string Title, string? Description, string Status, string Priority, DateTimeOffset? DueAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record TaskInput(string Title, string? Description, string? Status, string? Priority, DateTimeOffset? DueAt);

