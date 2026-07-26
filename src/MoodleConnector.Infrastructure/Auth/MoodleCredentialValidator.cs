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
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = password,
            ["service"] = serviceName
        });
        using var response = await httpClient.PostAsync($"{baseUrl}/login/token.php", content, cancellationToken);
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
