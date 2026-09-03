using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleAssignmentGradeReadGateway(
    IOptions<MoodleApiOptions> options,
    IMoodleConnectorCredentialsProvider credentialsProvider,
    IMoodleRestClient restClient) : IMoodleAssignmentGradeReadGateway
{
    private const string MoodleFunction = "mod_assign_get_grades";
    private readonly MoodleApiOptions _options = options.Value;

    public async Task<AssignmentExistingGrade?> GetExistingGradeAsync(
        string userExternalId,
        string assignmentId,
        string studentId,
        CancellationToken cancellationToken)
    {
        if (_options.UseStubData)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(userExternalId))
        {
            throw new ArgumentException("O usuario Moodle e obrigatorio.", nameof(userExternalId));
        }

        var grades = await GetExistingGradesAsync(
            userExternalId,
            assignmentId,
            [studentId],
            cancellationToken);
        return grades.GetValueOrDefault(studentId);
    }

    public async Task<IReadOnlyDictionary<string, AssignmentExistingGrade>> GetExistingGradesAsync(
        string userExternalId,
        string assignmentId,
        IReadOnlyCollection<string> studentIds,
        CancellationToken cancellationToken)
    {
        if (_options.UseStubData || studentIds.Count == 0)
        {
            return new Dictionary<string, AssignmentExistingGrade>(StringComparer.OrdinalIgnoreCase);
        }

        if (string.IsNullOrWhiteSpace(userExternalId))
        {
            throw new ArgumentException("O usuario Moodle e obrigatorio.", nameof(userExternalId));
        }

        var assignmentIdNumber = ParseMoodleId(assignmentId, nameof(assignmentId));
        var requestedStudentIds = studentIds
            .Select(studentId => ParseMoodleId(studentId, nameof(studentIds)))
            .ToHashSet();
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var payload = await restClient.CallAsync(credentials, MoodleFunction, new Dictionary<string, object?>
        {
            ["assignmentids[0]"] = assignmentIdNumber.ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);
        return ParseGrades(payload.GetRawText(), assignmentIdNumber, requestedStudentIds);
    }

    private static IReadOnlyDictionary<string, AssignmentExistingGrade> ParseGrades(
        string payload,
        long assignmentId,
        IReadOnlySet<long> requestedStudentIds)
    {
        var result = new Dictionary<string, AssignmentExistingGrade>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return result;
        }

        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("assignments", out var assignments) ||
            assignments.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var assignment in assignments.EnumerateArray())
        {
            if (!MatchesLongProperty(assignment, "assignmentid", assignmentId) ||
                !assignment.TryGetProperty("grades", out var grades) ||
                grades.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var grade in grades.EnumerateArray())
            {
                if (!TryReadLongProperty(grade, "userid", out var studentId) ||
                    !requestedStudentIds.Contains(studentId))
                {
                    continue;
                }

                var parsedGrade = ReadDecimalProperty(grade, "grade");
                var studentIdText = studentId.ToString(CultureInfo.InvariantCulture);
                result[studentIdText] = new AssignmentExistingGrade(
                    assignmentId.ToString(CultureInfo.InvariantCulture),
                    studentIdText,
                    parsedGrade,
                    HasGrade: parsedGrade is >= 0,
                    Feedback: ReadTextProperty(grade, "feedback")
                        ?? ReadTextProperty(grade, "feedbacktext")
                        ?? ReadTextProperty(grade, "feedbackcomments"),
                    GradeMax: ReadDecimalProperty(grade, "grademax"),
                    GraderId: ReadLongProperty(grade, "grader"),
                    TimeModified: ReadLongProperty(grade, "timemodified"));
            }
        }

        return result;
    }

    private static bool MatchesLongProperty(JsonElement element, string propertyName, long expected)
    {
        return TryReadLongProperty(element, propertyName, out var actual) && actual == expected;
    }

    private static bool TryReadLongProperty(JsonElement element, string propertyName, out long result)
    {
        result = 0;
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt64(out result),
            JsonValueKind.String => long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result),
            _ => false
        };
    }

    private static long? ReadLongProperty(JsonElement element, string propertyName) =>
        TryReadLongProperty(element, propertyName, out var value) ? value : null;

    private static decimal? ReadDecimalProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var grade) => grade,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var grade) => grade,
            _ => null
        };
    }

    private static string? ReadTextProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var childName in new[] { "text", "content", "message" })
            {
                if (value.TryGetProperty(childName, out var child) && child.ValueKind == JsonValueKind.String)
                {
                    return child.GetString();
                }
            }
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
