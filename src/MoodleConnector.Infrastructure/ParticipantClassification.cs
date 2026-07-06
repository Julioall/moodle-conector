using System.Globalization;
using System.Text;
using MoodleConnector.Domain;

namespace MoodleConnector.Infrastructure;

internal enum ParticipantClassificationKind
{
    Student = 0,
    KnownStaff = 1,
    UncertainFallback = 2
}

internal static class ParticipantClassification
{
    private static readonly HashSet<string> StudentRoles =
        new(StringComparer.OrdinalIgnoreCase) { "student", "estudante", "aluno" };

    internal static ParticipantClassificationKind Classify(CourseParticipantSummary participant)
    {
        if (participant.Roles.Count == 0)
        {
            return ParticipantClassificationKind.UncertainFallback;
        }

        var normalizedRoles = participant.Roles
            .SelectMany(role => new[] { role.ShortName, role.Name })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Normalize(value!))
            .ToArray();

        if (normalizedRoles.Any(StudentRoles.Contains))
        {
            return ParticipantClassificationKind.Student;
        }

        return ParticipantClassificationKind.KnownStaff;
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
