namespace MoodleConnector.Presentation;

public sealed record CalendarEventDto(Guid Id, string Title, string? Description, DateTimeOffset StartAt, DateTimeOffset? EndAt, string Type, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record CalendarEventInput(string Title, string? Description, DateTimeOffset StartAt, DateTimeOffset? EndAt, string? Type);

