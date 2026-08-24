using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    IMoodleCurrentUserIdGateway currentUserIdGateway,
    IMoodleBusinessFlowRegistry businessFlows,
    IMoodleResourceResolver resourceResolver,
    ILogger<MoodleCoursesGateway>? logger = null) : IMoodleCoursesGateway
{
    private readonly MoodleApiOptions _options = options.Value;
    private readonly ILogger<MoodleCoursesGateway> _logger = logger ?? NullLogger<MoodleCoursesGateway>.Instance;
    private static readonly TimeSpan CourseListCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CategoryCacheDuration = TimeSpan.FromMinutes(30);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CourseCacheLocks = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<CourseHierarchyNode>> GetMyCourseHierarchyAsync(string userExternalId, CancellationToken cancellationToken)
    {
        var courses = await GetCachedCoursesAsync(userExternalId, cancellationToken);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var course in courses)
        {
            var parts = (course.CategoryName ?? "Sem categoria").Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var length = 1; length <= parts.Length; length++)
            {
                var path = string.Join(" > ", parts.Take(length));
                counts[path] = counts.TryGetValue(path, out var count) ? count + 1 : 1;
            }
        }

        return counts
            .Select(item => new CourseHierarchyNode(item.Key, item.Key.Split('>').Last().Trim(), item.Key.Count(character => character == '>'), item.Value))
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<PagedCourses> GetMyCoursesByCategoryAsync(string userExternalId, string categoryPath, int limit, int page, CancellationToken cancellationToken)
    {
        var courses = await GetCachedCoursesAsync(userExternalId, cancellationToken);
        var filtered = courses.Where(course => string.Equals(course.CategoryName ?? "Sem categoria", categoryPath.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        var skip = (page - 1) * limit;
        return new PagedCourses(filtered.Skip(skip).Take(limit).ToArray(), filtered.Length, page, limit);
    }

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
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<CourseSummary>? cached) && cached is not null)
        {
            return cached;
        }

        // Dashboard metrics arrive together. Collapse their cold-cache reads
        // so a large Moodle catalogue is fetched only once per connection.
        var gate = CourseCacheLocks.GetOrAdd(cacheKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue(cacheKey, out cached) && cached is not null)
            {
                return cached;
            }

            var profile = await functionCatalog.GetCurrentAsync(false, cancellationToken);
            var moodleCourses = await GetCoursesAsync(credentials, moodleUserId, profile, cancellationToken);
            var categories = await GetCategoryPathsAsync(credentials, profile, cancellationToken);
            _logger.LogInformation("Moodle courses loaded: {CourseCount}, categories: {CategoryCount}", moodleCourses.Count, categories.Count);
            var courses = moodleCourses
                .Select(course =>
                {
                    var categoryId = course.CategoryId ?? course.Category;
                    var categoryName = categoryId is long resolvedCategoryId &&
                                        categories.TryGetValue(resolvedCategoryId, out var resolvedCategoryPath) &&
                                        !string.IsNullOrWhiteSpace(resolvedCategoryPath)
                        ? resolvedCategoryPath
                        : course.CategoryName;

                    return new CourseSummary(
                        course.Id.ToString(CultureInfo.InvariantCulture),
                        course.IdNumber,
                        course.ShortName,
                        course.FullName,
                        course.DisplayName,
                        categoryId,
                        categoryName,
                        ToDateTimeOffset(course.StartDate),
                        ToDateTimeOffset(course.EndDate),
                        ToBool(course.Visible),
                        course.ViewUrl,
                        course.CourseImage,
                        ToDecimal(course.Progress),
                        ToBool(course.HasProgress),
                        ToBool(course.IsFavourite),
                        ToDateTimeOffset(course.TimeAccess));
                })
                .ToArray();
            cache.Set(cacheKey, courses, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CourseListCacheDuration,
                SlidingExpiration = TimeSpan.FromMinutes(3),
            });
            return courses;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<IReadOnlyDictionary<long, string>> GetCategoryPathsAsync(
        MoodleConnectorCredentials credentials,
        MoodleFunctionProfile profile,
        CancellationToken cancellationToken)
    {
        if (!IsFunctionAvailable(profile, "core_course_get_categories"))
        {
            _logger.LogWarning(
                "Moodle categories function is unavailable. Courses will remain without category paths. ConnectionId={ConnectionId} Alias={Alias}",
                credentials.ConnectionId,
                credentials.Alias);
            return new Dictionary<long, string>();
        }

        var cacheKey = $"moodle:course-categories:{credentials.ConnectionId}";
        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CategoryCacheDuration;
            JsonElement payload;
            try
            {
                payload = await restClient.CallAsync(credentials, "core_course_get_categories", new Dictionary<string, object?>(), cancellationToken);
            }
            catch (MoodleApiException exception)
            {
                _logger.LogWarning(exception, "Moodle categories unavailable; courses will remain without category paths.");
                return new Dictionary<long, string>();
            }
            var categories = JsonSerializer.Deserialize<IReadOnlyList<CategoryDto>>(payload.GetRawText()) ?? [];
            _logger.LogInformation("Moodle categories loaded: {CategoryCount}", categories.Count);
            var byId = categories.ToDictionary(category => category.Id);
            var paths = new Dictionary<long, string>();
            string BuildPath(long id)
            {
                if (paths.TryGetValue(id, out var cached)) return cached;
                if (!byId.TryGetValue(id, out var category)) return string.Empty;

                if (!string.IsNullOrWhiteSpace(category.Path))
                {
                    var pathNames = category.Path
                        .Split('/', StringSplitOptions.RemoveEmptyEntries)
                        .Select(value => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pathId) && byId.TryGetValue(pathId, out var pathCategory) ? pathCategory.Name.Trim() : string.Empty)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .ToArray();
                    if (pathNames.Length > 0)
                    {
                        var resolvedPath = string.Join(" > ", pathNames);
                        paths[id] = resolvedPath;
                        return resolvedPath;
                    }
                }

                var parent = category.Parent > 0 ? BuildPath(category.Parent) : string.Empty;
                var path = string.IsNullOrWhiteSpace(parent) ? category.Name : $"{parent} > {category.Name}";
                paths[id] = path;
                return path;
            }
            foreach (var category in categories) _ = BuildPath(category.Id);
            return paths;
        }) ?? new Dictionary<long, string>();
    }

    private async Task<IReadOnlyList<CourseDto>> GetCoursesAsync(
        MoodleConnectorCredentials credentials,
        long moodleUserId,
        MoodleFunctionProfile profile,
        CancellationToken cancellationToken)
    {
        var strategy = businessFlows.ResolveStrategy("listar_cursos_ativos", profile);
        if (strategy?.StrategyName == "timeline")
        {
            // The timeline endpoint is available on some Moodle installations,
            // but it commonly omits categoryid and can be slow. The enrolled
            // endpoint contains the category reference needed by the portal,
            // so use it first whenever the connection advertises it.
            if (IsFunctionAvailable(profile, "core_enrol_get_users_courses"))
            {
                try
                {
                    var enrolledCourses = await GetEnrolledCoursesAsync(credentials, moodleUserId, cancellationToken);
                    if (enrolledCourses.Count > 0)
                    {
                        if (RequiresTimelineCategoryEnrichment(enrolledCourses) &&
                            IsFunctionAvailable(profile, "core_course_get_enrolled_courses_by_timeline_classification"))
                        {
                            try
                            {
                                var timelineCourses = await GetTimelineCoursesAsync(credentials, cancellationToken);
                                var enrichedCourses = MergeCourseCategoryData(enrolledCourses, timelineCourses);
                                if (!ReferenceEquals(enrichedCourses, enrolledCourses))
                                {
                                    _logger.LogInformation(
                                        "Moodle enrolled courses were enriched with timeline category data. ConnectionId={ConnectionId} Alias={Alias}",
                                        credentials.ConnectionId,
                                        credentials.Alias);
                                }

                                return enrichedCourses;
                            }
                            catch (MoodleApiException exception)
                            {
                                _logger.LogWarning(exception, "Moodle timeline category enrichment failed; using enrolled courses payload as-is.");
                            }
                        }

                        return enrolledCourses;
                    }
                }
                catch (MoodleApiException exception)
                {
                    _logger.LogWarning(exception, "Moodle enrolled courses preferred path unavailable; falling back to timeline courses.");
                }
            }

            if (!IsFunctionAvailable(profile, "core_course_get_enrolled_courses_by_timeline_classification"))
            {
                throw new MoodleApiException(
                    "flow_unavailable",
                    "A conexão Moodle não possui uma função compatível para listar os cursos matriculados.");
            }

            return await GetTimelineCoursesAsync(credentials, cancellationToken);
        }

        if (strategy?.StrategyName != "enrolled_courses_fallback")
        {
            throw new MoodleApiException(
                "flow_unavailable",
                "O fluxo listar_cursos_ativos nao possui uma estrategia compativel com as funcoes habilitadas nesta conexao Moodle.");
        }

        return await GetEnrolledCoursesAsync(credentials, moodleUserId, cancellationToken);
    }

    private async Task<IReadOnlyList<CourseDto>> GetEnrolledCoursesAsync(
        MoodleConnectorCredentials credentials,
        long moodleUserId,
        CancellationToken cancellationToken)
    {
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

    private async Task<IReadOnlyList<CourseDto>> GetTimelineCoursesAsync(
        MoodleConnectorCredentials credentials,
        CancellationToken cancellationToken)
    {
        var timelinePayload = await restClient.CallAsync(
                credentials,
                "core_course_get_enrolled_courses_by_timeline_classification",
                new Dictionary<string, object?>
                {
                    // The portal needs the complete enrolled catalogue. Moodle
                    // still scopes this response to the authenticated user; an
                    // in-progress-only view made valid past/future courses look
                    // like a failed course import in Claris.
                    ["classification"] = "all",
                    ["limit"] = 1_000,
                    ["offset"] = 0,
                    ["sort"] = "fullname"
                },
                cancellationToken);
        var timeline = JsonSerializer.Deserialize<TimelineCoursesResponseDto>(timelinePayload.GetRawText());
        return timeline?.Courses ?? [];
    }

    private static bool IsFunctionAvailable(MoodleFunctionProfile profile, string functionName) =>
        profile.Functions.Any(function =>
            function.IsAvailable &&
            string.Equals(function.Name, functionName, StringComparison.OrdinalIgnoreCase));

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

    private async Task<long> ResolveMoodleUserIdAsync(MoodleConnectorCredentials credentials, string userExternalId, CancellationToken cancellationToken)
    {
        if (long.TryParse(userExternalId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var moodleUserId))
        {
            return moodleUserId;
        }

        return await currentUserIdGateway.GetCurrentUserIdAsync(cancellationToken);
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

    private static bool RequiresTimelineCategoryEnrichment(IReadOnlyList<CourseDto> courses) =>
        courses.Any(course => (course.CategoryId ?? course.Category) is null);

    private static IReadOnlyList<CourseDto> MergeCourseCategoryData(
        IReadOnlyList<CourseDto> primaryCourses,
        IReadOnlyList<CourseDto> fallbackCourses)
    {
        if (primaryCourses.Count == 0 || fallbackCourses.Count == 0)
        {
            return primaryCourses;
        }

        var fallbackById = fallbackCourses
            .GroupBy(course => course.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var changed = false;
        var merged = new CourseDto[primaryCourses.Count];

        for (var index = 0; index < primaryCourses.Count; index++)
        {
            var course = primaryCourses[index];
            if (!fallbackById.TryGetValue(course.Id, out var fallback))
            {
                merged[index] = course;
                continue;
            }

            var enriched = MergeCourseCategoryData(course, fallback);
            changed |= !ReferenceEquals(enriched, course);
            merged[index] = enriched;
        }

        return changed ? merged : primaryCourses;
    }

    private static CourseDto MergeCourseCategoryData(CourseDto course, CourseDto fallback)
    {
        var categoryId = course.CategoryId ?? fallback.CategoryId;
        var category = course.Category ?? fallback.Category;
        var categoryName = !string.IsNullOrWhiteSpace(course.CategoryName)
            ? course.CategoryName
            : fallback.CategoryName;

        if (categoryId == course.CategoryId &&
            category == course.Category &&
            string.Equals(categoryName, course.CategoryName, StringComparison.Ordinal))
        {
            return course;
        }

        return new CourseDto
        {
            Id = course.Id,
            ShortName = course.ShortName,
            IdNumber = course.IdNumber,
            FullName = course.FullName,
            DisplayName = course.DisplayName,
            CategoryId = categoryId,
            Category = category,
            CategoryName = categoryName,
            StartDate = course.StartDate,
            EndDate = course.EndDate,
            Visible = course.Visible,
            ViewUrl = course.ViewUrl,
            CourseImage = course.CourseImage,
            Progress = course.Progress,
            HasProgress = course.HasProgress,
            IsFavourite = course.IsFavourite,
            TimeAccess = course.TimeAccess
        };
    }

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

        [JsonPropertyName("category")]
        public long? Category { get; init; }

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

    private sealed class CategoryDto
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("parent")]
        public long Parent { get; init; }

        [JsonPropertyName("path")]
        public string? Path { get; init; }
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
