using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Domain;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleCoursesGateway(
    IOptions<MoodleApiOptions> options,
    IMemoryCache cache,
    IMoodleConnectorCredentialsProvider credentialsProvider,
    IMoodleRestClient restClient,
    IMoodleFunctionCatalog functionCatalog,
    IMoodleBusinessFlowRegistry businessFlows,
    IMoodleResourceResolver resourceResolver) : IMoodleCoursesGateway
{
    private readonly MoodleApiOptions _options = options.Value;
    private static readonly TimeSpan CourseListCacheDuration = TimeSpan.FromMinutes(10);

    public async Task<PagedCourses> GetMyCoursesAsync(
        string userExternalId,
        int limit,
        int page,
        CancellationToken cancellationToken)
    {
        var courses = await GetCachedCoursesAsync(userExternalId, cancellationToken);
        var skip = (page - 1) * limit;
        var items = courses.Skip(skip).Take(limit).ToArray();
        return new PagedCourses(items, courses.Count, page, limit);
    }

    public async Task<IReadOnlyList<CourseSummary>> SearchMyCoursesAsync(
        string userExternalId,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var normalizedQuery = query.Trim();
        var reference = resourceResolver.Resolve(normalizedQuery);
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var profile = await functionCatalog.GetCurrentAsync(false, cancellationToken);
        var strategy = businessFlows.ResolveStrategy("buscar_cursos", profile);
        if (strategy?.StrategyName == "course_search" && reference.Type is MoodleResourceType.SearchTerm or MoodleResourceType.SearchUrl)
        {
            var searched = await SearchCoursesAsync(credentials, reference.Value, cancellationToken);
            return searched.Take(limit).ToArray();
        }
        if (strategy?.StrategyName == "course_by_field" && reference.Type is MoodleResourceType.CourseId or MoodleResourceType.CourseUrl or MoodleResourceType.IdNumber or MoodleResourceType.ShortName or MoodleResourceType.CategoryId or MoodleResourceType.CategoryUrl)
        {
            var field = GetMoodleField(reference.Type);
            var found = await GetCoursesByFieldAsync(credentials, field, reference.Value, cancellationToken);
            return found.Take(limit).ToArray();
        }

        var courses = await GetCachedCoursesAsync(userExternalId, cancellationToken);

        return courses
            .Where(course => MatchesCourse(course, normalizedQuery))
            .Take(limit)
            .ToArray();
    }

    public async Task<CourseSummary?> GetMyCourseAsync(
        string userExternalId,
        string courseId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return null;
        }

        var reference = resourceResolver.Resolve(courseId);
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var profile = await functionCatalog.GetCurrentAsync(false, cancellationToken);
        var strategy = businessFlows.ResolveStrategy("consultar_curso", profile);
        if (strategy?.StrategyName == "course_by_field" && reference.Type is MoodleResourceType.CourseId or MoodleResourceType.CourseUrl or MoodleResourceType.IdNumber or MoodleResourceType.ShortName)
        {
            var found = await GetCoursesByFieldAsync(credentials, GetMoodleField(reference.Type), reference.Value, cancellationToken);
            var course = found.FirstOrDefault();
            if (course is not null)
            {
                if (await IsEnrolledInCourseAsync(credentials, userExternalId, course.CourseId, profile, cancellationToken))
                {
                    return course;
                }

                throw new MoodleApiException(
                    "not_enrolled",
                    "O curso foi localizado, mas o usuario atual nao possui matricula nele.");
            }
        }

        var courses = await GetCachedCoursesAsync(userExternalId, cancellationToken);
        return courses.FirstOrDefault(course =>
            string.Equals(course.CourseId, courseId.Trim(), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(course.ShortName, courseId.Trim(), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(course.IdNumber, courseId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<CourseSummary>> GetCachedCoursesAsync(
        string userExternalId,
        CancellationToken cancellationToken)
    {
        if (_options.UseStubData)
        {
            throw new InvalidOperationException("UseStubData esta desativado para fluxos reais. Ajuste a configuracao para usar Moodle real.");
        }

        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var moodleUserId = await ResolveMoodleUserIdAsync(credentials, userExternalId, cancellationToken);
        var cacheKey = $"moodle:courses:{credentials.ConnectionId}:{moodleUserId}";
        return await cache.GetOrCreateAsync(
            cacheKey,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CourseListCacheDuration;
                entry.SlidingExpiration = TimeSpan.FromMinutes(3);

                var moodleCourses = await GetCoursesAsync(credentials, moodleUserId, cancellationToken);
                return moodleCourses
                    .Select(course => new CourseSummary(
                        course.Id.ToString(CultureInfo.InvariantCulture),
                        course.IdNumber,
                        course.ShortName,
                        course.FullName,
                        course.DisplayName,
                        course.CategoryId,
                        course.CategoryName,
                        ToDateTimeOffset(course.StartDate),
                        ToDateTimeOffset(course.EndDate),
                        ToBool(course.Visible),
                        course.ViewUrl,
                        course.CourseImage,
                        ToDecimal(course.Progress),
                        ToBool(course.HasProgress),
                        ToBool(course.IsFavourite),
                        ToDateTimeOffset(course.TimeAccess)))
                    .ToArray();
            }) ?? [];
    }

    private async Task<IReadOnlyList<CourseDto>> GetCoursesAsync(MoodleConnectorCredentials credentials, int moodleUserId, CancellationToken cancellationToken)
    {
        var profile = await functionCatalog.GetCurrentAsync(false, cancellationToken);
        var strategy = businessFlows.ResolveStrategy("listar_cursos_ativos", profile);
        if (strategy?.StrategyName == "timeline")
        {
            var timelinePayload = await restClient.CallAsync(
                credentials,
                "core_course_get_enrolled_courses_by_timeline_classification",
                new Dictionary<string, object?>
                {
                    ["classification"] = "inprogress",
                    ["limit"] = 1_000,
                    ["offset"] = 0,
                    ["sort"] = "fullname"
                },
                cancellationToken);
            var timeline = JsonSerializer.Deserialize<TimelineCoursesResponseDto>(timelinePayload.GetRawText());
            return timeline?.Courses ?? [];
        }

        if (strategy?.StrategyName != "enrolled_courses_fallback")
        {
            throw new MoodleApiException(
                "flow_unavailable",
                "O fluxo listar_cursos_ativos nao possui uma estrategia compativel com as funcoes habilitadas nesta conexao Moodle.");
        }

        var payload = await restClient.CallAsync(
            credentials,
            "core_enrol_get_users_courses",
            new Dictionary<string, string>
            {
                ["userid"] = moodleUserId.ToString(CultureInfo.InvariantCulture)
            }.ToDictionary(pair => pair.Key, pair => (object?)pair.Value),
            cancellationToken);

        return JsonSerializer.Deserialize<List<CourseDto>>(payload.GetRawText()) ?? [];
    }

    private async Task<IReadOnlyList<CourseSummary>> GetCoursesByFieldAsync(
        MoodleConnectorCredentials credentials,
        string field,
        string value,
        CancellationToken cancellationToken)
    {
        var payload = await restClient.CallAsync(
            credentials,
            "core_course_get_courses_by_field",
            new Dictionary<string, object?> { ["field"] = field, ["value"] = value },
            cancellationToken);
        var response = JsonSerializer.Deserialize<CoursesByFieldResponseDto>(payload.GetRawText());
        return (response?.Courses ?? []).Select(ToCourseSummary).ToArray();
    }

    private async Task<IReadOnlyList<CourseSummary>> SearchCoursesAsync(
        MoodleConnectorCredentials credentials,
        string query,
        CancellationToken cancellationToken)
    {
        var payload = await restClient.CallAsync(
            credentials,
            "core_course_search_courses",
            new Dictionary<string, object?> { ["criterianame"] = "search", ["criteriavalue"] = query },
            cancellationToken);
        var response = JsonSerializer.Deserialize<CourseSearchResponseDto>(payload.GetRawText());
        return (response?.Courses ?? []).Select(ToCourseSummary).ToArray();
    }

    private async Task<bool> IsEnrolledInCourseAsync(
        MoodleConnectorCredentials credentials,
        string userExternalId,
        string courseId,
        MoodleFunctionProfile profile,
        CancellationToken cancellationToken)
    {
        if (int.TryParse(userExternalId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var moodleUserId) &&
            long.TryParse(courseId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCourseId) &&
            profile.Functions.Any(function => function.IsAvailable && string.Equals(function.Name, "core_enrol_get_enrolled_users", StringComparison.OrdinalIgnoreCase)))
        {
            var payload = await restClient.CallAsync(
                credentials,
                "core_enrol_get_enrolled_users",
                new Dictionary<string, object?> { ["courseid"] = parsedCourseId },
                cancellationToken);
            var enrolledUsers = JsonSerializer.Deserialize<IReadOnlyList<EnrolledUserDto>>(payload.GetRawText()) ?? [];
            return enrolledUsers.Any(user => user.Id == moodleUserId);
        }

        var enrolledCourses = await GetCachedCoursesAsync(userExternalId, cancellationToken);
        return enrolledCourses.Any(course => string.Equals(course.CourseId, courseId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<int> ResolveMoodleUserIdAsync(MoodleConnectorCredentials credentials, string userExternalId, CancellationToken cancellationToken)
    {
        if (int.TryParse(userExternalId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var moodleUserId))
        {
            return moodleUserId;
        }

        var payload = await restClient.CallAsync(
            credentials,
            "core_webservice_get_site_info",
            new Dictionary<string, object?>(),
            cancellationToken);

        if (!payload.TryGetProperty("userid", out var userIdElement) || userIdElement.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidOperationException("Nao foi possivel resolver o usuario Moodle a partir do token atual.");
        }

        return userIdElement.GetInt32();
    }

    private static DateTimeOffset? ToDateTimeOffset(JsonElement value)
    {
        var seconds = ToInt64(value);
        return seconds is > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds.Value) : null;
    }

    private static long? ToInt64(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }

    private static decimal? ToDecimal(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }

    private static bool? ToBool(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt32(out var number) => number != 0,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var boolean) => boolean,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number != 0,
            _ => null
        };
    }

    private static bool MatchesCourse(CourseSummary course, string query)
    {
        return Contains(course.CourseId, query) ||
               Contains(course.IdNumber, query) ||
               Contains(course.ShortName, query) ||
               Contains(course.FullName, query) ||
               Contains(course.DisplayName, query) ||
               Contains(course.CategoryName, query);
    }

    private static bool Contains(string? value, string query)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetMoodleField(MoodleResourceType type) => type switch
    {
        MoodleResourceType.CourseId or MoodleResourceType.CourseUrl => "id",
        MoodleResourceType.IdNumber => "idnumber",
        MoodleResourceType.ShortName => "shortname",
        MoodleResourceType.CategoryId or MoodleResourceType.CategoryUrl => "category",
        _ => "id"
    };

    private static CourseSummary ToCourseSummary(CourseDto course) => new(
        course.Id.ToString(CultureInfo.InvariantCulture),
        course.IdNumber,
        course.ShortName,
        course.FullName,
        course.DisplayName,
        course.CategoryId,
        course.CategoryName,
        ToDateTimeOffset(course.StartDate),
        ToDateTimeOffset(course.EndDate),
        ToBool(course.Visible),
        course.ViewUrl,
        course.CourseImage,
        ToDecimal(course.Progress),
        ToBool(course.HasProgress),
        ToBool(course.IsFavourite),
        ToDateTimeOffset(course.TimeAccess));

    private sealed class CourseDto
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("shortname")]
        public string? ShortName { get; init; }

        [JsonPropertyName("idnumber")]
        public string? IdNumber { get; init; }

        [JsonPropertyName("fullname")]
        public string FullName { get; init; } = string.Empty;

        [JsonPropertyName("displayname")]
        public string? DisplayName { get; init; }

        [JsonPropertyName("categoryid")]
        public long? CategoryId { get; init; }

        [JsonPropertyName("categoryname")]
        public string? CategoryName { get; init; }

        [JsonPropertyName("startdate")]
        public JsonElement StartDate { get; init; }

        [JsonPropertyName("enddate")]
        public JsonElement EndDate { get; init; }

        [JsonPropertyName("visible")]
        public JsonElement Visible { get; init; }

        [JsonPropertyName("viewurl")]
        public string? ViewUrl { get; init; }

        [JsonPropertyName("courseimage")]
        public string? CourseImage { get; init; }

        [JsonPropertyName("progress")]
        public JsonElement Progress { get; init; }

        [JsonPropertyName("hasprogress")]
        public JsonElement HasProgress { get; init; }

        [JsonPropertyName("isfavourite")]
        public JsonElement IsFavourite { get; init; }

        [JsonPropertyName("timeaccess")]
        public JsonElement TimeAccess { get; init; }
    }

    private sealed class CoursesByFieldResponseDto
    {
        [JsonPropertyName("courses")]
        public IReadOnlyList<CourseDto>? Courses { get; init; }
    }

    private sealed class CourseSearchResponseDto
    {
        [JsonPropertyName("courses")]
        public IReadOnlyList<CourseDto>? Courses { get; init; }
    }

    private sealed class TimelineCoursesResponseDto
    {
        [JsonPropertyName("courses")]
        public IReadOnlyList<CourseDto>? Courses { get; init; }
    }

    private sealed class EnrolledUserDto
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }
    }
}
