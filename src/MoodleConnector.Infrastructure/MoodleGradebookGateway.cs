using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleGradebookGateway(
    IOptions<MoodleApiOptions> options,
    IMoodleConnectorCredentialsProvider credentialsProvider,
    IMoodleRestClient restClient,
    IMemoryCache? memoryCache = null) : IMoodleGradebookGateway
{
    private const string MoodleFunction = "gradereport_user_get_grade_items";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);
    private readonly MoodleApiOptions _options = options.Value;

    public async Task<CourseGradebook> GetStudentGradebookAsync(
        string courseId,
        string studentId,
        CancellationToken cancellationToken)
    {
        var courseIdNumber = ParseMoodleId(courseId, nameof(courseId));
        var studentIdNumber = ParseMoodleId(studentId, nameof(studentId));
        
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var cacheKey = $"moodle-gradebook:{credentials.Alias}:{courseIdNumber}:{studentIdNumber}";
        if (memoryCache?.TryGetValue(cacheKey, out CourseGradebook? cached) == true && cached is not null)
        {
            return cached;
        }

        var payload = await restClient.CallAsync(credentials, MoodleFunction, new Dictionary<string, object?>
        {
            ["courseid"] = courseIdNumber.ToString(CultureInfo.InvariantCulture),
            ["userid"] = studentIdNumber.ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);

        var gradebook = ParseGradebook(payload.GetRawText(), courseId, studentId);
        memoryCache?.Set(cacheKey, gradebook, CacheDuration);
        return gradebook;
    }

    private static CourseGradebook ParseGradebook(string payload, string courseId, string studentId)
    {
        var items = new List<GradebookItem>();

        if (string.IsNullOrWhiteSpace(payload))
        {
            return new CourseGradebook(courseId, studentId, items);
        }

        using var document = JsonDocument.Parse(payload);
        
        if (!document.RootElement.TryGetProperty("usergrades", out var userGrades) || userGrades.ValueKind != JsonValueKind.Array)
        {
            return new CourseGradebook(courseId, studentId, items);
        }

        foreach (var userGrade in userGrades.EnumerateArray())
        {
            if (userGrade.TryGetProperty("gradeitems", out var gradeItems) && gradeItems.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in gradeItems.EnumerateArray())
                {
                    items.Add(new GradebookItem(
                        Id: ReadStringProperty(item, "id") ?? string.Empty,
                        ItemName: ReadStringProperty(item, "itemname") ?? string.Empty,
                        ItemType: ReadStringProperty(item, "itemtype") ?? string.Empty,
                        ItemModule: ReadStringProperty(item, "itemmodule") ?? string.Empty,
                        CategoryId: ReadStringProperty(item, "categoryid"),
                        GradeRaw: ReadDecimalProperty(item, "graderaw"),
                        GradeFormatted: ReadStringProperty(item, "gradeformatted"),
                        GradeMin: ReadDecimalProperty(item, "grademin", "min", "mingrade"),
                        GradeMax: ReadDecimalProperty(item, "grademax", "max", "maxgrade", "grade_max"),
                        PercentageFormatted: ReadDecimalProperty(item, "percentageformatted", "percentage", "percent"),
                        Feedback: ReadStringProperty(item, "feedback"),
                        FeedbackFormat: ReadStringProperty(item, "feedbackformat"),
                        GradedDateSubmitted: ReadLongProperty(item, "gradeddatesubmitted"),
                        GradedDateGraded: ReadLongProperty(item, "gradeddategraded"),
                        GraderId: ReadStringProperty(item, "grader")
                    ));
                }
            }
        }

        return new CourseGradebook(courseId, studentId, items);
    }

    private static string? ReadStringProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            if (value.ValueKind == JsonValueKind.String)
                return value.GetString();
            if (value.ValueKind == JsonValueKind.Number)
                return value.GetRawText();
        }
        return null;
    }

    private static decimal? ReadDecimalProperty(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var d))
                return d;
            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString()?.Trim().TrimEnd('%').Trim();
                var ptBrFirst = text?.Contains(',', StringComparison.Ordinal) == true &&
                    text.Contains('.', StringComparison.Ordinal) == false;
                if (ptBrFirst
                    ? decimal.TryParse(text, NumberStyles.Any, CultureInfo.GetCultureInfo("pt-BR"), out var ds) ||
                      decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out ds)
                    : decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out ds) ||
                      decimal.TryParse(text, NumberStyles.Any, CultureInfo.GetCultureInfo("pt-BR"), out ds))
                {
                    return ds;
                }
            }
        }
        return null;
    }

    private static long? ReadLongProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var d))
                return d;
            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var ds))
                return ds;
        }
        return null;
    }

    private static long ParseMoodleId(string value, string parameterName)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
        {
            return id;
        }

        throw new ArgumentException($"O parametro {parameterName} deve ser um identificador numerico do Moodle.", parameterName);
    }

}
