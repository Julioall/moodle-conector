using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleCredentialValidator(
    HttpClient httpClient,
    IOptions<MoodleApiOptions> options) : IMoodleCredentialValidator
{
    public async Task<bool> ValidateAsync(string moodleBaseUrl, string username, string password, CancellationToken cancellationToken)
    {
        var serviceName = string.IsNullOrWhiteSpace(options.Value.LoginService)
            ? "moodle_mobile_app"
            : options.Value.LoginService;

        var baseUrl = moodleBaseUrl.Trim().TrimEnd('/');
        var query = $"{baseUrl}/login/token.php?username={Uri.EscapeDataString(username)}" +
                    $"&password={Uri.EscapeDataString(password)}" +
                    $"&service={Uri.EscapeDataString(serviceName)}";

        using var response = await httpClient.GetAsync(query, cancellationToken);
        if (!response.IsSuccessStatusCode) return false;

        var payload = await response.Content
            .ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);

        return payload is not null && !string.IsNullOrWhiteSpace(payload.Token);
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }
    }
}
