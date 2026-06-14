using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleAssignmentGradeReadGateway(
    HttpClient httpClient,
    IOptions<MoodleApiOptions> options,
    IMoodleAccessTokenProvider tokenProvider,
    IMoodleConnectorCredentialsProvider credentialsProvider) : IMoodleAssignmentGradeReadGateway
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
        var token = await ResolveTokenAsync(cancellationToken);
        var endpoint = $"{credentials.BaseUrl.TrimEnd('/')}/webservice/rest/server.php";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["wstoken"] = token,
            ["wsfunction"] = MoodleFunction,
            ["moodlewsrestformat"] = "json",
            ["assignmentids[0]"] = assignmentIdNumber.ToString(CultureInfo.InvariantCulture)
        });
        using var response = await httpClient.PostAsync(endpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfMoodleReturnedError(payload);

        return FindGrade(payload, assignmentIdNumber, studentIdNumber);
    }

    private async Task<string> ResolveTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.WriteServiceToken))
        {
            return _options.WriteServiceToken;
        }

        return await tokenProvider.GetAccessTokenAsync(cancellationToken);
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

    private static void ThrowIfMoodleReturnedError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload) ||
            string.Equals(payload.Trim(), "null", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("exception", out var exceptionElement))
        {
            return;
        }

        var errorCode = document.RootElement.TryGetProperty("errorcode", out var errorCodeElement)
            ? errorCodeElement.GetString()
            : exceptionElement.GetString();
        throw new InvalidOperationException($"O Moodle rejeitou a leitura de notas: {errorCode ?? "erro_desconhecido"}.");
    }
}
