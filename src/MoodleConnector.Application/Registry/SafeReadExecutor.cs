using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Domain.Registry;

namespace MoodleConnector.Application.Registry;

public sealed class SafeReadExecutor : ISafeReadExecutor
{
    private readonly IConnectionRegistry _connectionRegistry;
    private readonly IOperationRegistry _operationRegistry;
    private readonly ICapabilityRegistry _capabilityRegistry;
    private readonly IPolicyEngine _policyEngine;
    private readonly IResponseNormalizer _responseNormalizer;
    private readonly IMoodleConnectorCredentialsProvider _credentialsProvider;
    private readonly IMoodleRestClient _restClient;
    private readonly IMoodleFunctionContractRegistry? _contractRegistry;
    private readonly IOptions<MoodleFunctionContractOptions>? _contractOptions;

    public SafeReadExecutor(
        IConnectionRegistry connectionRegistry,
        IOperationRegistry operationRegistry,
        ICapabilityRegistry capabilityRegistry,
        IPolicyEngine policyEngine,
        IResponseNormalizer responseNormalizer,
        IMoodleConnectorCredentialsProvider credentialsProvider,
        IMoodleRestClient restClient,
        IMoodleFunctionContractRegistry? contractRegistry = null,
        IOptions<MoodleFunctionContractOptions>? contractOptions = null,
        ICurrentUserContext? currentUser = null)
    {
        _connectionRegistry = connectionRegistry;
        _operationRegistry = operationRegistry;
        _capabilityRegistry = capabilityRegistry;
        _policyEngine = policyEngine;
        _responseNormalizer = responseNormalizer;
        _credentialsProvider = credentialsProvider;
        _restClient = restClient;
        _contractRegistry = contractRegistry;
        _contractOptions = contractOptions;
        _currentUser = currentUser;
    }

    private readonly ICurrentUserContext? _currentUser;

    public async Task<JsonNode?> ExecuteAsync(
        string operationName, 
        Dictionary<string, object?> parameters, 
        string? moodleAlias = null,
        NormalizationContext? context = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Fetch Operation Schema before touching connection or credentials.
        // Unknown operations must fail closed without turning the connection
        // registry into an oracle for arbitrary function names.
        var operation = _operationRegistry.GetOperation(operationName);

        if (operation is null)
        {
            throw new InvalidOperationException($"Operation '{operationName}' is not registered.");
        }

        // 2. Resolve Connection
        var connectionInfo = await _connectionRegistry.ResolveConnectionAsync(moodleAlias, cancellationToken);
        if (connectionInfo == null)
        {
            throw new InvalidOperationException($"Could not resolve connection for alias '{moodleAlias}'.");
        }

        // 3. Retrieve Moodle Credentials (using internal provider for real execution)
        // Note: In real architecture, connectionInfo might contain the token or reference it.
        var connection = await _credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);

        // 4 & 5. Capability Check and Execution (with bounded retry for invalid cache)
        JsonElement rawElement;
        bool cacheInvalidated = false;
        MoodleFunctionContractResolution? contractResolution = null;
        while (true)
        {
            var capabilitySnapshot = await _capabilityRegistry.GetSnapshotAsync(connectionInfo, connection.Username ?? "", cancellationToken);
            if (!capabilitySnapshot.IsFunctionAvailable(operationName))
            {
                throw new InvalidOperationException($"Capability Denied: The function '{operationName}' is not available for this connection.");
            }

            contractResolution = _contractRegistry?.Resolve(
                operationName,
                capabilitySnapshot.MoodleRelease,
                capabilitySnapshot.GetFunctionVersion(operationName));
            EnsureContractAllowsRead(operationName, parameters, contractResolution);
            operation = _operationRegistry.GetOperation(
                    operationName,
                    capabilitySnapshot.MoodleRelease,
                    capabilitySnapshot.GetFunctionVersion(operationName))
                ?? throw new InvalidOperationException($"Operation '{operationName}' is not registered.");

            // 6. Evaluate policy only after contract effect and version are
            // known; a verified contract may turn a name-based write fallback
            // into a safe read, or the reverse.
            var policyResult = _policyEngine.Evaluate(operation);
            if (policyResult.Decision == PolicyDecision.Deny)
            {
                throw new InvalidOperationException($"Policy Denied: {policyResult.Reason}");
            }

            if (policyResult.Decision == PolicyDecision.RedirectToControlledWrite)
            {
                throw new InvalidOperationException($"Policy Redirect: {policyResult.Reason}");
            }

            try
            {
                // Generic reads must use the selected user's Moodle token.
                // The service token is reserved for connector-owned discovery
                // and must never widen a user-selected generic operation.
                rawElement = await _restClient.CallAsync(connection, operationName, parameters, allowServiceToken: false, cancellationToken);
                break; // Success
            }
            catch (Application.MoodleApi.MoodleApiException ex) when (ex.RemoteErrorCode == "webservice_access_exception" || ex.ErrorCode == "webservice_access_exception" || ex.RemoteErrorCode == "accessdenied")
            {
                if (cacheInvalidated)
                {
                    // Already retried once, throw
                    throw new InvalidOperationException($"Capability Denied: Moodle rejected '{operationName}' even after cache refresh.", ex);
                }

                cacheInvalidated = true;
                _capabilityRegistry.Invalidate(connectionInfo, connection.Username ?? "");
            }
        }
        
