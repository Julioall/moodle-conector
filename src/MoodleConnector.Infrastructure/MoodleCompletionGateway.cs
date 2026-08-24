using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleCompletionGateway(
    IOptions<MoodleApiOptions> options,
    IMoodleConnectorCredentialsProvider credentialsProvider,
    IMoodleRestClient restClient) : IMoodleCompletionGateway
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
        var activitiesCompletionTask = FetchActivitiesCompletionAsync(credentials, courseIdNumber, studentIdNumber, cancellationToken);
        var courseCompletionTask = FetchCourseCompletionAsync(credentials, courseIdNumber, studentIdNumber, cancellationToken);

        await Task.WhenAll(activitiesCompletionTask, courseCompletionTask);

        var activities = await activitiesCompletionTask;
        var courseCompletion = await courseCompletionTask;

        return new CourseCompletionStatus(
            Completed: courseCompletion.Completed,
            Timecompleted: courseCompletion.Timecompleted,
            Activities: activities);
    }

    private async Task<List<ActivityCompletionStatus>> FetchActivitiesCompletionAsync(
        MoodleConnectorCredentials credentials,
        long courseId,
        long studentId,
        CancellationToken cancellationToken)
    {
        var payload = await restClient.CallAsync(credentials, "core_completion_get_activities_completion_status", new Dictionary<string, object?>
        {
            ["courseid"] = courseId.ToString(CultureInfo.InvariantCulture),
            ["userid"] = studentId.ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);
        return ParseActivitiesCompletion(payload.GetRawText());
    }

    private async Task<(bool Completed, long Timecompleted)> FetchCourseCompletionAsync(
        MoodleConnectorCredentials credentials,
        long courseId,
        long studentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = await restClient.CallAsync(credentials, "core_completion_get_course_completion_status", new Dictionary<string, object?>
            {
                ["courseid"] = courseId.ToString(CultureInfo.InvariantCulture),
                ["userid"] = studentId.ToString(CultureInfo.InvariantCulture)
            }, cancellationToken);
            return ParseCourseCompletion(payload.GetRawText());
        }
        catch (MoodleConnector.Application.MoodleApi.MoodleApiException)
        {
            return (false, 0);
        }
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

}
