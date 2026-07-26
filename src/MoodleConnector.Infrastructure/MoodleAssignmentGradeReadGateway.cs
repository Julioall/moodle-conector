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

        var assignmentIdNumber = ParseMoodleId(assignmentId, nameof(assignmentId));
        var studentIdNumber = ParseMoodleId(studentId, nameof(studentId));
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var payload = await restClient.CallAsync(credentials, MoodleFunction, new Dictionary<string, object?>
        {
            ["assignmentids[0]"] = assignmentIdNumber.ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);

        return FindGrade(payload.GetRawText(), assignmentIdNumber, studentIdNumber);
    }

    private static AssignmentExistingGrade? FindGrade(
        string payload,
        long assignmentId,
        long studentId)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("assignments", out var assignments) ||
            assignments.ValueKind != JsonValueKind.Array)
        {
            return null;
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
                if (!MatchesLongProperty(grade, "userid", studentId))
                {
                    continue;
                }

                var parsedGrade = ReadDecimalProperty(grade, "grade");
                return new AssignmentExistingGrade(
                    assignmentId.ToString(CultureInfo.InvariantCulture),
                    studentId.ToString(CultureInfo.InvariantCulture),
                    parsedGrade,
                    HasGrade: parsedGrade is >= 0);
            }
        }

        return null;
    }

    private static bool MatchesLongProperty(JsonElement element, string propertyName, long expected)
    {
        return element.TryGetProperty(propertyName, out var value) &&
            value.ValueKind switch
            {
                JsonValueKind.Number => value.TryGetInt64(out var actual) && actual == expected,
                JsonValueKind.String => long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var actual) && actual == expected,
                _ => false
            };
    }

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

    private static long ParseMoodleId(string value, string parameterName)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
        {
            return id;
        }

        throw new ArgumentException($"O parametro {parameterName} deve ser um identificador numerico do Moodle.", parameterName);
    }

}
