using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Activities;

public sealed record ListCourseActivitiesQuery(
    string UserExternalId,
    string CourseId,
    IReadOnlyCollection<string> ActivityTypes,
    bool IncludeHidden) : IRequest<CourseActivitiesSummary?>;

public sealed record GetCourseActivityQuery(
    string UserExternalId,
    string CourseId,
    string ActivityId,
    IReadOnlyCollection<string> AllowedActivityTypes) : IRequest<CourseActivitySummary?>;

public sealed record ListActivityDeadlinesQuery(
    string UserExternalId,
    string CourseId,
    IReadOnlyCollection<string> ActivityTypes,
    bool IncludeHidden) : IRequest<CourseActivityDeadlinesSummary?>;

public sealed class ListCourseActivitiesQueryHandler(
    IMoodleCoursesGateway coursesGateway,
    IMoodleCourseContentsGateway contentsGateway)
    : IRequestHandler<ListCourseActivitiesQuery, CourseActivitiesSummary?>
{
    public async Task<CourseActivitiesSummary?> Handle(
        ListCourseActivitiesQuery request,
        CancellationToken cancellationToken)
    {
        var course = await coursesGateway.GetMyCourseAsync(
            request.UserExternalId,
            request.CourseId,
            cancellationToken);
        if (course is null)
        {
            return null;
        }

        var activityTypes = NormalizeActivityTypes(request.ActivityTypes);
        var contents = await contentsGateway.GetCourseContentsAsync(
            request.UserExternalId,
            course.CourseId,
            activityTypes,
            request.IncludeHidden,
            onlyWithFiles: false,
            cancellationToken);

        var activities = contents.Sections
            .SelectMany(section => section.Modules)
            .Where(module => activityTypes.Contains(module.ModuleType, StringComparer.OrdinalIgnoreCase))
            .Select(ToActivity)
            .ToArray();

        return new CourseActivitiesSummary(
            contents.CourseId,
            activityTypes,
            request.IncludeHidden,
            activities.Length,
            activities.Count(activity => !activity.HasDates),
            activities.Count(activity => !activity.HasDeadline),
            activities);
    }

    internal static IReadOnlyCollection<string> NormalizeActivityTypes(IReadOnlyCollection<string> activityTypes)
    {
        var normalized = activityTypes
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length == 0 ? CourseActivityModuleTypes.All : normalized;
    }

    internal static CourseActivitySummary ToActivity(CourseModuleSummary module)
    {
        var openAt = FindDate(module.Dates, ["open", "abre", "abertura", "disponivel de", "available from"]);
        var dueAt = FindDate(module.Dates, ["due", "entrega", "prazo", "vencimento", "cut-off", "cutoff"]);
        var closeAt = FindDate(module.Dates, ["close", "fecha", "fechamento", "available until", "disponivel ate"]);
        var hasDeadline = dueAt is not null || closeAt is not null;

        return new CourseActivitySummary(
            module.ModuleId,
            module.InstanceId,
            module.ModuleType,
            module.Name,
            module.Url,
            module.Visible,
            module.UserVisible,
            module.Description,
            module.AvailabilityInfo,
            module.Dates.Count > 0,
            hasDeadline,
            openAt,
            dueAt,
            closeAt,
            module.Dates,
            module.Files.Count);
    }

    private static DateTimeOffset? FindDate(IReadOnlyList<CourseModuleDate> dates, IReadOnlyCollection<string> labels)
    {
        return dates
            .FirstOrDefault(date => labels.Any(label =>
                date.Label.Contains(label, StringComparison.OrdinalIgnoreCase)))
            ?.Date;
    }
}

public sealed class GetCourseActivityQueryHandler(
    IMoodleCoursesGateway coursesGateway,
    IMoodleCourseContentsGateway contentsGateway)
    : IRequestHandler<GetCourseActivityQuery, CourseActivitySummary?>
{
    public async Task<CourseActivitySummary?> Handle(
        GetCourseActivityQuery request,
        CancellationToken cancellationToken)
    {
        var course = await coursesGateway.GetMyCourseAsync(
            request.UserExternalId,
            request.CourseId,
            cancellationToken);
        if (course is null || string.IsNullOrWhiteSpace(request.ActivityId))
        {
            return null;
        }

        var activityTypes = ListCourseActivitiesQueryHandler.NormalizeActivityTypes(request.AllowedActivityTypes);
        var contents = await contentsGateway.GetCourseContentsAsync(
            request.UserExternalId,
            course.CourseId,
            activityTypes,
            includeHidden: true,
            onlyWithFiles: false,
            cancellationToken);

        var normalizedActivityId = request.ActivityId.Trim();
        var module = contents.Sections
            .SelectMany(section => section.Modules)
            .FirstOrDefault(activity =>
                activityTypes.Contains(activity.ModuleType, StringComparer.OrdinalIgnoreCase) &&
                (string.Equals(activity.ModuleId, normalizedActivityId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(activity.InstanceId, normalizedActivityId, StringComparison.OrdinalIgnoreCase)));

        return module is null ? null : ListCourseActivitiesQueryHandler.ToActivity(module);
    }
}

public sealed class ListActivityDeadlinesQueryHandler(
    IMoodleCoursesGateway coursesGateway,
    IMoodleCourseContentsGateway contentsGateway)
    : IRequestHandler<ListActivityDeadlinesQuery, CourseActivityDeadlinesSummary?>
{
    public async Task<CourseActivityDeadlinesSummary?> Handle(
        ListActivityDeadlinesQuery request,
        CancellationToken cancellationToken)
    {
        var activitiesHandler = new ListCourseActivitiesQueryHandler(coursesGateway, contentsGateway);
        var activities = await activitiesHandler.Handle(
            new ListCourseActivitiesQuery(
                request.UserExternalId,
                request.CourseId,
                request.ActivityTypes,
                request.IncludeHidden),
            cancellationToken);
        if (activities is null)
        {
            return null;
        }

        return new CourseActivityDeadlinesSummary(
            activities.CourseId,
            activities.ActivityTypeFilters,
            activities.IncludeHidden,
            activities.Total,
            activities.WithoutDatesCount,
            activities.WithoutDeadlineCount,
            activities.Activities.Select(activity => new CourseActivityDeadlineSummary(
                activity.ActivityId,
                activity.InstanceId,
                activity.ActivityType,
                activity.Name,
                activity.Visible,
                activity.UserVisible,
                activity.HasDates,
                activity.HasDeadline,
                activity.OpenAt,
                activity.DueAt,
                activity.CloseAt,
                activity.Dates)).ToArray());
    }
}
