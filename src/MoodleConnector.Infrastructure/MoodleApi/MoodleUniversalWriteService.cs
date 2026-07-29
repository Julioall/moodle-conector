using System.Globalization;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Auditing;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Application.PendingActions;
using MoodleConnector.Domain;

namespace MoodleConnector.Infrastructure.MoodleApi;

internal sealed class MoodleUniversalWriteService(
    IMoodleFunctionCatalog catalog,
    IMoodleRestClient restClient,
    IMoodleConnectorCredentialsProvider credentialsProvider,
    IPendingActionService pendingActions,
    IActionConfirmationService confirmations,
    IPendingMoodleActionRepository pendingActionRepository,
    IMoodleAuditLogRepository auditLogs,
    IOptions<MoodleUniversalApiFeatureOptions> features,
    ICurrentUserContext? currentUser = null) : IMoodleUniversalWriteService
{
    private static readonly TimeSpan PendingActionExpiration = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] SensitiveParameterFragments =
    [
        "password", "passwd", "pwd", "token", "secret", "authorization",
        "cookie", "connectionstring", "apikey", "privatekey", "accesskey", "refresh", "clientsecret", "jwt", "bearer"
    ];

    public async Task<MoodleWritePreview> PrepareAsync(
        string functionName,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var parameterNames = parameters.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var connection = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        try
        {
            EnsureEnabled();
            var descriptor = await ResolveControlledWriteAsync(functionName, cancellationToken);
            EnsureNoSensitiveParameters(parameters);
            if (!connection.CanWrite)
            {
                throw new MoodleApiException("write_not_allowed", "A conexao Moodle selecionada nao permite escrita.");
            }

            var parameterHash = CreateParameterHash(parameters);
            var confirmationText = $"CONFIRMAR ESCRITA MOODLE {descriptor.Name.ToUpperInvariant()} {parameterHash[..12].ToUpperInvariant()}";
            var preview = new
            {
                function = descriptor.Name,
                connectionAlias = connection.Alias,
                parameterNames,
                parameterHash,
                risk = descriptor.Risk.ToString(),
                execution = "Nenhuma chamada foi enviada ao Moodle; confirme explicitamente para executar uma unica vez."
            };
            var payload = new UniversalMoodleWritePayload(
                descriptor.Name,
                connection.ConnectionId,
                connection.Alias,
                parameters.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value, JsonOptions), StringComparer.Ordinal),
                parameterHash,
                parameterNames);
            var pending = await pendingActions.CreatePendingActionAsync(
                "moodle_prepare_write",
                ToolRiskLevel.CriticalHumanConfirmedWrite,
                payload,
                preview,
                confirmationText,
                PendingActionExpiration,
                TryGetCourseId(parameters),
                cancellationToken);
            var action = await pendingActionRepository.GetByIdAsync(pending.PendingActionId, cancellationToken)
                ?? throw new InvalidOperationException("A acao pendente universal nao foi encontrada apos a preparacao.");
            var preparedAt = DateTimeOffset.UtcNow;
            await auditLogs.AddAsync(new MoodleAuditLog
            {
                CorrelationId = action.CorrelationId,
                ToolName = "moodle_prepare_write",
                RiskLevel = ToolRiskLevel.CriticalHumanConfirmedWrite,
                ActorSubject = action.CreatedBySubject,
                ActorEmail = action.CreatedByEmail,
                ActorMoodleUserId = action.CreatedByMoodleUserId,
                CourseId = action.CourseId,
                MoodleConnectionId = connection.ConnectionId,
                MoodleConnectionAlias = connection.Alias,
                MoodleFunction = descriptor.Name,
                PendingActionId = action.Id,
                StartedAt = preparedAt,
                FinishedAt = preparedAt,
                DurationMs = 0,
                RequestSanitizedJson = AuditPayloadSanitizer.SerializeSanitized(new
                {
                    connectionAlias = connection.Alias,
                    parameterNames,
                    parameterHash
                }),
                ResponseSummaryJson = JsonSerializer.Serialize(new { pendingActionId = action.Id, action.ExpiresAt }, JsonOptions),
                Status = "write_prepared"
            }, cancellationToken);
            await auditLogs.SaveChangesAsync(cancellationToken);

            return new MoodleWritePreview(
                pending.PendingActionId,
                descriptor.Name,
                parameterNames,
                parameterHash,
                confirmationText,
                pending.ExpiresAt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var errorCode = ex is MoodleApiException moodleError ? moodleError.ErrorCode : ex.GetType().Name;
            await RecordPreparationBlockedAsync(connection, functionName, parameterNames, startedAt, DateTimeOffset.UtcNow, errorCode, cancellationToken);
            await auditLogs.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<MoodleWriteResult> ConfirmAsync(
        Guid pendingActionId,
        string confirmationText,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var action = await pendingActionRepository.GetByIdAsync(pendingActionId, cancellationToken)
            ?? throw new InvalidOperationException("Acao pendente nao encontrada.");
        if (!string.Equals(action.ToolName, "moodle_prepare_write", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A acao pendente nao pertence ao executor universal de escrita.");
        }

        var payload = JsonSerializer.Deserialize<UniversalMoodleWritePayload>(action.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Os dados da acao pendente estao invalidos.");
        var connection = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        if (!connection.CanWrite)
        {
            throw new MoodleApiException("write_not_allowed", "A conexao Moodle selecionada nao permite escrita.");
        }
        if (!string.Equals(connection.ConnectionId, payload.ConnectionId, StringComparison.Ordinal))
        {
            throw new MoodleApiException("wrong_moodle_alias", "A confirmacao deve usar a mesma conexao Moodle utilizada na previa.");
        }

        await ResolveControlledWriteAsync(payload.Function, cancellationToken);
        var values = payload.Parameters.ToDictionary(
            pair => pair.Key,
            pair => (object?)pair.Value.Clone(),
            StringComparer.Ordinal);
        if (!string.Equals(payload.ParameterHash, CreateParameterHash(values), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Os parametros da acao pendente foram alterados e a confirmacao foi invalidada.");
        }

        var confirmation = await confirmations.ConfirmAsync(
            pendingActionId,
            confirmationText,
            requiredScope: "moodle.write",
            cancellationToken);

        if (confirmation.Status == "already_confirmed")
        {
            return new MoodleWriteResult("already_confirmed", action.Id, payload.Function, confirmation.AuditId, 0);
        }

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await restClient.CallAsync(
                connection,
                payload.Function,
                values,
                allowServiceToken: false,
                cancellationToken);
            var responseSize = Encoding.UTF8.GetByteCount(response.GetRawText());
            await RecordExecutionAsync(action, payload, "write_executed", responseSize, startedAt, DateTimeOffset.UtcNow, stopwatch.ElapsedMilliseconds, null, cancellationToken);
            await auditLogs.SaveChangesAsync(cancellationToken);
            return new MoodleWriteResult("executed", action.Id, payload.Function, confirmation.AuditId, responseSize);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var errorCode = ex is MoodleApiException moodleError ? moodleError.ErrorCode : ex.GetType().Name;
            await RecordExecutionAsync(action, payload, "write_failed", 0, startedAt, DateTimeOffset.UtcNow, stopwatch.ElapsedMilliseconds, errorCode, cancellationToken);
            await auditLogs.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private async Task<MoodleFunctionDescriptor> ResolveControlledWriteAsync(string functionName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(functionName))
        {
            throw new ArgumentException("A funcao Moodle e obrigatoria.", nameof(functionName));
        }

        var profile = await catalog.GetCurrentAsync(false, cancellationToken);
        var descriptor = profile.Functions.FirstOrDefault(function =>
            string.Equals(function.Name, functionName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (descriptor is null || !descriptor.IsAvailable)
        {
            throw new MoodleApiException("function_not_available", "A funcao solicitada nao esta habilitada para a conexao Moodle selecionada.");
        }
        if (descriptor.Risk == MoodleFunctionRisk.Destructive)
        {
            throw new MoodleApiException("destructive_function_blocked", "Funcoes Moodle destrutivas permanecem bloqueadas por padrao.");
        }
        if (descriptor.Risk != MoodleFunctionRisk.ControlledWrite)
        {
            throw new MoodleApiException("function_not_write_allowed", "A funcao solicitada nao esta classificada explicitamente como escrita controlada.");
        }

        return descriptor;
    }

    private Task RecordExecutionAsync(
        PendingMoodleAction action,
        UniversalMoodleWritePayload payload,
        string status,
        int responseSize,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        long durationMs,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        return auditLogs.AddAsync(new MoodleAuditLog
        {
            CorrelationId = action.CorrelationId,
            ToolName = "moodle_confirm_write",
            RiskLevel = ToolRiskLevel.CriticalHumanConfirmedWrite,
            ActorSubject = action.CreatedBySubject,
            ActorEmail = action.CreatedByEmail,
            ActorMoodleUserId = action.CreatedByMoodleUserId,
            CourseId = action.CourseId,
            MoodleConnectionId = payload.ConnectionId,
            MoodleConnectionAlias = payload.ConnectionAlias,
            MoodleFunction = payload.Function,
            PendingActionId = action.Id,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            DurationMs = durationMs,
            RequestSanitizedJson = AuditPayloadSanitizer.SerializeSanitized(new
            {
                payload.ConnectionAlias,
                payload.ParameterNames,
                payload.ParameterHash
            }),
            ResponseSummaryJson = JsonSerializer.Serialize(new { responseSize, durationMs }, JsonOptions),
            Status = status,
            ErrorCode = errorCode,
            // A mensagem remota pode refletir valores de parâmetros; o código normalizado é suficiente para auditoria.
            ErrorMessage = null
        }, cancellationToken);
    }

    private Task RecordPreparationBlockedAsync(
        MoodleConnectorCredentials connection,
        string functionName,
        IReadOnlyList<string> parameterNames,
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        string errorCode,
        CancellationToken cancellationToken)
    {
        return auditLogs.AddAsync(new MoodleAuditLog
        {
            CorrelationId = Guid.NewGuid().ToString("N"),
            ToolName = "moodle_prepare_write",
            RiskLevel = ToolRiskLevel.CriticalHumanConfirmedWrite,
            ActorSubject = string.IsNullOrWhiteSpace(currentUser?.Subject) ? "unknown" : currentUser.Subject,
            ActorEmail = currentUser?.Email,
            MoodleConnectionId = connection.ConnectionId,
            MoodleConnectionAlias = connection.Alias,
            MoodleFunction = string.IsNullOrWhiteSpace(functionName) ? null : functionName.Trim(),
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            DurationMs = Math.Max(0, (long)(finishedAt - startedAt).TotalMilliseconds),
            RequestSanitizedJson = AuditPayloadSanitizer.SerializeSanitized(new
            {
                connectionAlias = connection.Alias,
                parameterNames
            }),
            ResponseSummaryJson = "{}",
            Status = "write_prepare_blocked",
            ErrorCode = errorCode
        }, cancellationToken);
    }

    private void EnsureEnabled()
    {
        if (!features.Value.UniversalMoodleWriteEnabled)
        {
            throw new InvalidOperationException("A escrita universal Moodle esta desabilitada. Habilite Features:UniversalMoodleWriteEnabled somente apos revisao administrativa.");
        }
    }

    private static void EnsureNoSensitiveParameters(IReadOnlyDictionary<string, object?> parameters)
    {
        var sensitive = MoodleParameterSerializer.Flatten(parameters).Keys.FirstOrDefault(IsSensitiveParameterName);
        if (sensitive is not null)
        {
            throw new MoodleApiException("sensitive_write_parameter_blocked", "A escrita universal não aceita parâmetros que possam conter credenciais ou segredos técnicos.");
        }
    }

    private static bool IsSensitiveParameterName(string name)
    {
        var components = name.Split(['[', ']'], StringSplitOptions.RemoveEmptyEntries);
        return components.Any(component =>
        {
            var normalized = component.Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();
            return SensitiveParameterFragments.Any(fragment => normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        });
    }

    private static string CreateParameterHash(IReadOnlyDictionary<string, object?> parameters)
    {
        var json = JsonSerializer.Serialize(parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal), JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static long? TryGetCourseId(IReadOnlyDictionary<string, object?> parameters)
    {
        if (!parameters.TryGetValue("courseid", out var courseId) || courseId is null)
        {
            return null;
        }

        return long.TryParse(Convert.ToString(courseId, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;
    }

    private sealed record UniversalMoodleWritePayload(
        string Function,
        string ConnectionId,
        string ConnectionAlias,
        IReadOnlyDictionary<string, JsonElement> Parameters,
        string ParameterHash,
        IReadOnlyList<string> ParameterNames);
}