        // Convert JsonElement to JsonNode for the normalizer
        var rawNode = JsonNode.Parse(rawElement.GetRawText());

        if (contractResolution is { IsVerified: true } &&
            MoodleFunctionContractSchemaValidator.ValidateOutput(contractResolution.Contract!, rawElement) is { Count: > 0 } outputErrors)
        {
            throw new MoodleApiException(
                MoodleErrorContract.SchemaValidationFailed,
                $"A resposta de '{operationName}' nao corresponde ao contrato verificado: {string.Join("; ", outputErrors)}",
                functionName: operationName,
                stage: MoodleIntegrationStage.ResponseParsing);
        }

        // 7. Normalization
        if (!string.IsNullOrEmpty(operation.NormalizationProfile))
        {
            var normalizedResponse = _responseNormalizer.Normalize(operation.NormalizationProfile, rawNode, context);
            return normalizedResponse;
        }

        return rawNode;
    }

    private void EnsureContractAllowsRead(
        string operationName,
        IReadOnlyDictionary<string, object?> parameters,
        MoodleFunctionContractResolution? resolution)
    {
        if (resolution is null)
        {
            if (_contractOptions?.Value.RequireVerifiedContracts == true)
            {
                throw new MoodleApiException(
                    MoodleErrorContract.SchemaUnavailable,
                    $"A leitura de '{operationName}' foi bloqueada: o registro de contratos verificados não está disponível.",
                    functionName: operationName);
            }

            return;
        }

        if (!resolution.IsVerified)
        {
            if (_contractOptions?.Value.RequireVerifiedContracts == true)
            {
                throw new MoodleApiException(
                    resolution.Status == MoodleContractResolutionStatus.Stale
                        ? MoodleErrorContract.SchemaVersionMismatch
                        : resolution.Status == MoodleContractResolutionStatus.Conflicting
                            ? MoodleErrorContract.ContractConflict
                            : MoodleErrorContract.SchemaUnavailable,
                    $"A leitura de '{operationName}' foi bloqueada: {string.Join(", ", resolution.Reasons)}",
                    functionName: operationName);
            }

            return;
        }

        var contract = resolution.Contract!;
        EnsureContractAuthorization(operationName, contract);
        if (contract.Effect != MoodleEffect.Read)
        {
            throw new MoodleApiException(
                MoodleErrorContract.FunctionNotAllowed,
                $"A funcao '{operationName}' possui efeito '{contract.Effect}' e nao pode usar o caminho de leitura.",
                functionName: operationName);
        }

        var inputErrors = MoodleFunctionContractSchemaValidator.ValidateInput(contract, parameters);
        if (inputErrors.Count > 0)
        {
            throw new MoodleApiException(
                MoodleErrorContract.SchemaValidationFailed,
                $"Os parametros de '{operationName}' nao correspondem ao contrato verificado: {string.Join("; ", inputErrors)}",
                functionName: operationName);
        }
    }

    private void EnsureContractAuthorization(string operationName, MoodleFunctionContract contract)
    {
        if (contract.AdministrativeOnly &&
            (string.IsNullOrWhiteSpace(contract.PlatformPermission) ||
             _currentUser is null ||
             !_currentUser.HasPlatformPermission(contract.PlatformPermission)))
        {
            throw new MoodleApiException(
                MoodleErrorContract.PermissionDenied,
                $"A funcao '{operationName}' e administrativa e esta bloqueada para o usuario atual.",
                functionName: operationName);
        }

        if (!string.IsNullOrWhiteSpace(contract.PlatformPermission) &&
            (_currentUser is null || !_currentUser.HasPlatformPermission(contract.PlatformPermission)))
        {
            throw new MoodleApiException(
                MoodleErrorContract.PermissionDenied,
                $"A funcao '{operationName}' exige a permissao de plataforma '{contract.PlatformPermission}'.",
                functionName: operationName);
        }

        foreach (var scope in contract.RequiredScopes ?? [])
        {
            if (!string.IsNullOrWhiteSpace(scope) &&
                (_currentUser is null || !_currentUser.HasScope(scope.Trim())))
            {
                throw new MoodleApiException(
                    MoodleErrorContract.PermissionDenied,
                    $"A funcao '{operationName}' exige o escopo '{scope.Trim()}'.",
                    functionName: operationName);
            }
        }
    }
}
