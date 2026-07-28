using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Auditing;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Domain;
using System.Diagnostics;
using System.Text.Json;

namespace MoodleConnector.Infrastructure.MoodleApi;

internal sealed class MoodleFunctionExecutor(
    IMoodleFunctionCatalog catalog,
    IMoodleRestClient restClient,
    IMoodleConnectorCredentialsProvider credentialsProvider,
    IMoodleAuditLogRepository? auditLogs = null,
    ICurrentUserContext? currentUser = null) : IMoodleFunctionExecutor
{
    public async Task<MoodleFunctionResult> ExecuteReadAsync(
        string functionName,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(functionName))
        {
            throw new ArgumentException("A funcao Moodle e obrigatoria.", nameof(functionName));
        }

        var normalizedName = functionName.Trim();
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        MoodleConnectorCredentials? connection = null;
        try
        {
            connection = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
            var profile = await catalog.GetCurrentAsync(false, cancellationToken);
            var descriptor = profile.Functions.FirstOrDefault(function =>
                string.Equals(function.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
            if (descriptor is null || !descriptor.IsAvailable)
            {
                throw new MoodleApiException("function_not_available", "A funcao solicitada nao esta habilitada para a conexao Moodle selecionada.");
            }

            if (descriptor.Risk != MoodleFunctionRisk.Read)
            {
                throw new MoodleApiException(
                    descriptor.Risk == MoodleFunctionRisk.Destructive ? "destructive_function_blocked" : "function_not_read_safe",
                    "A funcao solicitada nao esta classificada explicitamente como leitura segura.");
            }

            var payload = await restClient.CallAsync(
                connection,
                descriptor.Name,
                parameters,
                allowServiceToken: true,
                cancellationToken);
            await RecordAuditAsync(
                connection,
                descriptor.Name,
                parameters,
                "read_executed",
                payload.GetRawText().Length,
                startedAt,
                DateTimeOffset.UtcNow,
                stopwatch.ElapsedMilliseconds,
                null,
                null,
                cancellationToken);
            return new MoodleFunctionResult(descriptor.Name, payload);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (ex is MoodleApiException { ErrorCode: "function_not_available" })
            {
                // Moodle can revoke a function while the connector remains online.
                // Refresh the per-connection catalog before the next request.
                try
                {
                    await catalog.GetCurrentAsync(true, cancellationToken);
                }
                catch (Exception refreshException) when (refreshException is not OperationCanceledException)
                {
                    // Preserve the original operation error. The refresh is best effort
                    // and must not hide the function that actually failed.
                }
            }

            if (connection is not null)
            {
                try
                {
                    await RecordAuditAsync(
                        connection,
                        normalizedName,
                        parameters,
                        "read_failed",
                        0,
                        startedAt,
                        DateTimeOffset.UtcNow,
                        stopwatch.ElapsedMilliseconds,
                        ex is MoodleApiException moodleError
                            ? MoodleErrorContract.NormalizeCode(moodleError.ErrorCode)
                            : MoodleErrorContract.Unexpected,
                        (ex as MoodleApiException)?.AuditId,
                        cancellationToken);
                }
                catch (Exception auditException) when (auditException is not OperationCanceledException)
                {
                    // The original Moodle failure is authoritative. Audit persistence
                    // is best effort and must never replace the integration error.
                }
            }
            throw;
        }
    }

    private async Task RecordAuditAsync(
        MoodleConnectorCredentials connection,
        string functionName,
        IReadOnlyDictionary<string, object?> parameters,
        string status,
        int responseSize,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        long durationMs,
        string? errorCode,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        if (auditLogs is null)
        {
            return;
        }

        await auditLogs.AddAsync(new MoodleAuditLog
        {
            CorrelationId = string.IsNullOrWhiteSpace(correlationId)
                ? Guid.NewGuid().ToString("N")
                : correlationId,
            ToolName = "moodle_execute_read",
            RiskLevel = ToolRiskLevel.ReadOnly,
            ActorSubject = string.IsNullOrWhiteSpace(currentUser?.Subject) ? "unknown" : currentUser.Subject,
            ActorEmail = currentUser?.Email,
            MoodleConnectionId = connection.ConnectionId,
            MoodleConnectionAlias = connection.Alias,
            MoodleFunction = functionName,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            DurationMs = durationMs,
            RequestSanitizedJson = AuditPayloadSanitizer.SerializeSanitized(new
            {
                connectionAlias = connection.Alias,
                parameterNames = parameters.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray()
            }),
            ResponseSummaryJson = JsonSerializer.Serialize(new { responseSize, durationMs }),
            Status = status,
            ErrorCode = errorCode
        }, cancellationToken);
        await auditLogs.SaveChangesAsync(cancellationToken);
    }
}
