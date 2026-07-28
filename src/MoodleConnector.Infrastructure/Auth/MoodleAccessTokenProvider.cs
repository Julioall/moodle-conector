using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleAccessTokenProvider(
    HttpClient httpClient,
    IMemoryCache cache,
    IOptions<MoodleApiOptions> options,
    IOptions<ConnectorSecretsOptions> connectorSecretsOptions,
    ILogger<MoodleAccessTokenProvider> logger) : IMoodleAccessTokenProvider
{
    private readonly MoodleApiOptions _moodleOptions = options.Value;
    private readonly ConnectorSecretsOptions _secretOptions = connectorSecretsOptions.Value;

    public async Task<string> GetAccessTokenAsync(
        MoodleConnectorCredentials connection,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"moodle:token:{connection.ConnectionId}:{CreateCredentialFingerprint(connection)}";
        if (cache.TryGetValue(cacheKey, out string? cachedToken) && !string.IsNullOrWhiteSpace(cachedToken))
        {
            return cachedToken;
        }

        if (!Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("http" or "https"))
        {
            throw CreateFailure(
                MoodleErrorContract.NetworkError,
                connection,
                null,
                "The Moodle token endpoint URL is invalid.");
        }

        var serviceName = string.IsNullOrWhiteSpace(_moodleOptions.LoginService)
            ? "moodle_mobile_app"
            : _moodleOptions.LoginService;
        var endpoint = new Uri(baseUri.ToString().TrimEnd('/') + "/login/token.php");
        var auditId = Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = connection.Username,
                ["password"] = connection.Password,
                ["service"] = serviceName
            });
            using var response = await httpClient.PostAsync(endpoint, content, cancellationToken);
            TokenResponse? payload;
            try
            {
                payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
            }
            catch (JsonException exception)
            {
                throw CreateFailure(
                    MoodleErrorContract.InvalidResponse,
                    connection,
                    endpoint,
                    "The Moodle token endpoint returned invalid JSON.",
                    (int)response.StatusCode,
                    exception,
                    auditId,
                    stopwatch.ElapsedMilliseconds);
            }

            if (!response.IsSuccessStatusCode)
            {
                var code = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? MoodleErrorContract.AuthenticationFailed
                    : MoodleErrorContract.NetworkError;
                throw CreateFailure(
                    code,
                    connection,
                    endpoint,
                    "The Moodle token endpoint returned an unsuccessful HTTP response.",
                    (int)response.StatusCode,
                    auditId: auditId,
                    durationMs: stopwatch.ElapsedMilliseconds,
                    remoteErrorCode: payload?.ErrorCode);
            }

            if (payload is null || string.IsNullOrWhiteSpace(payload.Token))
            {
                var normalizedRemoteCode = MoodleErrorContract.NormalizeCode(payload?.ErrorCode);
                var code = normalizedRemoteCode == MoodleErrorContract.AuthenticationFailed ||
                           !string.IsNullOrWhiteSpace(payload?.Error)
                    ? MoodleErrorContract.AuthenticationFailed
                    : MoodleErrorContract.TokenMissing;
                throw CreateFailure(
                    code,
                    connection,
                    endpoint,
                    "The Moodle token endpoint did not return a token.",
                    (int)response.StatusCode,
                    auditId: auditId,
                    durationMs: stopwatch.ElapsedMilliseconds,
                    remoteErrorCode: payload?.ErrorCode);
            }

            var ttlMinutes = Math.Clamp(_secretOptions.TokenCacheMinutes, 1, 120);
            cache.Set(cacheKey, payload.Token, TimeSpan.FromMinutes(ttlMinutes));
            logger.LogInformation(
                "Moodle token acquired. AuditId={AuditId} ConnectionId={ConnectionId} Alias={Alias} Endpoint={Endpoint} HttpStatus={HttpStatus} DurationMs={DurationMs}",
                auditId,
                connection.ConnectionId,
                connection.Alias,
                endpoint.GetLeftPart(UriPartial.Path),
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);
            return payload.Token;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw CreateFailure(
                MoodleErrorContract.RequestTimeout,
                connection,
                endpoint,
                "The Moodle token request timed out.",
                innerException: exception,
                auditId: auditId,
                durationMs: stopwatch.ElapsedMilliseconds);
        }
        catch (HttpRequestException exception)
        {
            throw CreateFailure(
                MoodleErrorContract.NetworkError,
                connection,
                endpoint,
                "The Moodle token request failed at the network layer.",
                exception.StatusCode is null ? null : (int)exception.StatusCode.Value,
                exception,
                auditId,
                stopwatch.ElapsedMilliseconds);
        }
        catch (MoodleApiException)
        {
            throw;
        }
        catch (Exception exception) when (exception.GetType().Name.Contains("BrokenCircuit", StringComparison.Ordinal))
        {
            throw CreateFailure(
                MoodleErrorContract.NetworkError,
                connection,
                endpoint,
                "The Moodle network circuit is open.",
                innerException: exception,
                auditId: auditId,
                durationMs: stopwatch.ElapsedMilliseconds);
        }
    }

    private MoodleApiException CreateFailure(
        string code,
        MoodleConnectorCredentials connection,
        Uri? endpoint,
        string internalMessage,
        int? httpStatusCode = null,
        Exception? innerException = null,
        string? auditId = null,
        long? durationMs = null,
        string? remoteErrorCode = null)
    {
        var failure = new MoodleApiException(
            code,
            internalMessage,
            httpStatusCode,
            innerException,
            auditId,
            connection.ConnectionId,
            connection.Alias,
            endpoint?.GetLeftPart(UriPartial.Path),
            "login/token.php",
            durationMs,
            remoteErrorCode);
        logger.LogWarning(
            innerException,
            "Moodle token acquisition failed. AuditId={AuditId} ErrorCode={ErrorCode} RemoteErrorCode={RemoteErrorCode} ConnectionId={ConnectionId} Alias={Alias} Endpoint={Endpoint} HttpStatus={HttpStatus} DurationMs={DurationMs}",
            failure.AuditId,
            failure.ErrorCode,
            failure.RemoteErrorCode,
            connection.ConnectionId,
            connection.Alias,
            failure.Endpoint,
            failure.HttpStatusCode,
            failure.DurationMs);
        return failure;
    }

    private static string CreateCredentialFingerprint(MoodleConnectorCredentials connection)
    {
        var source = $"{connection.BaseUrl.Trim().TrimEnd('/')}\u001f{connection.Username}\u001f{connection.Password}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)).AsSpan(0, 12));
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
