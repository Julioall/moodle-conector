using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Application.Tools;
using Microsoft.Extensions.Logging;

namespace MoodleConnector.Presentation.Tools;

[McpServerToolType]
public sealed class MoodleUniversalTools(
    IMoodleFunctionCatalog functionCatalog,
    IMoodleFunctionExecutor functionExecutor,
    IMoodleBusinessFlowRegistry businessFlows,
    IMoodleConnectorCredentialsProvider credentialsProvider,
    IMoodleConnectionSelection connectionSelection,
    IMoodleRestClient restClient,
    ILogger<MoodleUniversalTools> logger)
{
    [McpServerTool(Name = "moodle_diagnose_connection", Title = "Diagnosticar Conexao Moodle",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<MoodleConnectionDiagnostic>))]
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
    [Description("Lista as funcoes Web Service habilitadas para a conexao Moodle atual. Funcoes desconhecidas permanecem classificadas como Unknown e nao podem ser executadas pela tool de leitura.")]
    public async Task<CallToolResult> ListFunctionsAsync(
        [Description("Termo opcional para filtrar o nome da funcao.")] string? search = null,
        [Description("Alias opcional da conexao Moodle.")] string? moodleAlias = null,
        [Description("Ignora o cache e consulta novamente o Moodle.")] bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        connectionSelection.Alias = moodleAlias;
        try
        {
            var profile = await functionCatalog.GetCurrentAsync(forceRefresh, cancellationToken);
            var functions = string.IsNullOrWhiteSpace(search)
                ? profile.Functions
                : profile.Functions.Where(function => function.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            return Success<IReadOnlyList<MoodleFunctionDescriptor>>(functions, $"{functions.Count} funcao(oes) encontrada(s).");
        }
        catch (OperationCanceledException) { throw; }
        catch (MoodleApiException ex) { return ToolResultHelper.Error<IReadOnlyList<MoodleFunctionDescriptor>>(ex); }
        catch (InvalidOperationException ex) { return ToolResultHelper.Error<IReadOnlyList<MoodleFunctionDescriptor>>(ex.Message); }
    }

    [McpServerTool(Name = "moodle_check_function", Title = "Verificar Funcao Moodle",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<MoodleFunctionDescriptor>))]
    [Description("Confirma se uma funcao Moodle esta disponivel para o token atual e informa sua classificacao de risco local.")]
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
            var profile = await functionCatalog.GetCurrentAsync(false, cancellationToken);
            var descriptor = profile.Functions.FirstOrDefault(function =>
                string.Equals(function.Name, functionName.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? new MoodleFunctionDescriptor(functionName.Trim(), MoodleFunctionRisk.Unknown, false);
            return Success(descriptor, descriptor.IsAvailable ? "Funcao Moodle disponivel." : "Funcao Moodle nao esta disponivel para esta conexao.");
        }
        catch (OperationCanceledException) { throw; }
        catch (MoodleApiException ex) { return ToolResultHelper.Error<MoodleFunctionDescriptor>(ex); }
        catch (InvalidOperationException ex) { return ToolResultHelper.Error<MoodleFunctionDescriptor>(ex.Message); }
    }

    [McpServerTool(Name = "moodle_describe_function", Title = "Descrever Funcao Moodle",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<MoodleFunctionDescriptor>))]
    [Description("Retorna a disponibilidade e a classificação de risco local de uma função Web Service Moodle. O Moodle não fornece, por esse endpoint, um schema completo de parâmetros.")]
    public Task<CallToolResult> DescribeFunctionAsync(
        [Description("Nome exato da função Web Service Moodle.")] string functionName,
        [Description("Alias opcional da conexão Moodle.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default) =>
        CheckFunctionAsync(functionName, moodleAlias, cancellationToken);

    [McpServerTool(Name = "moodle_list_available_flows", Title = "Listar Fluxos Moodle Disponiveis",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<IReadOnlyCollection<BusinessFlowAvailability>>))]
    [Description("Avalia os fluxos acadêmicos registrados para a conexão Moodle atual, selecionando a melhor estratégia ou informando as funções ausentes.")]
    public async Task<CallToolResult> ListAvailableFlowsAsync(
        [Description("Alias opcional da conexão Moodle.")] string? moodleAlias = null,
        [Description("Ignora o cache e consulta novamente o Moodle.")] bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        connectionSelection.Alias = moodleAlias;
        try
        {
            var profile = await functionCatalog.GetCurrentAsync(forceRefresh, cancellationToken);
            var flows = businessFlows.EvaluateAll(profile);
            return Success<IReadOnlyCollection<BusinessFlowAvailability>>(flows, $"{flows.Count(flow => flow.IsAvailable)} fluxo(s) Moodle disponível(is).");
        }
        catch (OperationCanceledException) { throw; }
        catch (MoodleApiException ex) { return ToolResultHelper.Error<IReadOnlyCollection<BusinessFlowAvailability>>(ex); }
        catch (InvalidOperationException ex) { return ToolResultHelper.Error<IReadOnlyCollection<BusinessFlowAvailability>>(ex.Message); }
    }

    [McpServerTool(Name = "moodle_execute_read", Title = "Executar Leitura Moodle",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<MoodleFunctionResult>))]
    [Description("Executa uma funcao Moodle explicitamente classificada como leitura, desde que esteja habilitada para o token da conexao atual. Escritas, funcoes destrutivas e funcoes desconhecidas sao recusadas.")]
    public async Task<CallToolResult> ExecuteReadAsync(
        [Description("Nome exato da funcao Web Service Moodle.")] string functionName,
        [Description("Objeto JSON com os parametros da funcao Moodle.")] JsonElement parameters,
        [Description("Alias opcional da conexao Moodle.")] string? moodleAlias = null,
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

        connectionSelection.Alias = moodleAlias;
        try
        {
            var values = parameters.EnumerateObject().ToDictionary(
                property => property.Name,
                property => (object?)property.Value.Clone(),
                StringComparer.Ordinal);
            var data = await functionExecutor.ExecuteReadAsync(functionName, values, cancellationToken);
            return Success(data, $"Funcao de leitura '{data.Function}' executada com sucesso.");
        }
        catch (OperationCanceledException) { throw; }
        catch (MoodleApiException ex) { return ToolResultHelper.Error<MoodleFunctionResult>(ex); }
        catch (ArgumentException ex) { return ToolResultHelper.Error<MoodleFunctionResult>(ex.Message); }
        catch (InvalidOperationException ex) { return ToolResultHelper.Error<MoodleFunctionResult>(ex.Message); }
    }

    private static CallToolResult Success<T>(
        T data,
        string narration,
        string? auditId = null,
        IReadOnlyList<string>? warnings = null)
    {
        var response = new ToolResponse<T>(
            "ok",
            data,
            warnings ?? [],
            AuditId: auditId ?? Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            Message: narration);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = narration }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
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
