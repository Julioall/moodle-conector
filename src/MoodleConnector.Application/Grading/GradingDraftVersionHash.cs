using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Grading;

public static class GradingDraftVersionHash
{
    public static string Compute(AssistedGradingItem item)
    {
        var payload = string.Join(
            "|",
            item.Id.ToString("N"),
            item.BatchId.ToString("N"),
            item.FinalGrade?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            item.FinalFeedback ?? string.Empty,
            item.TeacherDecision ?? string.Empty,
            item.ReviewNotes ?? string.Empty,
            item.SubmissionContentHash ?? string.Empty,
            item.ReviewStatus.ToString(),
            item.CommitStatus.ToString(),
            item.UpdatedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
