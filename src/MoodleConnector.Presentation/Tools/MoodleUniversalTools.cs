using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Auditing;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Application.Registry;
using MoodleConnector.Domain.Registry;
using MoodleConnector.Application.Tools;
using Microsoft.Extensions.Logging;
using MoodleConnector.Presentation.Configuration;
using MoodleConnector.Domain;
using MoodleConnector.Presentation.Security;

namespace MoodleConnector.Presentation.Tools;

[McpServerToolType]
public sealed class MoodleUniversalTools(
    IMoodleFunctionCatalog functionCatalog,
    ISafeReadExecutor safeReadExecutor,
    IOperationRegistry operationRegistry,
    IPolicyEngine policyEngine,
    IMoodleBusinessFlowRegistry businessFlows,
    IMoodleConnectorCredentialsProvider credentialsProvider,
    IMoodleConnectionSelection connectionSelection,
    IMoodleRestClient restClient,
    ILogger<MoodleUniversalTools> logger,
    IMoodleFunctionContractRegistry? contractRegistry = null,
    ICurrentUserContext? currentUser = null,
    IPendingMoodleActionRepository? pendingActions = null,
    MoodleContinuationTokenService? continuationTokens = null)
{
    [McpServerTool(Name = "moodle_diagnose_connection", Title = "Diagnosticar Conexao Moodle",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<MoodleConnectionDiagnostic>))]
    [MoodleToolMetadata(
        Family = "discovery",
        Classification = "R6",
        Kind = "diagnostic",
        CanonicalOperation = "connector.diagnostics.connection",
        Structural = true,
        ExposureStatus = "Diagnostic",
        ExposureReason = "Diagnostico tecnico detalhado para suporte e validacao de conexao; nao e necessario na superficie cognitiva normal.",
        Evidence = "Implementacao MoodleUniversalTools.DiagnoseConnectionAsync; preservada em Full e callable por compatibilidade.")]
    [Description("Verifica a conexao Moodle selecionada e descobre as funcoes Web Service efetivamente habilitadas para o token. Nao expõe tokens ou senhas.")]
    public async Task<CallToolResult> DiagnoseConnectionAsync(
        [Description("Alias opcional da conexao Moodle.")] string? moodleAlias = null,
        [Description("Ignora o cache e consulta novamente o Moodle.")] bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        connectionSelection.Alias = moodleAlias;
        var auditId = Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            EnsureSiteInfoDiscoveryIsAllowed();
            var connection = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
            var liveSiteInfo = await restClient.CallAsync(
                connection,
                "core_webservice_get_site_info",
                new Dictionary<string, object?>(),
                allowServiceToken: false,
                cancellationToken);
            var profile = MoodleFunctionProfileParser.Parse(connection, liveSiteInfo);
            var flows = businessFlows.EvaluateAll(profile);
            var data = new MoodleConnectionDiagnostic(
                Healthy: true,
                RequestedAlias: MoodleConnectionAlias.Normalize(moodleAlias),
                profile.ConnectionAlias,
                profile.ConnectionId,
                BaseUrl: SanitizeBaseUrl(connection.BaseUrl),
                ConnectionFound: true,
                Active: true,
                UrlValid: true,
                CredentialsPresent: true,
                DecryptionSucceeded: true,
                TokenAvailable: true,
                HttpSucceeded: true,
                AuthenticationSucceeded: true,
                SiteInfoSucceeded: true,
                profile.SiteName,
                profile.Release,
                profile.MoodleUserId,
                LatencyMs: stopwatch.ElapsedMilliseconds,
                profile.Functions.Count,
                profile.Functions.Count(function => function.Risk == MoodleFunctionRisk.Read),
                profile.Functions.Count(function => function.Risk == MoodleFunctionRisk.ControlledWrite),
                connection.CanWrite,
                flows,
                profile.DiscoveredAt,
                DiagnosticErrorCode: null,
                DiagnosticMessage: null);
            logger.LogInformation(
                "Moodle diagnostic completed. AuditId={AuditId} ConnectionId={ConnectionId} Alias={Alias} Endpoint={Endpoint} Function={Function} HttpStatus={HttpStatus} DurationMs={DurationMs} SiteInfoUserId={SiteInfoUserId}",
                auditId,
                connection.ConnectionId,
                connection.Alias,
                SanitizeBaseUrl(connection.BaseUrl),
                "core_webservice_get_site_info",
                200,
                stopwatch.ElapsedMilliseconds,
                GetInt64(liveSiteInfo, "userid"));
            return Success(
                data,
                $"Conexao '{profile.ConnectionAlias}' diagnosticada: {profile.Functions.Count} funcoes descobertas.",
                auditId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var descriptor = MoodleErrorContract.Describe(ex);
            var moodleFailure = ex as MoodleApiException;
            var stage = moodleFailure?.Stage ?? MoodleIntegrationStage.Unknown;
            var found = stage > MoodleIntegrationStage.ConnectionLookup ||
                descriptor.ErrorCode == MoodleErrorContract.ConnectionDisabled;
            var active = stage > MoodleIntegrationStage.ConnectionState;
            var urlValid = stage > MoodleIntegrationStage.UrlValidation;
            var credentialsPresent = stage > MoodleIntegrationStage.CredentialPresence;
            var decryptionSucceeded = stage > MoodleIntegrationStage.CredentialDecryption;
            var tokenAvailable = stage > MoodleIntegrationStage.TokenRequest &&
                descriptor.ErrorCode != MoodleErrorContract.AuthenticationFailed;
            var httpSucceeded = moodleFailure?.HttpStatusCode is >= 200 and < 300;
            var data = new MoodleConnectionDiagnostic(
                Healthy: false,
                RequestedAlias: MoodleConnectionAlias.Normalize(moodleAlias),
                Alias: moodleFailure?.ConnectionAlias ?? MoodleConnectionAlias.NormalizeOrDefault(moodleAlias),
                ConnectionId: moodleFailure?.ConnectionId,
                BaseUrl: SanitizeBaseUrl(moodleFailure?.Endpoint),
                ConnectionFound: found,
                Active: active,
                UrlValid: urlValid,
                CredentialsPresent: credentialsPresent,
                DecryptionSucceeded: decryptionSucceeded,
                TokenAvailable: tokenAvailable,
                HttpSucceeded: httpSucceeded,
                AuthenticationSucceeded: tokenAvailable &&
                    descriptor.ErrorCode != MoodleErrorContract.AuthenticationFailed,
                SiteInfoSucceeded: false,
                SiteName: null,
                Release: null,
                MoodleUserId: null,
                LatencyMs: stopwatch.ElapsedMilliseconds,
                FunctionCount: 0,
                ReadFunctionCount: 0,
                ControlledWriteFunctionCount: 0,
                CanWrite: false,
                Flows: [],
                DiscoveredAt: DateTimeOffset.UtcNow,
                DiagnosticErrorCode: descriptor.ErrorCode,
                DiagnosticMessage: descriptor.Message);
            logger.LogWarning(
                ex,
                "Moodle diagnostic failed. AuditId={AuditId} ErrorCode={ErrorCode} ConnectionId={ConnectionId} Alias={Alias} Endpoint={Endpoint} Function={Function} HttpStatus={HttpStatus} DurationMs={DurationMs}",
                descriptor.AuditId,
                descriptor.ErrorCode,
                moodleFailure?.ConnectionId,
                moodleFailure?.ConnectionAlias,
                moodleFailure?.Endpoint,
                moodleFailure?.FunctionName ?? "core_webservice_get_site_info",
                moodleFailure?.HttpStatusCode,
                stopwatch.ElapsedMilliseconds);
            return Success(data, descriptor.Message, descriptor.AuditId, [descriptor.Message]);
        }
    }

    [McpServerTool(Name = "moodle_list_functions", Title = "Listar Funcoes Moodle",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<IReadOnlyList<MoodleFunctionDescriptor>>))]
    [MoodleToolMetadata(
        Family = "discovery",
        Classification = "R6",
        Kind = "diagnostic",
        CanonicalOperation = "connector.diagnostics.functions",
        Structural = true,
        ExposureStatus = "Keep",
        ExposureReason = "Lista as funcoes externas acessiveis ao token para permitir cobertura generica e reportar contratos ausentes sem adivinhar a semantica pelo nome.",
        Evidence = "Implementacao MoodleUniversalTools.ListFunctionsAsync; retorna disponibilidade e estado do contrato quando o manifesto estiver configurado.")]
    [Description("Lista todas as funcoes Web Service habilitadas para o token da conexao Moodle atual. Funcoes de consulta executam diretamente; as demais usam a confirmacao de escrita.")]
    public async Task<CallToolResult> ListFunctionsAsync(
        [Description("Termo opcional para filtrar o nome da funcao.")] string? search = null,
        [Description("Alias opcional da conexao Moodle.")] string? moodleAlias = null,
        [Description("Ignora o cache e consulta novamente o Moodle.")] bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        connectionSelection.Alias = moodleAlias;
        try
        {
            EnsureSiteInfoDiscoveryIsAllowed();
            var profile = await functionCatalog.GetCurrentAsync(forceRefresh, cancellationToken);
            var functions = string.IsNullOrWhiteSpace(search)
                ? profile.Functions
                : profile.Functions.Where(function => function.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            functions = functions
                .Where(function => function.IsAvailable)
                .Select(function => EnrichWithContract(function, profile.Release))
                .ToArray();
            return Success<IReadOnlyList<MoodleFunctionDescriptor>>(
                functions,
                $"{functions.Count} funcao(oes) encontrada(s).",
                freshness: BuildCapabilitiesFreshness(profile, functions.Count, "moodle_functions"));
        }
        catch (OperationCanceledException) { throw; }
        catch (MoodleApiException ex) { return ToolResultHelper.Error<IReadOnlyList<MoodleFunctionDescriptor>>(ex); }
        catch (InvalidOperationException ex) { return ToolResultHelper.Error<IReadOnlyList<MoodleFunctionDescriptor>>(ex.Message); }
    }

    [McpServerTool(Name = "moodle_check_function", Title = "Verificar Funcao Moodle",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<MoodleFunctionDescriptor>))]
    [MoodleToolMetadata(
        Family = "discovery",
        Classification = "R6",
        Kind = "diagnostic",
        CanonicalOperation = "connector.diagnostics.function",
        Structural = true,
        ExposureStatus = "Diagnostic",
        ExposureReason = "Verificacao tecnica de uma funcao remota para suporte; nao representa uma intencao academica distinta.",
        Evidence = "Implementacao MoodleUniversalTools.CheckFunctionAsync; preservada em Full e callable por compatibilidade.")]
    [Description("Confirma se uma funcao Moodle esta disponivel para o token atual e informa se ela executa como consulta ou exige confirmacao de escrita.")]
    public async Task<CallToolResult> CheckFunctionAsync(
        [Description("Nome exato da funcao Web Service Moodle.")] string functionName,
        [Description("Alias opcional da conexao Moodle.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(functionName))
        {
            return ToolResultHelper.Error<MoodleFunctionDescriptor>("Informe o nome da funcao Moodle.");
        }

        connectionSelection.Alias = moodleAlias;
        try
        {
            EnsureSiteInfoDiscoveryIsAllowed();
            var profile = await functionCatalog.GetCurrentAsync(false, cancellationToken);
            var discovered = profile.Functions.FirstOrDefault(function =>
                string.Equals(function.Name, functionName.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? new MoodleFunctionDescriptor(functionName.Trim(), MoodleFunctionRisk.Unknown, false);
            var descriptor = discovered;
            descriptor = EnrichWithContract(descriptor, profile.Release);
            return Success(descriptor, descriptor.IsAvailable ? "Funcao Moodle disponivel." : "Funcao Moodle nao esta disponivel para esta conexao.");
        }
        catch (OperationCanceledException) { throw; }
        catch (MoodleApiException ex) { return ToolResultHelper.Error<MoodleFunctionDescriptor>(ex); }
        catch (InvalidOperationException ex) { return ToolResultHelper.Error<MoodleFunctionDescriptor>(ex.Message); }
    }

    [McpServerTool(Name = "moodle_describe_function", Title = "Descrever Funcao Moodle",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<MoodleFunctionDescription>))]
    [MoodleToolMetadata(
        Family = "discovery",
        Classification = "R6",
        Kind = "capability-discovery",
        CanonicalOperation = "connector.capabilities.function",
        Structural = true,
        ExposureStatus = "Keep",
        ExposureReason = "Explica contrato, efeito e disponibilidade de uma funcao sem confundir conhecimento do manifesto com permissao do token.",
        Evidence = "Implementacao MoodleUniversalTools.DescribeFunctionAsync; schemas somente sao retornados quando o contrato verificado e compativel.")]
    [Description("Descreve uma funcao External Web Service do Moodle: informa se o token atual a oferece, o efeito contratual, versao, origem, hash e schemas verificados. Um contrato conhecido nao libera uma funcao que o token nao oferece.")]
    public async Task<CallToolResult> DescribeFunctionAsync(
        [Description("Nome exato da funcao Web Service Moodle.")] string functionName,
        [Description("Alias opcional da conexao Moodle.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(functionName))
        {
            return ToolResultHelper.Error<MoodleFunctionDescription>("Informe o nome da funcao Moodle.");
        }

        connectionSelection.Alias = moodleAlias;
        try
        {
            EnsureSiteInfoDiscoveryIsAllowed();
            var profile = await functionCatalog.GetCurrentAsync(false, cancellationToken);
            var descriptor = profile.Functions.FirstOrDefault(function =>
                string.Equals(function.Name, functionName.Trim(), StringComparison.OrdinalIgnoreCase));
            var resolution = contractRegistry?.Resolve(
                functionName.Trim(),
                profile.Release,
                descriptor?.ExternalFunctionVersion);
            var contract = resolution is { IsVerified: true } ? resolution.Contract : null;
            var data = new MoodleFunctionDescription(
                functionName.Trim(),
                descriptor?.IsAvailable == true,
                descriptor?.Risk ?? MoodleFunctionRisk.Unknown,
                descriptor?.ExternalFunctionVersion,
                ToContractStatus(resolution?.Status),
                resolution?.ContractHash,
                resolution?.Reasons ?? ["contract_registry_unavailable"],
                contract?.Effect,
                contract?.MoodleVersion,
                contract?.Component,
                contract?.PluginVersion,
                contract?.Source,
                contract?.InputSchema,
                contract?.OutputSchema,
                contract?.Pagination,
                contract?.FileHandling,
                contract?.RequiredScopes,
                contract?.PlatformPermission,
                contract?.AdministrativeOnly == true,
                contract?.MoodleCapabilities);
            var message = data.IsAvailable
                ? "Funcao Moodle descrita para a conexao selecionada."
                : "A funcao nao foi anunciada ao token da conexao selecionada; o contrato, se conhecido, nao concede acesso.";
            return Success(data, message);
        }
        catch (OperationCanceledException) { throw; }
        catch (MoodleApiException ex) { return ToolResultHelper.Error<MoodleFunctionDescription>(ex); }
        catch (InvalidOperationException ex) { return ToolResultHelper.Error<MoodleFunctionDescription>(ex.Message); }
    }

    [McpServerTool(Name = "moodle_list_available_flows", Title = "Listar Fluxos Moodle Disponiveis",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<IReadOnlyCollection<BusinessFlowAvailability>>))]
    [MoodleToolMetadata(
        Family = "discovery",
        Classification = "R6",
        Kind = "capability-discovery",
        CanonicalOperation = "connector.capabilities.flows",
        Structural = true,
        ExposureStatus = "Keep",
        ExposureReason = "Descoberta de fluxos disponiveis orienta clientes sem descoberta dinamica e preserva fallback explicito.",
        Evidence = "Referenciado pelo ADR-0001 e pelas skills de cursos/core; permanece exposto em Production.")]
    [Description("Avalia os fluxos acadêmicos registrados para a conexão Moodle atual, selecionando a melhor estratégia ou informando as funções ausentes.")]
    public async Task<CallToolResult> ListAvailableFlowsAsync(
        [Description("Alias opcional da conexão Moodle.")] string? moodleAlias = null,
        [Description("Ignora o cache e consulta novamente o Moodle.")] bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        connectionSelection.Alias = moodleAlias;
        try
        {
            EnsureSiteInfoDiscoveryIsAllowed();
            var profile = await functionCatalog.GetCurrentAsync(forceRefresh, cancellationToken);
            var flows = businessFlows.EvaluateAll(profile);
            return Success<IReadOnlyCollection<BusinessFlowAvailability>>(
                flows,
                $"{flows.Count(flow => flow.IsAvailable)} fluxo(s) Moodle disponível(is).",
                freshness: BuildCapabilitiesFreshness(profile, flows.Count, "available_flows"));
        }
        catch (OperationCanceledException) { throw; }
        catch (MoodleApiException ex) { return ToolResultHelper.Error<IReadOnlyCollection<BusinessFlowAvailability>>(ex); }
        catch (InvalidOperationException ex) { return ToolResultHelper.Error<IReadOnlyCollection<BusinessFlowAvailability>>(ex.Message); }
    }

    [McpServerTool(Name = "moodle_execute_read", Title = "Executar Leitura Moodle",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<MoodleFunctionResult>))]
    [Description("Executa uma funcao Moodle de consulta habilitada para o token da conexao atual. Qualquer funcao que possa alterar estado, inclusive remocao, e redirecionada para moodle_prepare_write e confirmacao literal.")]
    public async Task<CallToolResult> ExecuteReadAsync(
        [Description("Nome exato da funcao Web Service Moodle.")] string functionName,
        [Description("Objeto JSON com os parametros da funcao Moodle.")] JsonElement parameters,
        [Description("Alias opcional da conexao Moodle.")] string? moodleAlias = null,
        [Description("Token opaco de continuacao retornado em completeness quando existem mais paginas. Ao usa-lo, envie parameters como objeto vazio.")] string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(functionName))
        {
            return ToolResultHelper.Error<MoodleFunctionResult>("Informe o nome da funcao Moodle.");
        }

        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return ToolResultHelper.Error<MoodleFunctionResult>("Os parametros devem ser fornecidos como um objeto JSON.");
        }

        try
        {
            var normalizedFunction = functionName.Trim();
            var effectiveAlias = moodleAlias;
            Dictionary<string, object?> values;
            MoodleContinuationState? continuationState = null;
            if (!string.IsNullOrWhiteSpace(continuationToken))
            {
                if (continuationTokens is null || currentUser is null ||
                    !continuationTokens.TryRead(continuationToken, out continuationState) ||
                    continuationState is null)
                {
                    return ToolResultHelper.Error<MoodleFunctionResult>("Token de continuacao invalido ou expirado.", errorCode: MoodleErrorContract.InvalidContinuationToken);
                }

                if (!string.Equals(continuationState.Subject, currentUser.Subject, StringComparison.Ordinal) ||
                    !string.Equals(continuationState.Function, normalizedFunction, StringComparison.OrdinalIgnoreCase))
                {
                    return ToolResultHelper.Error<MoodleFunctionResult>("Token de continuacao nao pertence ao usuario ou funcao informados.", errorCode: MoodleErrorContract.InvalidContinuationToken);
                }

                if (parameters.EnumerateObject().Any())
                {
                    return ToolResultHelper.Error<MoodleFunctionResult>("Ao continuar uma leitura, parameters deve ser um objeto vazio.", errorCode: MoodleErrorContract.InvalidContinuationParameters);
                }

                if (!string.IsNullOrWhiteSpace(moodleAlias) &&
                    !string.Equals(moodleAlias, continuationState.ConnectionAlias, StringComparison.OrdinalIgnoreCase))
                {
                    return ToolResultHelper.Error<MoodleFunctionResult>("O token de continuacao pertence a outro alias Moodle.", errorCode: MoodleErrorContract.InvalidContinuationToken);
                }

                effectiveAlias = continuationState.ConnectionAlias;
                try
                {
                    values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                        continuationState.ParametersJson)!
                        .ToDictionary(pair => pair.Key, pair => (object?)pair.Value.Clone(), StringComparer.Ordinal);
                }
                catch (Exception exception) when (exception is JsonException or NullReferenceException)
                {
                    return ToolResultHelper.Error<MoodleFunctionResult>("O estado do token de continuacao esta invalido.", errorCode: MoodleErrorContract.InvalidContinuationToken);
                }
            }
            else
            {
                values = parameters.EnumerateObject().ToDictionary(
                    property => property.Name,
                    property => (object?)property.Value.Clone(),
                    StringComparer.Ordinal);
            }

            connectionSelection.Alias = effectiveAlias;
            var profile = await functionCatalog.GetCurrentAsync(false, cancellationToken);
            var discoveredFunction = profile.Functions.FirstOrDefault(function =>
                string.Equals(function.Name, normalizedFunction, StringComparison.OrdinalIgnoreCase));
            var resolution = contractRegistry?.Resolve(
                normalizedFunction,
                profile.Release,
                discoveredFunction?.ExternalFunctionVersion);
            if (continuationState is not null &&
                !string.Equals(continuationState.ContractHash, resolution?.ContractHash, StringComparison.OrdinalIgnoreCase))
            {
                return ToolResultHelper.Error<MoodleFunctionResult>(
                    "O contrato da funcao mudou desde a pagina anterior; inicie uma nova leitura.",
                    errorCode: MoodleErrorContract.ContractChanged);
            }

            var normalized = await safeReadExecutor.ExecuteAsync(
                normalizedFunction,
                values,
                effectiveAlias,
                new NormalizationContext(NormalizationMode.Agent),
                cancellationToken);
            // Universal reads are intentionally open-ended. The normalizer is
            // not a security boundary, so redact the final public payload as
            // well as audit data before it reaches MCP/ChatGPT.
            var payload = AuditPayloadSanitizer.ToSanitizedElement(normalized);
            var contract = resolution is { IsVerified: true } ? resolution.Contract : null;
            var completeness = BuildCompleteness(
                normalizedFunction,
                profile.ConnectionAlias,
                values,
                payload,
                contract,
                continuationState);
            var data = new MoodleFunctionResult(
                normalizedFunction,
                payload,
                Guid.NewGuid().ToString("N"),
                profile.ConnectionAlias,
                contract?.ContractHash,
                completeness.HasMore == true || completeness.Truncated ? "partial" : "executed",
                completeness);
            return Success(data, $"Funcao de leitura '{data.Function}' executada com sucesso.");
        }
        catch (OperationCanceledException) { throw; }
        catch (MoodleApiException ex) { return ToolResultHelper.Error<MoodleFunctionResult>(ex); }
        catch (ArgumentException ex) { return ToolResultHelper.Error<MoodleFunctionResult>(ex.Message); }
        catch (InvalidOperationException ex)
        {
            var errorCode = ex.Message.Contains("not registered", StringComparison.OrdinalIgnoreCase) ||
                            ex.Message.Contains("unknown", StringComparison.OrdinalIgnoreCase)
                ? MoodleErrorContract.UnknownMoodleFunction
                : null;
            return ToolResultHelper.Error<MoodleFunctionResult>(ex.Message, errorCode: errorCode);
        }
    }

    private MoodleResultCompleteness BuildCompleteness(
        string functionName,
        string connectionAlias,
        IReadOnlyDictionary<string, object?> parameters,
        JsonElement payload,
        MoodleFunctionContract? contract,
        MoodleContinuationState? continuationState)
    {
        var pagination = contract?.Pagination;
        var returnedCount = GetReturnedCount(payload);
        var hasMore = pagination?.HasMoreProperty is { Length: > 0 } hasMoreProperty
            ? GetBooleanProperty(payload, hasMoreProperty)
            : GetBooleanProperty(payload, "hasMore");

        if (hasMore is null && pagination?.TotalProperty is { Length: > 0 } totalProperty &&
            GetLongProperty(payload, totalProperty) is { } total && returnedCount is { } count)
        {
            var start = pagination.Mode.Equals("page", StringComparison.OrdinalIgnoreCase)
                ? (Math.Max(1, GetLongParameter(parameters, pagination.PageParameter) ?? 1) - 1) *
                  Math.Max(1, GetLongParameter(parameters, pagination.PageSizeParameter) ?? count)
                : Math.Max(0, GetLongParameter(parameters, pagination.OffsetParameter) ?? 0);
            hasMore = start + count < total;
        }

        var truncated = GetBooleanProperty(payload, "truncated") == true || hasMore == true;
        string? continuationToken = null;
        string? reason = null;
        if (hasMore == true)
        {
            if (pagination is null)
            {
                reason = "response_has_more_without_verified_pagination_contract";
            }
            else if (currentUser is null || continuationTokens is null)
            {
                reason = "continuation_service_unavailable";
            }
            else if (!TryBuildNextParameters(pagination, parameters, payload, returnedCount, out var nextParameters, out reason))
            {
                // The response remains explicitly partial, but no unsafe
                // parameter is invented for a function whose contract is
                // incomplete.
            }
            else
            {
                continuationToken = continuationTokens.Create(
                    currentUser.Subject,
                    connectionAlias,
                    functionName,
                    JsonSerializer.Serialize(nextParameters),
                    contract?.ContractHash);
            }
        }
        else if (pagination is not null && hasMore is null)
        {
            reason = "pagination_completeness_not_proven";
        }

        return new MoodleResultCompleteness(returnedCount, truncated, hasMore, continuationToken, reason);
    }

    private static bool TryBuildNextParameters(
        MoodlePaginationContract pagination,
        IReadOnlyDictionary<string, object?> parameters,
        JsonElement payload,
        int? returnedCount,
        out Dictionary<string, object?> nextParameters,
        out string? reason)
    {
        nextParameters = parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        reason = null;
        var mode = pagination.Mode.Trim().ToLowerInvariant();
        switch (mode)
        {
            case "offset":
                if (string.IsNullOrWhiteSpace(pagination.OffsetParameter) || string.IsNullOrWhiteSpace(pagination.LimitParameter))
                {
                    reason = "pagination_contract_missing_offset_parameters";
                    return false;
                }

                var offset = GetLongParameter(parameters, pagination.OffsetParameter) ?? 0;
                var limit = GetLongParameter(parameters, pagination.LimitParameter) ?? returnedCount ?? 0;
                if (limit <= 0)
                {
                    reason = "pagination_limit_unavailable";
                    return false;
                }

                nextParameters[pagination.OffsetParameter] = JsonSerializer.SerializeToElement(offset + Math.Max(1, returnedCount ?? limit));
                nextParameters[pagination.LimitParameter] = JsonSerializer.SerializeToElement(limit);
                return true;

            case "page":
                if (string.IsNullOrWhiteSpace(pagination.PageParameter) || string.IsNullOrWhiteSpace(pagination.PageSizeParameter))
                {
                    reason = "pagination_contract_missing_page_parameters";
                    return false;
                }

                var page = GetLongParameter(parameters, pagination.PageParameter) ?? 1;
                var pageSize = GetLongParameter(parameters, pagination.PageSizeParameter) ?? returnedCount ?? 0;
                if (pageSize <= 0)
                {
                    reason = "pagination_page_size_unavailable";
                    return false;
                }

                nextParameters[pagination.PageParameter] = JsonSerializer.SerializeToElement(Math.Max(1, page) + 1);
                nextParameters[pagination.PageSizeParameter] = JsonSerializer.SerializeToElement(pageSize);
                return true;

            case "cursor":
                if (string.IsNullOrWhiteSpace(pagination.CursorParameter))
                {
                    reason = "pagination_contract_missing_cursor_parameter";
                    return false;
                }

                var cursorProperty = pagination.NextCursorProperty;
                if (string.IsNullOrWhiteSpace(cursorProperty))
                {
                    reason = "pagination_contract_missing_next_cursor_property";
                    return false;
                }

                if (!TryGetProperty(payload, cursorProperty, out var cursor) || cursor.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(cursor.GetString()))
                {
                    reason = "pagination_next_cursor_unavailable";
                    return false;
                }

                nextParameters[pagination.CursorParameter] = cursor.Clone();
                return true;

            default:
                reason = "pagination_mode_not_supported";
                return false;
        }
    }

    private static int? GetReturnedCount(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Array)
        {
            return payload.GetArrayLength();
        }

        if (GetLongProperty(payload, "returned") is { } returned)
        {
            return returned is >= 0 and <= int.MaxValue ? (int)returned : null;
        }

        if (payload.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in payload.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    return property.Value.GetArrayLength();
                }
            }
        }

        return null;
    }

    private static long? GetLongParameter(IReadOnlyDictionary<string, object?> parameters, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !parameters.TryGetValue(name, out var value))
        {
            return null;
        }

        if (value is JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var number)
                ? number
                : element.ValueKind == JsonValueKind.String && long.TryParse(element.GetString(), out number)
                    ? number
                    : null;
        }

        return value is long longValue ? longValue : value is int intValue ? intValue : null;
    }

    private static long? GetLongProperty(JsonElement root, string name)
    {
        return TryGetProperty(root, name, out var value)
            ? value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
                ? number
                : value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number)
                    ? number
                    : null
            : null;
    }

    private static bool? GetBooleanProperty(JsonElement root, string name)
    {
        return TryGetProperty(root, name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    [McpServerTool(Name = "moodle_get_coverage_report", Title = "Relatorio de Cobertura Moodle",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<MoodleFunctionCoverageReport>))]
    [MoodleToolMetadata(
        Family = "discovery",
        Classification = "R6",
        Kind = "capability-discovery",
        CanonicalOperation = "connector.capabilities.coverage",
        Structural = true,
        ExposureStatus = "Keep",
        ExposureReason = "Relata todas as funcoes anunciadas ao token e separa descoberta, contrato verificado e prontidao de execucao.",
        Evidence = "Implementacao MoodleUniversalTools.GetCoverageReportAsync; pagina o inventario local sem tratar contrato como permissao.")]
    [Description("Gera um relatorio paginado da cobertura das funcoes Web Service anunciadas ao token Moodle. Distingue funcao descoberta, contrato ausente/invalido, leitura pronta, escrita pronta e bloqueios de autorizacao. O relatorio nao executa funcoes remotas.")]
    public async Task<CallToolResult> GetCoverageReportAsync(
        [Description("Termo opcional para filtrar o nome da funcao.")] string? search = null,
        [Description("Pagina iniciando em 1.")] int page = 1,
        [Description("Quantidade de funcoes por pagina, entre 1 e 200.")] int pageSize = 50,
        [Description("Alias opcional da conexao Moodle.")] string? moodleAlias = null,
        [Description("Ignora o cache e consulta novamente o Moodle.")] bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 200)
        {
            return ToolResultHelper.Error<MoodleFunctionCoverageReport>("Pagina deve ser maior que zero e pageSize deve estar entre 1 e 200.");
        }

        connectionSelection.Alias = moodleAlias;
        try
        {
            EnsureSiteInfoDiscoveryIsAllowed();
            var profile = await functionCatalog.GetCurrentAsync(forceRefresh, cancellationToken);
            var functions = profile.Functions
                .Where(function => function.IsAvailable)
                .Where(function => string.IsNullOrWhiteSpace(search) ||
                    function.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(function => BuildCoverageItem(function, profile.Release))
                .OrderBy(function => function.FunctionName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var items = functions.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
            IReadOnlyList<string> warnings = contractRegistry is null
                ? ["contract_registry_unavailable"]
                : functions.Any(function => function.ContractStatus != MoodleContractStatus.Verified)
                    ? ["discovered_functions_include_unverified_contracts"]
                    : [];
            warnings = warnings
                .Append("homologation_not_executed")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var report = new MoodleFunctionCoverageReport(
                profile.ConnectionId,
                profile.ConnectionAlias,
                profile.Release,
                profile.DiscoveredAt,
                DiscoveryComplete: true,
                profile.IsCached,
                functions.Length,
                page,
                pageSize,
                HasMore: page * pageSize < functions.Length,
                functions.GroupBy(item => item.CoverageState, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
                items,
                warnings);
            return Success(report, $"Relatorio de cobertura Moodle gerado para {functions.Length} funcao(oes).", freshness: BuildCapabilitiesFreshness(profile, items.Length, "moodle_function_coverage"));
        }
        catch (OperationCanceledException) { throw; }
        catch (MoodleApiException ex) { return ToolResultHelper.Error<MoodleFunctionCoverageReport>(ex); }
        catch (InvalidOperationException ex) { return ToolResultHelper.Error<MoodleFunctionCoverageReport>(ex.Message); }
    }

    [McpServerTool(Name = "moodle_get_operation_result", Title = "Consultar Resultado Moodle",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<MoodleOperationResult>))]
    [MoodleToolMetadata(
        Family = "discovery",
        Classification = "R6",
        Kind = "operation-result",
        CanonicalOperation = "connector.operations.result",
        Structural = true,
        ExposureStatus = "Keep",
        ExposureReason = "Consulta somente o resultado persistido de uma escrita ou upload universal; nunca repete a chamada remota.",
        Evidence = "Implementacao MoodleUniversalTools.GetOperationResultAsync; valida titularidade ou permissao administrativa e nao executa o Moodle.")]
    [Description("Consulta o resultado persistido de uma escrita ou upload universal Moodle. Esta ferramenta nunca repete a chamada remota; para execution_unknown, reconcilie a acao antes de qualquer nova tentativa.")]
    public async Task<CallToolResult> GetOperationResultAsync(
        [Description("Identificador da acao pendente retornado pela preparacao universal.")] Guid pendingActionId,
        CancellationToken cancellationToken = default)
    {
        if (pendingActions is null || currentUser is null)
        {
            return ToolResultHelper.Error<MoodleOperationResult>("A consulta de resultados universais nao esta disponivel neste contexto.");
        }

        try
        {
            var action = await pendingActions.GetByIdAsync(pendingActionId, cancellationToken);
            if (action is null)
            {
                return ToolResultHelper.Error<MoodleOperationResult>("Acao pendente nao encontrada.");
            }

            if (!string.Equals(action.CreatedBySubject, currentUser.Subject, StringComparison.Ordinal) &&
                !currentUser.HasPlatformPermission("tool.pending_actions.manage"))
            {
                return ToolResultHelper.Error<MoodleOperationResult>(
                    "Apenas o criador da acao ou um administrador Moodle pode consultar seu resultado.",
                    errorCode: MoodleErrorContract.PermissionDenied);
            }

            if (action.ToolName is not ("moodle_prepare_write" or "moodle_prepare_upload"))
            {
                return ToolResultHelper.Error<MoodleOperationResult>("A acao informada nao pertence ao fluxo universal Moodle.");
            }

            var operation = action.ToolName == "moodle_prepare_upload" ? "moodle_upload_draft" : "moodle_call";
            var status = ToOperationStatus(action.Status);
            var warnings = new List<string>();
            JsonElement? payload = null;
            JsonElement? files = null;
            string? function = null;
            string? contractHash = null;

            if (!string.IsNullOrWhiteSpace(action.ResultJson))
            {
                using var document = JsonDocument.Parse(action.ResultJson);
                var root = document.RootElement;
                status = ReadString(root, "status") ?? status;
                function = ReadString(root, "function");
                contractHash = ReadString(root, "contractHash");
                payload = CloneProperty(root, "payload");
                files = CloneProperty(root, "files");
                if (root.TryGetProperty("warnings", out var storedWarnings) &&
                    storedWarnings.ValueKind == JsonValueKind.Array)
                {
                    warnings.AddRange(storedWarnings.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString()!)
                        .Where(item => !string.IsNullOrWhiteSpace(item)));
                }
            }
            else if (action.Status == PendingActionStatus.ExecutionUnknown)
            {
                warnings.Add("execution_unknown_reconciliation_required");
            }
            else
            {
                warnings.Add("operation_result_not_available");
            }

            var result = new MoodleOperationResult(
                action.Id,
                action.ToolName,
                status,
                operation,
                function,
                contractHash,
                payload,
                files,
                action.ResultUpdatedAtUtc,
                action.Status == PendingActionStatus.ExecutionUnknown,
                warnings);
            return Success(result, action.Status == PendingActionStatus.ExecutionUnknown
                ? "A acao possui resultado remoto desconhecido; reconcilie-a sem repetir a chamada."
                : "Resultado persistido da operacao Moodle consultado sem executar nova chamada.",
                warnings: warnings);
        }
        catch (OperationCanceledException) { throw; }
        catch (JsonException) { return ToolResultHelper.Error<MoodleOperationResult>("O resultado persistido da acao esta invalido."); }
        catch (InvalidOperationException ex) { return ToolResultHelper.Error<MoodleOperationResult>(ex.Message); }
    }

    private static CallToolResult Success<T>(
        T data,
        string narration,
        string? auditId = null,
        IReadOnlyList<string>? warnings = null,
        ToolFreshness? freshness = null)
    {
        var response = new ToolResponse<T>(
            "ok",
            data,
            warnings ?? [],
            AuditId: auditId ?? Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            Message: narration,
            Freshness: freshness);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = narration }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private MoodleFunctionDescriptor EnrichWithContract(MoodleFunctionDescriptor descriptor, string? moodleRelease)
    {
        var resolution = contractRegistry?.Resolve(
            descriptor.Name,
            moodleRelease,
            descriptor.ExternalFunctionVersion);
        if (resolution is null)
        {
            return descriptor;
        }

        var status = resolution.Status switch
        {
            MoodleContractResolutionStatus.Verified => MoodleContractStatus.Verified,
            MoodleContractResolutionStatus.Stale => MoodleContractStatus.Stale,
            MoodleContractResolutionStatus.Conflicting => MoodleContractStatus.Conflicting,
            MoodleContractResolutionStatus.Invalid => MoodleContractStatus.Invalid,
            _ => MoodleContractStatus.Missing
        };
        return descriptor with
        {
            ContractStatus = status,
            ContractHash = resolution.ContractHash,
            ContractReasons = resolution.Reasons
        };
    }

    private MoodleFunctionCoverageItem BuildCoverageItem(MoodleFunctionDescriptor descriptor, string? moodleRelease)
    {
        var resolution = contractRegistry?.Resolve(
            descriptor.Name,
            moodleRelease,
            descriptor.ExternalFunctionVersion);
        if (resolution is null)
        {
            return new MoodleFunctionCoverageItem(
                descriptor.Name,
                descriptor.IsAvailable,
                descriptor.Risk,
                descriptor.ExternalFunctionVersion,
                MoodleContractStatus.Missing,
                null,
                "schema_unavailable",
                false,
                null,
                ["contract_registry_unavailable"],
                AdministrativeOnly: false);
        }

        var status = ToContractStatus(resolution.Status);
        if (!resolution.IsVerified)
        {
            var unverifiedState = resolution.Status switch
            {
                MoodleContractResolutionStatus.Stale => "schema_version_mismatch",
                MoodleContractResolutionStatus.Conflicting => "contract_conflict",
                MoodleContractResolutionStatus.Invalid => "schema_invalid",
                _ => "schema_unavailable"
            };
            return new MoodleFunctionCoverageItem(
                descriptor.Name,
                descriptor.IsAvailable,
                descriptor.Risk,
                descriptor.ExternalFunctionVersion,
                status,
                null,
                unverifiedState,
                false,
                resolution.ContractHash,
                resolution.Reasons,
                AdministrativeOnly: false);
        }

        var contract = resolution.Contract!;
        var reasons = new List<string>();
        if (!string.IsNullOrWhiteSpace(contract.PlatformPermission) &&
            (currentUser is null || !currentUser.HasPlatformPermission(contract.PlatformPermission)))
        {
            reasons.Add("platform_permission_denied");
        }
        foreach (var scope in contract.RequiredScopes ?? [])
        {
            if (!string.IsNullOrWhiteSpace(scope) &&
                (currentUser is null || !currentUser.HasScope(scope.Trim())))
            {
                reasons.Add($"scope_required:{scope.Trim()}");
            }
        }

        if (contract.AdministrativeOnly &&
            (string.IsNullOrWhiteSpace(contract.PlatformPermission) ||
             currentUser is null ||
             !currentUser.HasPlatformPermission(contract.PlatformPermission)))
        {
            reasons.Add("administrative_function_restricted");
        }

        var authorized = reasons.Count == 0;
        var state = !authorized
            ? "policy_blocked"
            : contract.Effect == MoodleEffect.Read
                ? "read_ready"
                : contract.Effect == MoodleEffect.Write
                    ? "write_ready"
                    : "policy_blocked";
        if (contract.Effect == MoodleEffect.Unknown)
        {
            reasons.Add("contract_effect_unknown");
        }

        return new MoodleFunctionCoverageItem(
            descriptor.Name,
            descriptor.IsAvailable,
            descriptor.Risk,
            descriptor.ExternalFunctionVersion,
            status,
            contract.Effect,
            state,
            authorized && (contract.Effect is MoodleEffect.Read or MoodleEffect.Write),
            contract.ContractHash,
            reasons,
            AdministrativeOnly: contract.AdministrativeOnly);
    }

    private static MoodleContractStatus ToContractStatus(MoodleContractResolutionStatus? status) =>
        status switch
        {
            MoodleContractResolutionStatus.Verified => MoodleContractStatus.Verified,
            MoodleContractResolutionStatus.Stale => MoodleContractStatus.Stale,
            MoodleContractResolutionStatus.Conflicting => MoodleContractStatus.Conflicting,
            MoodleContractResolutionStatus.Invalid => MoodleContractStatus.Invalid,
            _ => MoodleContractStatus.Missing
        };

    private static ToolFreshness BuildCapabilitiesFreshness(
        MoodleFunctionProfile profile,
        int recordCount,
        string recordType) =>
        new(
            profile.IsCached ? "cache" : "live",
            profile.DiscoveredAt,
            Math.Max(0, (long)(DateTimeOffset.UtcNow - profile.DiscoveredAt).TotalSeconds),
            Stale: false,
            RefreshQueued: false,
            Complete: true,
            RecordCount: recordCount,
            DecisionSafe: true,
            Dataset: "capabilities",
            RecordType: recordType);

    private static string ToOperationStatus(PendingActionStatus status) =>
        status switch
        {
            PendingActionStatus.ExecutionUnknown => "execution_unknown",
            PendingActionStatus.PendingConfirmation => "pending_confirmation",
            _ => status.ToString().ToLowerInvariant()
        };

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static JsonElement? CloneProperty(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind is not JsonValueKind.Null
            ? value.Clone()
            : null;

    private void EnsureSiteInfoDiscoveryIsAllowed()
    {
        var operation = operationRegistry.GetOperation("core_webservice_get_site_info");
        var decision = policyEngine.Evaluate(operation);
        if (decision.Decision != PolicyDecision.Allow)
        {
            throw new InvalidOperationException(
                $"Policy Denied: discovery requires the registered read operation 'core_webservice_get_site_info'. {decision.Reason}");
        }
    }

    private static string? SanitizeBaseUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty,
            UserName = string.Empty,
            Password = string.Empty
        }.Uri.AbsoluteUri.TrimEnd('/');
    }

    private static long? GetInt64(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object &&
        payload.TryGetProperty(name, out var value) &&
        value.TryGetInt64(out var number)
            ? number
            : null;
}

