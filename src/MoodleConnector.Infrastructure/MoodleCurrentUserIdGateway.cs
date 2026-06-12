using System.Text;
using System.Text.Json;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleCurrentUserIdGateway(
    HttpClient httpClient,
    IMoodleConnectorCredentialsProvider credentialsProvider,
    IMoodleAccessTokenProvider tokenProvider) : IMoodleCurrentUserIdGateway
{
    public async Task<long> GetCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        var endpoint = BuildMoodleGetUrl(credentials.BaseUrl, token, "core_webservice_get_site_info");

        using var response = await httpClient.GetAsync(endpoint, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!json.RootElement.TryGetProperty("userid", out var userIdElement) ||
            userIdElement.ValueKind != JsonValueKind.Number ||
            !userIdElement.TryGetInt64(out var moodleUserId))
        {
            throw new InvalidOperationException("Nao foi possivel resolver o usuario Moodle a partir da conexao atual.");
        }

        return moodleUserId;
    }

    private static string BuildMoodleGetUrl(string baseUrl, string token, string wsFunction)
    {
        var builder = new StringBuilder(baseUrl.TrimEnd('/')).Append("/webservice/rest/server.php?");
        builder.Append("wstoken=").Append(Uri.EscapeDataString(token));
        builder.Append("&wsfunction=").Append(Uri.EscapeDataString(wsFunction));
        builder.Append("&moodlewsrestformat=json");
        return builder.ToString();
    }
}
