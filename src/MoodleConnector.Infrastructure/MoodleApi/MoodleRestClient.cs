using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;

namespace MoodleConnector.Infrastructure.MoodleApi;

internal sealed class MoodleRestClient(
    HttpClient httpClient,
    IOptions<MoodleApiOptions> options,
    IMoodleAccessTokenProvider tokenProvider) : IMoodleRestClient
{
    private readonly MoodleApiOptions _options = options.Value;

    public Task<JsonElement> CallAsync(
        MoodleConnectorCredentials connection,
        string functionName,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken) =>
        CallAsync(connection, functionName, parameters, allowServiceToken: true, cancellationToken);

    public async Task<JsonElement> CallAsync(
        MoodleConnectorCredentials connection,
        string functionName,
        IReadOnlyDictionary<string, object?> parameters,
        bool allowServiceToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(functionName))
        {
            throw new ArgumentException("A funcao Moodle e obrigatoria.", nameof(functionName));
        }

        var token = await ResolveTokenAsync(allowServiceToken, cancellationToken);
        var values = new Dictionary<string, string>(MoodleParameterSerializer.Flatten(parameters), StringComparer.Ordinal)
        {
            ["wstoken"] = token,
            ["wsfunction"] = functionName.Trim(),
            ["moodlewsrestformat"] = "json"
        };
        var endpoint = BuildEndpoint(connection.BaseUrl);

        using var content = new FormUrlEncodedContent(values);
        using var response = await httpClient.PostAsync(endpoint, content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (payload.TrimStart().StartsWith('{'))
            {
                // Moodle frequently returns a structured Web Service error with a 4xx status.
                // Preserve its safe, normalized error code when it is available.
                _ = MoodleResponseParser.Parse(payload);
            }

            throw new MoodleApiException(
                response.StatusCode == HttpStatusCode.Unauthorized ? "invalid_token" : "moodle_unavailable",
                "A chamada ao Moodle falhou.",
                (int)response.StatusCode);
        }

        return MoodleResponseParser.Parse(payload);
    }

    private static string BuildEndpoint(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new MoodleApiException("wrong_moodle_alias", "A conexao Moodle selecionada possui URL invalida.");
        }

        return new Uri(baseUri.ToString().TrimEnd('/') + "/webservice/rest/server.php").ToString();
    }

    private async Task<string> ResolveTokenAsync(bool allowServiceToken, CancellationToken cancellationToken)
    {
        if (allowServiceToken && _options.AllowServiceTokenForReadOnlyQueries && !string.IsNullOrWhiteSpace(_options.ServiceToken))
        {
            return _options.ServiceToken;
        }

        return await tokenProvider.GetAccessTokenAsync(cancellationToken);
    }
}
