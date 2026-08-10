namespace MoodleConnector.Presentation;

public sealed record PortalOperationalReportDto(int OpenTasks, int CompletedTasks, int UpcomingEvents, int FollowupsRecorded, DateTimeOffset GeneratedAt);
