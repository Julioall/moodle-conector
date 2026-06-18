using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleCompletionGateway(
    HttpClient httpClient,
    IOptions<MoodleApiOptions> options,
    IMoodleAccessTokenProvider tokenProvider,
    IMoodleConnectorCredentialsProvider credentialsProvider) : IMoodleCompletionGateway
{
    private readonly MoodleApiOptions _options = options.Value;

    public async Task<CourseCompletionStatus> GetStudentCompletionAsync(
        string courseId,
        string studentId,
        CancellationToken cancellationToken)
    {
        var courseIdNumber = ParseMoodleId(courseId, nameof(courseId));
        var studentIdNumber = ParseMoodleId(studentId, nameof(studentId));
        
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        
        var endpoint = $"{credentials.BaseUrl.TrimEnd('/')}/webservice/rest/server.php";

        // Fetch Activities Completion
        var activitiesCompletionTask = FetchActivitiesCompletionAsync(endpoint, token, courseIdNumber, studentIdNumber, cancellationToken);
        
        // Fetch Course Completion
        var courseCompletionTask = FetchCourseCompletionAsync(endpoint, token, courseIdNumber, studentIdNumber, cancellationToken);

        await Task.WhenAll(activitiesCompletionTask, courseCompletionTask);

        var activities = activitiesCompletionTask.Result;
        var courseCompletion = courseCompletionTask.Result;

        return new CourseCompletionStatus(
            Completed: courseCompletion.Completed,
            Timecompleted: courseCompletion.Timecompleted,
            Activities: activities);
    }

    private async Task<List<ActivityCompletionStatus>> FetchActivitiesCompletionAsync(
        string endpoint,
        string token,
        long courseId,
        long studentId,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["wstoken"] = token,
            ["wsfunction"] = "core_completion_get_activities_completion_status",
            ["moodlewsrestformat"] = "json",
            ["courseid"] = courseId.ToString(CultureInfo.InvariantCulture),
            ["userid"] = studentId.ToString(CultureInfo.InvariantCulture)
        });
        
        using var response = await httpClient.PostAsync(endpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfMoodleReturnedError(payload);

        return ParseActivitiesCompletion(payload);
    }

    private async Task<(bool Completed, long Timecompleted)> FetchCourseCompletionAsync(
        string endpoint,
        string token,
        long courseId,
        long studentId,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["wstoken"] = token,
            ["wsfunction"] = "core_completion_get_course_completion_status",
            ["moodlewsrestformat"] = "json",
            ["courseid"] = courseId.ToString(CultureInfo.InvariantCulture),
            ["userid"] = studentId.ToString(CultureInfo.InvariantCulture)
        });
        
        using var response = await httpClient.PostAsync(endpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        
        // Sometimes course completion is not configured and Moodle returns an error for this function specifically.
        // We will catch it and return false instead of throwing if it's an error.
        if (IsMoodleError(payload))
        {
            return (false, 0);
        }

        return ParseCourseCompletion(payload);
    }

    private static List<ActivityCompletionStatus> ParseActivitiesCompletion(string payload)
    {
        var items = new List<ActivityCompletionStatus>();

        if (string.IsNullOrWhiteSpace(payload))
        {
            return items;
        }

        using var document = JsonDocument.Parse(payload);
        
        if (!document.RootElement.TryGetProperty("statuses", out var statuses) || statuses.ValueKind != JsonValueKind.Array)
        {
            return items;
        }

        foreach (var status in statuses.EnumerateArray())
        {
            items.Add(new ActivityCompletionStatus(
                Cmid: ReadStringProperty(status, "cmid") ?? string.Empty,
                Modname: ReadStringProperty(status, "modname") ?? string.Empty,
                Instance: ReadStringProperty(status, "instance") ?? string.Empty,
                State: ReadLongProperty(status, "state") ?? 0,
                Timecompleted: ReadLongProperty(status, "timecompleted") ?? 0,
                Tracking: ReadLongProperty(status, "tracking") ?? 0,
                Overrideby: ReadStringProperty(status, "overrideby"),
                Valueused: ReadBoolProperty(status, "valueused")
            ));
        }

        return items;
    }

    private static (bool Completed, long Timecompleted) ParseCourseCompletion(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return (false, 0);
        }

        using var document = JsonDocument.Parse(payload);
        
        if (!document.RootElement.TryGetProperty("completionstatus", out var completionStatus) || completionStatus.ValueKind != JsonValueKind.Object)
        {
            return (false, 0);
        }

        var completed = ReadBoolProperty(completionStatus, "completed");
        var timecompleted = ReadLongProperty(completionStatus, "timecompleted") ?? 0;

        return (completed, timecompleted);
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

    private static bool ReadBoolProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var num)) return num > 0;
            if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var b)) return b;
            if (value.ValueKind == JsonValueKind.String && value.GetString() == "1") return true;
        }
        return false;
    }

    private static long ParseMoodleId(string value, string parameterName)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
        {
            return id;
        }

        throw new ArgumentException($"O parametro {parameterName} deve ser um identificador numerico do Moodle.", parameterName);
    }

    private static bool IsMoodleError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload) || string.Equals(payload.Trim(), "null", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("exception", out _);
    }

    private static void ThrowIfMoodleReturnedError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload) || string.Equals(payload.Trim(), "null", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("exception", out var exceptionElement))
        {
            return;
        }

        var errorCode = document.RootElement.TryGetProperty("errorcode", out var errorCodeElement)
            ? errorCodeElement.GetString()
            : exceptionElement.GetString();
        throw new InvalidOperationException($"O Moodle rejeitou a leitura de progresso: {errorCode ?? "erro_desconhecido"}.");
    }
}
