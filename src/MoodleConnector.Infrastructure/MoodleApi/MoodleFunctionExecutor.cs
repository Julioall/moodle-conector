using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Auditing;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Domain;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json;

namespace MoodleConnector.Infrastructure.MoodleApi;

internal sealed class MoodleFunctionExecutor(
    IMoodleFunctionCatalog catalog,
    IMoodleRestClient restClient,
    IMoodleConnectorCredentialsProvider credentialsProvider,
    IMoodleAuditLogRepository? auditLogs = null,
    ICurrentUserContext? currentUser = null,
    IMoodleFunctionContractRegistry? contractRegistry = null,
    IOptions<MoodleFunctionContractOptions>? contractOptions = null) : IMoodleFunctionExecutor
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
        var executionParameters = new Dictionary<string, object?>(parameters, StringComparer.Ordinal);
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

            if (string.Equals(descriptor.Name, "core_enrol_get_users_courses", StringComparison.OrdinalIgnoreCase)
                && !executionParameters.ContainsKey("userid")
                && profile.MoodleUserId is { } profileUserId)
            {
                executionParameters["userid"] = profileUserId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            var contractResolution = contractRegistry?.Resolve(
                normalizedName,
                profile.Release,
                descriptor.ExternalFunctionVersion);
            if (contractResolution is null && contractOptions?.Value.RequireVerifiedContracts == true)
            {
                throw new MoodleApiException(
                    MoodleErrorContract.SchemaUnavailable,
                    $"A leitura de '{normalizedName}' foi bloqueada: o registro de contratos verificados não está disponível.",
                    functionName: normalizedName);
            }

            if (contractResolution is not null && !contractResolution.IsVerified)
            {
                if (contractOptions?.Value.RequireVerifiedContracts == true)
                {
                    throw new MoodleApiException(
                        contractResolution.Status == MoodleContractResolutionStatus.Stale
                            ? MoodleErrorContract.SchemaVersionMismatch
                            : contractResolution.Status == MoodleContractResolutionStatus.Conflicting
                                ? MoodleErrorContract.ContractConflict
                                : MoodleErrorContract.SchemaUnavailable,
                        $"A leitura de '{normalizedName}' foi bloqueada: {string.Join(", ", contractResolution.Reasons)}",
                        functionName: normalizedName);
                }
            }

            if (contractResolution is { IsVerified: true })
            {
                EnsureContractAuthorization(normalizedName, contractResolution.Contract!);
                if (contractResolution.Contract!.Effect != MoodleEffect.Read)
                {
                    throw new MoodleApiException(
                        MoodleErrorContract.FunctionNotAllowed,
                        $"A funcao '{normalizedName}' possui efeito '{contractResolution.Contract.Effect}' e nao pode usar o caminho de leitura.",
                        functionName: normalizedName);
                }

                var inputErrors = MoodleFunctionContractSchemaValidator.ValidateInput(
                    contractResolution.Contract,
                    executionParameters);
                if (inputErrors.Count > 0)
                {
                    throw new MoodleApiException(
                        MoodleErrorContract.SchemaValidationFailed,
                        $"Os parametros de '{normalizedName}' nao correspondem ao contrato verificado: {string.Join("; ", inputErrors)}",
                        functionName: normalizedName);
                }
            }
            else if (descriptor.Risk != MoodleFunctionRisk.Read)
            {
                throw new MoodleApiException(
                    descriptor.Risk == MoodleFunctionRisk.Destructive ? "destructive_function_blocked" : "function_not_read_safe",
                    "A funcao solicitada nao esta classificada explicitamente como leitura segura.");
            }

            var payload = await restClient.CallAsync(
                connection,
                descriptor.Name,
                executionParameters,
                allowServiceToken: false,
                cancellationToken);

            if (contractResolution is { IsVerified: true } &&
                MoodleFunctionContractSchemaValidator.ValidateOutput(contractResolution.Contract!, payload) is { Count: > 0 } outputErrors)
            {
                throw new MoodleApiException(
                    MoodleErrorContract.SchemaValidationFailed,
                    $"A resposta de '{normalizedName}' nao corresponde ao contrato verificado: {string.Join("; ", outputErrors)}",
                    functionName: normalizedName,
                    stage: MoodleIntegrationStage.ResponseParsing);
            }

            await RecordAuditAsync(
                connection,
                descriptor.Name,
                executionParameters,
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
                        executionParameters,
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

    private void EnsureContractAuthorization(string functionName, MoodleFunctionContract contract)
    {
        if (contract.AdministrativeOnly &&
            (string.IsNullOrWhiteSpace(contract.PlatformPermission) ||
             currentUser is null ||
             !currentUser.HasPlatformPermission(contract.PlatformPermission)))
        {
            throw new MoodleApiException(
                MoodleErrorContract.PermissionDenied,
                $"A funcao '{functionName}' e administrativa e esta bloqueada para o usuario atual.",
                functionName: functionName);
        }

        if (!string.IsNullOrWhiteSpace(contract.PlatformPermission) &&
            (currentUser is null || !currentUser.HasPlatformPermission(contract.PlatformPermission)))
        {
            throw new MoodleApiException(
                MoodleErrorContract.PermissionDenied,
                $"A funcao '{functionName}' exige a permissao de plataforma '{contract.PlatformPermission}'.",
                functionName: functionName);
        }

        foreach (var scope in contract.RequiredScopes ?? [])
        {
            if (!string.IsNullOrWhiteSpace(scope) &&
                (currentUser is null || !currentUser.HasScope(scope.Trim())))
            {
                throw new MoodleApiException(
                    MoodleErrorContract.PermissionDenied,
                    $"A funcao '{functionName}' exige o escopo '{scope.Trim()}'.",
                    functionName: functionName);
            }
        }
    }
}
