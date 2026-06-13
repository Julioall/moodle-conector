using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleGradingCapabilitiesGateway(
    HttpClient httpClient,
    IOptions<MoodleApiOptions> options,
    IMoodleAccessTokenProvider tokenProvider,
    IMoodleConnectorCredentialsProvider credentialsProvider) : IMoodleGradingCapabilitiesGateway
{
    private readonly MoodleApiOptions _options = options.Value;

    public async Task<MoodleWebServiceFunctionCatalog> GetFunctionCatalogAsync(
        string userExternalId,
        CancellationToken cancellationToken)
    {
        if (_options.UseStubData)
        {
            throw new InvalidOperationException("UseStubData esta desativado para descoberta de funcoes Moodle reais.");
        }

        if (string.IsNullOrWhiteSpace(userExternalId))
        {
            throw new ArgumentException("O usuario Moodle e obrigatorio.", nameof(userExternalId));
        }

        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var token = await ResolveReadTokenAsync(cancellationToken);
        var endpoint = BuildMoodleGetUrl(
            credentials.BaseUrl,
            token,
            "core_webservice_get_site_info");

        using var response = await httpClient.GetAsync(endpoint, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<SiteInfoDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("O Moodle retornou uma resposta vazia ao consultar funcoes do servico.");

        if (!string.IsNullOrWhiteSpace(payload.Exception))
        {
            throw new InvalidOperationException("O Moodle nao permitiu consultar as funcoes do servico atual.");
        }

        var serviceName = string.IsNullOrWhiteSpace(_options.LoginService)
            ? "moodle_mobile_app"
            : _options.LoginService;

        return new MoodleWebServiceFunctionCatalog(
            serviceName,
            (payload.Functions ?? [])
                .Select(function => function.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()!);
    }

    private static string BuildMoodleGetUrl(string baseUrl, string token, string wsFunction)
    {
        var builder = new StringBuilder(baseUrl.TrimEnd('/')).Append("/webservice/rest/server.php?");
        builder.Append("wstoken=").Append(Uri.EscapeDataString(token));
        builder.Append("&wsfunction=").Append(Uri.EscapeDataString(wsFunction));
        builder.Append("&moodlewsrestformat=json");

        return builder.ToString();
    }

    private async Task<string> ResolveReadTokenAsync(CancellationToken cancellationToken)
    {
        if (_options.AllowServiceTokenForReadOnlyQueries && !string.IsNullOrWhiteSpace(_options.ServiceToken))
        {
            return _options.ServiceToken;
        }

        return await tokenProvider.GetAccessTokenAsync(cancellationToken);
    }

    private sealed class SiteInfoDto
    {
        [JsonPropertyName("functions")]
        public IReadOnlyList<FunctionDto>? Functions { get; init; }

        [JsonPropertyName("exception")]
        public string? Exception { get; init; }
    }

    private sealed class FunctionDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }
}
