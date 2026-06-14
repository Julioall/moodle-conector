using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Application.Grading;

internal static class GradingAccessControl
{
    private const string UnauthorizedMessage = "Usuario atual nao esta autorizado a acessar este lote de correcao.";

    public static void EnsureCanAccessBatch(AssistedGradingBatch batch, ICurrentUserContext currentUser)
    {
        if (string.Equals(batch.CreatedBySubject, currentUser.Subject, StringComparison.Ordinal))
        {
            return;
        }

        if (currentUser.HasScope("grading.admin") || currentUser.HasScope("moodle.admin"))
        {
            return;
        }

        throw new UnauthorizedAccessException(UnauthorizedMessage);
    }
}