public sealed record MoodleConnectionDiagnostic(
    [property: JsonPropertyName("healthy")] bool Healthy,
    [property: JsonPropertyName("requestedAlias")] string? RequestedAlias,
    [property: JsonPropertyName("alias")] string Alias,
    [property: JsonPropertyName("connectionId")] string? ConnectionId,
    [property: JsonPropertyName("baseUrl")] string? BaseUrl,
    [property: JsonPropertyName("connectionFound")] bool ConnectionFound,
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("urlValid")] bool UrlValid,
    [property: JsonPropertyName("credentialsPresent")] bool CredentialsPresent,
    [property: JsonPropertyName("decryptionSucceeded")] bool DecryptionSucceeded,
    [property: JsonPropertyName("tokenAvailable")] bool TokenAvailable,
    [property: JsonPropertyName("httpSucceeded")] bool HttpSucceeded,
    [property: JsonPropertyName("authenticationSucceeded")] bool AuthenticationSucceeded,
    [property: JsonPropertyName("siteInfoSucceeded")] bool SiteInfoSucceeded,
    [property: JsonPropertyName("siteName")] string? SiteName,
    [property: JsonPropertyName("release")] string? Release,
    [property: JsonPropertyName("moodleUserId")] long? MoodleUserId,
    [property: JsonPropertyName("latencyMs")] long LatencyMs,
    [property: JsonPropertyName("functionCount")] int FunctionCount,
    [property: JsonPropertyName("readFunctionCount")] int ReadFunctionCount,
    [property: JsonPropertyName("controlledWriteFunctionCount")] int ControlledWriteFunctionCount,
    [property: JsonPropertyName("canWrite")] bool CanWrite,
    [property: JsonPropertyName("flows")] IReadOnlyCollection<BusinessFlowAvailability> Flows,
    [property: JsonPropertyName("discoveredAt")] DateTimeOffset DiscoveredAt,
    [property: JsonPropertyName("diagnosticErrorCode")] string? DiagnosticErrorCode,
    [property: JsonPropertyName("diagnosticMessage")] string? DiagnosticMessage);
