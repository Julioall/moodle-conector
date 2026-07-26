using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleAccessTokenProvider(
    HttpClient httpClient,
    IMemoryCache cache,
    IMoodleConnectorCredentialsProvider credentialsProvider,
    IOptions<MoodleApiOptions> options,
    IOptions<ConnectorSecretsOptions> connectorSecretsOptions) : IMoodleAccessTokenProvider
{
    private readonly MoodleApiOptions _moodleOptions = options.Value;
    private readonly ConnectorSecretsOptions _secretOptions = connectorSecretsOptions.Value;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var cacheKey = $"moodle:token:{credentials.ConnectionId}";
        if (cache.TryGetValue(cacheKey, out string? cachedToken) && !string.IsNullOrWhiteSpace(cachedToken))
        {
            return cachedToken;
        }

        var serviceName = string.IsNullOrWhiteSpace(_moodleOptions.LoginService)
            ? "moodle_mobile_app"
            : _moodleOptions.LoginService;

        var endpoint = $"{credentials.BaseUrl.TrimEnd('/')}/login/token.php";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = credentials.Username,
            ["password"] = credentials.Password,
            ["service"] = serviceName
        });
        using var response = await httpClient.PostAsync(endpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Token))
        {
            throw new InvalidOperationException("Nao foi possivel obter token de acesso no Moodle para o cliente autenticado.");
        }

        var ttlMinutes = Math.Clamp(_secretOptions.TokenCacheMinutes, 1, 120);
        cache.Set(cacheKey, payload.Token, TimeSpan.FromMinutes(ttlMinutes));
        return payload.Token;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("errorcode")]
        public string? ErrorCode { get; set; }
    }
}
