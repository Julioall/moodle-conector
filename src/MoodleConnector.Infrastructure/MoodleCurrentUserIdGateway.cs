using System.Text.Json;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleCurrentUserIdGateway(
    IMoodleConnectorCredentialsProvider credentialsProvider,
    IMoodleRestClient restClient) : IMoodleCurrentUserIdGateway
{
    public async Task<long> GetCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var payload = await restClient.CallAsync(
            credentials,
            "core_webservice_get_site_info",
            new Dictionary<string, object?>(),
            cancellationToken);

        if (!payload.TryGetProperty("userid", out var userIdElement) ||
            userIdElement.ValueKind != JsonValueKind.Number ||
            !userIdElement.TryGetInt64(out var moodleUserId))
        {
            throw new InvalidOperationException("Nao foi possivel resolver o usuario Moodle a partir da conexao atual.");
        }

        return moodleUserId;
    }
}
