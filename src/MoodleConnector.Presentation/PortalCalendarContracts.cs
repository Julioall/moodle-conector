namespace MoodleConnector.Presentation;

public sealed record PortalCalendarEventDto(Guid Id, string Title, string? Description, DateTimeOffset StartAt, DateTimeOffset? EndAt, string Type, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record PortalCalendarEventInput(string Title, string? Description, DateTimeOffset StartAt, DateTimeOffset? EndAt, string? Type);
