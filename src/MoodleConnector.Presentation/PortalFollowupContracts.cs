namespace MoodleConnector.Presentation;

public sealed record PortalFollowupDto(Guid Id, string StudentRef, string? CourseRef, string Kind, string Notes, DateTimeOffset OccurredAt, DateTimeOffset CreatedAt);
public sealed record PortalFollowupInput(string StudentRef, string? CourseRef, string Kind, string Notes, DateTimeOffset? OccurredAt);
