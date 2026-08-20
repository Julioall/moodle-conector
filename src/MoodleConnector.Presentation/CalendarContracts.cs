namespace MoodleConnector.Presentation;

public sealed record CalendarEventDto(Guid Id, string Title, string? Description, DateTimeOffset StartAt, DateTimeOffset? EndAt, string Type, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, IReadOnlyList<PlannerReferenceDto>? References = null);
public sealed record CalendarEventInput(string Title, string? Description, DateTimeOffset StartAt, DateTimeOffset? EndAt, string? Type, IReadOnlyList<PlannerReferenceInput>? References = null);
public sealed record CalendarEventUpdateInput(string Title, string? Description, DateTimeOffset StartAt, DateTimeOffset? EndAt, string? Type, IReadOnlyList<PlannerReferenceInput>? References = null);

