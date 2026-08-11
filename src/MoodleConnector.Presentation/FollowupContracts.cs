namespace MoodleConnector.Presentation;

public sealed record FollowupDto(Guid Id, string StudentRef, string? CourseRef, string Kind, string Notes, DateTimeOffset OccurredAt, DateTimeOffset CreatedAt);
public sealed record FollowupInput(string StudentRef, string? CourseRef, string Kind, string Notes, DateTimeOffset? OccurredAt);
public sealed record AppMessagePrepareInput(string CourseId, string MessageType, IReadOnlyList<string> RecipientIds, string? CustomText);
public sealed record AppMessageConfirmInput(Guid PendingActionId, string ConfirmationText);


