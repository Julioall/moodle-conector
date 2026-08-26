using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;

namespace MoodleConnector.Infrastructure.MoodleApi;

internal sealed class MoodleRestClient(
    HttpClient httpClient,
    IMoodleAccessTokenProvider tokenProvider,
    ILogger<MoodleRestClient> logger,
    IMoodleCallTelemetry? telemetry = null) : IMoodleRestClient
{
    public Task<JsonElement> CallAsync(
        MoodleConnectorCredentials connection,
        string functionName,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken) =>
        CallCoreAsync(connection, functionName, parameters, allowServiceToken: true, allowEmptyResponse: false, cancellationToken);

    public async Task<JsonElement> CallAsync(
        MoodleConnectorCredentials connection,
        string functionName,
        IReadOnlyDictionary<string, object?> parameters,
        bool allowServiceToken,
        CancellationToken cancellationToken)
        => await CallCoreAsync(connection, functionName, parameters, allowServiceToken, allowEmptyResponse: false, cancellationToken);

    public Task<JsonElement> CallWriteAsync(
        MoodleConnectorCredentials connection,
        string functionName,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken) =>
        CallCoreAsync(connection, functionName, parameters, allowServiceToken: false, allowEmptyResponse: true, cancellationToken);

    private async Task<JsonElement> CallCoreAsync(
        MoodleConnectorCredentials connection,
        string functionName,
        IReadOnlyDictionary<string, object?> parameters,
        bool allowServiceToken,
        bool allowEmptyResponse,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(functionName))
        {
            throw new ArgumentException("A funcao Moodle e obrigatoria.", nameof(functionName));
        }

        var normalizedFunction = functionName.Trim();
        telemetry?.RecordMoodleWebServiceCall(connection.Alias, normalizedFunction);
        _ = allowServiceToken; // Tenant-scoped reads always use the selected connection credentials.
        var endpoint = BuildEndpoint(connection);
        var auditId = Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var token = await ResolveTokenAsync(connection, allowServiceToken, cancellationToken);
            var values = new Dictionary<string, string>(MoodleParameterSerializer.Flatten(parameters), StringComparer.Ordinal)
            {
                ["wstoken"] = token,
                ["wsfunction"] = normalizedFunction,
                ["moodlewsrestformat"] = "json"
            };

            using var content = new FormUrlEncodedContent(values);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };
            if (allowEmptyResponse)
            {
                request.Options.Set(MoodleHttpRequestOptions.DisableAutomaticRetry, true);
            }

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (payload.TrimStart().StartsWith('{'))
                {
                    try
                    {
                        _ = MoodleResponseParser.Parse(payload);
                    }
                    catch (MoodleApiException remoteFailure)
                    {
                        if (MoodleErrorContract.NormalizeCode(remoteFailure.ErrorCode) ==
                            MoodleErrorContract.AuthenticationFailed)
                        {
                            tokenProvider.Invalidate(connection);
                        }

                        throw CreateFailure(
                            remoteFailure.ErrorCode,
                            connection,
                            endpoint,
                            normalizedFunction,
                            "Moodle returned a structured Web Service error.",
                            (int)response.StatusCode,
                            remoteFailure,
                            auditId,
                            stopwatch.ElapsedMilliseconds,
                            remoteFailure.RemoteErrorCode ?? remoteFailure.ErrorCode);
                    }
                }

                var code = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => MoodleErrorContract.AuthenticationFailed,
                    HttpStatusCode.Forbidden => MoodleErrorContract.PermissionDenied,
                    _ when (int)response.StatusCode >= 500 => MoodleErrorContract.NetworkError,
                    _ => MoodleErrorContract.ApiError
                };
                if (code == MoodleErrorContract.AuthenticationFailed)
                {
                    tokenProvider.Invalidate(connection);
                }

                throw CreateFailure(
                    code,
                    connection,
                    endpoint,
                    normalizedFunction,
                    "Moodle returned an unsuccessful HTTP response.",
                    (int)response.StatusCode,
                    auditId: auditId,
                    durationMs: stopwatch.ElapsedMilliseconds);
            }

            JsonElement parsed;
            try
            {
                parsed = MoodleResponseParser.Parse(payload, allowEmptyResponse);
            }
            catch (MoodleApiException remoteFailure)
            {
                if (MoodleErrorContract.NormalizeCode(remoteFailure.ErrorCode) ==
                    MoodleErrorContract.AuthenticationFailed)
                {
                    tokenProvider.Invalidate(connection);
                }

                var message = IsSafeRemoteErrorCode(remoteFailure.RemoteErrorCode)
                    ? $"Moodle rejected the Web Service request (error code: {remoteFailure.RemoteErrorCode})."
                    : MoodleErrorContract.NormalizeCode(remoteFailure.ErrorCode) == MoodleErrorContract.InvalidResponse
                        ? "Moodle returned an invalid response body."
                        : "Moodle returned a structured Web Service error.";
                throw CreateFailure(
                    remoteFailure.ErrorCode,
                    connection,
                    endpoint,
                    normalizedFunction,
                    message,
                    (int)response.StatusCode,
                    remoteFailure,
                    auditId,
                    stopwatch.ElapsedMilliseconds,
                    remoteFailure.RemoteErrorCode ?? remoteFailure.ErrorCode);
            }

            logger.LogInformation(
                "Moodle {OperationKind} completed. AuditId={AuditId} ConnectionId={ConnectionId} Alias={Alias} Endpoint={Endpoint} Function={Function} HttpStatus={HttpStatus} DurationMs={DurationMs}",
                allowEmptyResponse ? "write" : "read",
                auditId,
                connection.ConnectionId,
                connection.Alias,
                endpoint.GetLeftPart(UriPartial.Path),
                normalizedFunction,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);
            telemetry?.RecordMoodleWebServiceCompleted(connection.Alias, normalizedFunction, stopwatch.Elapsed.TotalMilliseconds);
            return parsed;
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
                normalizedFunction,
                "The Moodle Web Service request timed out.",
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
                normalizedFunction,
                "The Moodle Web Service request failed at the network layer.",
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
                normalizedFunction,
                "The Moodle network circuit is open.",
                innerException: exception,
                auditId: auditId,
                durationMs: stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception)
        {
            throw CreateFailure(
                MoodleErrorContract.Unexpected,
                connection,
                endpoint,
                normalizedFunction,
                "An unexpected error occurred while calling Moodle.",
                innerException: exception,
                auditId: auditId,
                durationMs: stopwatch.ElapsedMilliseconds);
        }
    }

    private static Uri BuildEndpoint(MoodleConnectorCredentials connection)
    {
        if (!Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var baseUri) ||
            !IsAllowedEndpoint(baseUri) ||
            string.IsNullOrWhiteSpace(baseUri.Host) ||
            !string.IsNullOrEmpty(baseUri.UserInfo))
        {
            throw new MoodleApiException(
                MoodleErrorContract.NetworkError,
                "The selected Moodle connection has an invalid URL.",
                connectionId: connection.ConnectionId,
                connectionAlias: connection.Alias,
                stage: MoodleIntegrationStage.UrlValidation);
        }

        return new Uri(baseUri.ToString().TrimEnd('/') + "/webservice/rest/server.php");
    }

    private Task<string> ResolveTokenAsync(
        MoodleConnectorCredentials connection,
        bool allowServiceToken,
        CancellationToken cancellationToken)
    {
        _ = allowServiceToken;
        return tokenProvider.GetAccessTokenAsync(connection, cancellationToken);
    }

    private MoodleApiException CreateFailure(
        string code,
        MoodleConnectorCredentials connection,
        Uri endpoint,
        string functionName,
        string internalMessage,
        int? httpStatusCode = null,
        Exception? innerException = null,
        string? auditId = null,
        long? durationMs = null,
        string? remoteErrorCode = null)
    {
        var stableCode = MoodleErrorContract.NormalizeCode(code);
        var failure = new MoodleApiException(
            stableCode,
            internalMessage,
            httpStatusCode,
            innerException,
            auditId,
            connection.ConnectionId,
            connection.Alias,
            endpoint.GetLeftPart(UriPartial.Path),
            functionName,
            durationMs,
            remoteErrorCode ?? (string.Equals(stableCode, code, StringComparison.Ordinal) ? null : code),
            stableCode == MoodleErrorContract.InvalidResponse
                ? MoodleIntegrationStage.ResponseParsing
                : MoodleIntegrationStage.MoodleRequest);
        telemetry?.RecordMoodleWebServiceFailure(
            connection.Alias,
            functionName,
            failure.ErrorCode,
            failure.DurationMs ?? 0);
        logger.LogWarning(
            innerException,
            "Moodle read failed. AuditId={AuditId} ErrorCode={ErrorCode} RemoteErrorCode={RemoteErrorCode} ConnectionId={ConnectionId} Alias={Alias} Endpoint={Endpoint} Function={Function} HttpStatus={HttpStatus} DurationMs={DurationMs}",
            failure.AuditId,
            failure.ErrorCode,
            failure.RemoteErrorCode,
            connection.ConnectionId,
            connection.Alias,
            failure.Endpoint,
            functionName,
            failure.HttpStatusCode,
            failure.DurationMs);
        return failure;
    }

    private static bool IsAllowedEndpoint(Uri endpoint)
    {
        if (endpoint.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        return endpoint.Scheme == Uri.UriSchemeHttp &&
               (string.Equals(endpoint.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                IPAddress.TryParse(endpoint.Host, out var address) && IPAddress.IsLoopback(address));
    }

    private static bool IsSafeRemoteErrorCode(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 128 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
}
