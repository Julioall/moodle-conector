using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleCurrentUserIdGateway(
    IMoodleFunctionCatalog functionCatalog) : IMoodleCurrentUserIdGateway
{
    public async Task<long> GetCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        var profile = await functionCatalog.GetCurrentAsync(false, cancellationToken);
        if (profile.MoodleUserId is not { } moodleUserId)
        {
            throw new InvalidOperationException("Nao foi possivel resolver o usuario Moodle a partir da conexao atual.");
        }

        return moodleUserId;
    }
}
