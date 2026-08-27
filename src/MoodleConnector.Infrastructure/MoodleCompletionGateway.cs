using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.MoodleApi;

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
        return ParseActivitiesCompletion(payload.GetRawText(), "core_completion_get_activities_completion_status");
    }

    private async Task<(bool Completed, long Timecompleted)> FetchCourseCompletionAsync(
        MoodleConnectorCredentials credentials,
        long courseId,
        long studentId,
        CancellationToken cancellationToken)
    {
        var payload = await restClient.CallAsync(credentials, "core_completion_get_course_completion_status", new Dictionary<string, object?>
        {
            ["courseid"] = courseId.ToString(CultureInfo.InvariantCulture),
            ["userid"] = studentId.ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);
        return ParseCourseCompletion(payload.GetRawText(), "core_completion_get_course_completion_status");
    }

    private static List<ActivityCompletionStatus> ParseActivitiesCompletion(string payload, string functionName)
    {
        var items = new List<ActivityCompletionStatus>();

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw InvalidCompletionPayload(functionName, "A resposta de conclusão de atividades está vazia.");
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("statuses", out var statuses) || statuses.ValueKind != JsonValueKind.Array)
            {
                throw InvalidCompletionPayload(functionName, "A resposta de conclusão de atividades não contém statuses.");
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
        }
        catch (JsonException exception)
        {
            throw InvalidCompletionPayload(functionName, "A resposta de conclusão de atividades não é um JSON válido.", exception);
        }

        return items;
    }

    private static (bool Completed, long Timecompleted) ParseCourseCompletion(string payload, string functionName)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw InvalidCompletionPayload(functionName, "A resposta de conclusão do curso está vazia.");
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("completionstatus", out var completionStatus) || completionStatus.ValueKind != JsonValueKind.Object)
            {
                throw InvalidCompletionPayload(functionName, "A resposta de conclusão do curso não contém completionstatus.");
            }

            if (!TryReadBoolProperty(completionStatus, "completed", out var completed))
            {
                throw InvalidCompletionPayload(functionName, "A resposta de conclusão do curso não contém um estado completed válido.");
            }

            var timecompleted = ReadLongProperty(completionStatus, "timecompleted") ?? 0;

            return (completed, timecompleted);
        }
        catch (JsonException exception)
        {
            throw InvalidCompletionPayload(functionName, "A resposta de conclusão do curso não é um JSON válido.", exception);
        }
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
        return TryReadBoolProperty(element, propertyName, out var value) && value;
    }

    private static bool TryReadBoolProperty(JsonElement element, string propertyName, out bool result)
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            if (value.ValueKind == JsonValueKind.True) { result = true; return true; }
            if (value.ValueKind == JsonValueKind.False) { result = false; return true; }
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var num)) { result = num > 0; return true; }
            if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var b)) { result = b; return true; }
            if (value.ValueKind == JsonValueKind.String && value.GetString() == "1") { result = true; return true; }
            if (value.ValueKind == JsonValueKind.String && value.GetString() == "0") { result = false; return true; }
        }

        result = false;
        return false;
    }

    private static MoodleApiException InvalidCompletionPayload(
        string functionName,
        string message,
        Exception? innerException = null) =>
        new(
            MoodleErrorContract.InvalidResponse,
            message,
            innerException: innerException,
            functionName: functionName,
            stage: MoodleIntegrationStage.ResponseParsing);

    private static long ParseMoodleId(string value, string parameterName)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
        {
            return id;
        }

        throw new ArgumentException($"O parametro {parameterName} deve ser um identificador numerico do Moodle.", parameterName);
    }

}
