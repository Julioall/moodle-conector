using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleCoursesGateway(
    HttpClient httpClient,
    IOptions<MoodleApiOptions> options,
    IMemoryCache cache,
    IMoodleAccessTokenProvider tokenProvider,
    IMoodleConnectorCredentialsProvider credentialsProvider) : IMoodleCoursesGateway
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
        var token = await ResolveReadTokenAsync(cancellationToken);

        var moodleUserId = await ResolveMoodleUserIdAsync(credentials.BaseUrl, token, userExternalId, cancellationToken);
        var cacheKey = $"moodle:courses:{credentials.ConnectionId}:{moodleUserId}";
        return await cache.GetOrCreateAsync(
            cacheKey,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CourseListCacheDuration;
                entry.SlidingExpiration = TimeSpan.FromMinutes(3);

                var moodleCourses = await GetCoursesAsync(credentials.BaseUrl, token, moodleUserId, cancellationToken);
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

    private async Task<IReadOnlyList<CourseDto>> GetCoursesAsync(string baseUrl, string token, int moodleUserId, CancellationToken cancellationToken)
    {
        var endpoint = BuildMoodleGetUrl(
            baseUrl,
            token,
            "core_enrol_get_users_courses",
            new Dictionary<string, string>
            {
                ["userid"] = moodleUserId.ToString(CultureInfo.InvariantCulture)
            });

        using var response = await httpClient.GetAsync(endpoint, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<List<CourseDto>>(cancellationToken: cancellationToken);
        return payload ?? [];
    }

    private static string BuildMoodleGetUrl(string baseUrl, string token, string wsFunction, IReadOnlyDictionary<string, string> parameters)
    {
        var builder = new StringBuilder(baseUrl.TrimEnd('/')).Append("/webservice/rest/server.php?");
        builder.Append("wstoken=").Append(Uri.EscapeDataString(token));
        builder.Append("&wsfunction=").Append(Uri.EscapeDataString(wsFunction));
        builder.Append("&moodlewsrestformat=json");

        foreach (var pair in parameters)
        {
            builder.Append('&')
                .Append(Uri.EscapeDataString(pair.Key))
                .Append('=')
                .Append(Uri.EscapeDataString(pair.Value));
        }

        return builder.ToString();
    }

    private async Task<int> ResolveMoodleUserIdAsync(string baseUrl, string token, string userExternalId, CancellationToken cancellationToken)
    {
        if (int.TryParse(userExternalId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var moodleUserId))
        {
            return moodleUserId;
        }

        var endpoint = BuildMoodleGetUrl(baseUrl, token, "core_webservice_get_site_info", new Dictionary<string, string>());
        using var response = await httpClient.GetAsync(endpoint, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!json.RootElement.TryGetProperty("userid", out var userIdElement) || userIdElement.ValueKind != JsonValueKind.Number)
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

    private async Task<string> ResolveReadTokenAsync(CancellationToken cancellationToken)
    {
        if (_options.AllowServiceTokenForReadOnlyQueries && !string.IsNullOrWhiteSpace(_options.ServiceToken))
        {
            return _options.ServiceToken;
        }

        return await tokenProvider.GetAccessTokenAsync(cancellationToken);
    }

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
}
