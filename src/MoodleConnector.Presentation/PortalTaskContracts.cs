namespace MoodleConnector.Presentation;

public sealed record PortalTaskDto(Guid Id, string Title, string? Description, string Status, string Priority, DateTimeOffset? DueAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record PortalTaskInput(string Title, string? Description, string? Status, string? Priority, DateTimeOffset? DueAt);
