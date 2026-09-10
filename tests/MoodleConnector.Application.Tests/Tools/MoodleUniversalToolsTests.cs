using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Application.Registry;
using MoodleConnector.Domain.Registry;
using MoodleConnector.Domain;
using MoodleConnector.Infrastructure;
using MoodleConnector.Presentation.Tools;
using MoodleConnector.Presentation.Security;

namespace MoodleConnector.Application.Tests.Tools;

public sealed class MoodleUniversalToolsTests
{
    [Fact]
    public async Task DiagnoseConnectionAsync_ExecutaSiteInfoAoVivoSemServiceToken()
    {
        var rest = new FakeRestClient(SiteInfo());
        var sut = CreateSut(new FakeCredentialsProvider(Connection()), rest);

        var result = await sut.DiagnoseConnectionAsync("Goiás", forceRefresh: true);

        Assert.False(result.IsError);
        Assert.False(rest.LastAllowServiceToken);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("ok", structured.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(structured.GetProperty("auditId").GetString()));
        var data = structured.GetProperty("data");
        Assert.True(data.GetProperty("healthy").GetBoolean());
        Assert.Equal("goias", data.GetProperty("requestedAlias").GetString());
        Assert.Equal("https://ead.fieg.com.br", data.GetProperty("baseUrl").GetString());
        Assert.True(data.GetProperty("siteInfoSucceeded").GetBoolean());
        Assert.Equal("5.0.1", data.GetProperty("release").GetString());
        Assert.False(data.TryGetProperty("token", out _));
        Assert.DoesNotContain("password", structured.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiagnoseConnectionAsync_RelataCheckpointDeDescriptografiaSemLancar()
    {
        var failure = new MoodleApiException(
            MoodleErrorContract.TokenDecryptionFailed,
            "internal",
            connectionId: "goias-connection",
            connectionAlias: "goias",
            endpoint: "https://ead.fieg.com.br",
            stage: MoodleIntegrationStage.CredentialDecryption);
        var sut = CreateSut(new FakeCredentialsProvider(error: failure), new FakeRestClient(SiteInfo()));

        var result = await sut.DiagnoseConnectionAsync("goias");

        Assert.False(result.IsError);
        var data = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("data");
        Assert.False(data.GetProperty("healthy").GetBoolean());
        Assert.True(data.GetProperty("connectionFound").GetBoolean());
        Assert.True(data.GetProperty("active").GetBoolean());
        Assert.True(data.GetProperty("urlValid").GetBoolean());
        Assert.True(data.GetProperty("credentialsPresent").GetBoolean());
        Assert.False(data.GetProperty("decryptionSucceeded").GetBoolean());
        Assert.False(data.GetProperty("tokenAvailable").GetBoolean());
        Assert.False(data.GetProperty("authenticationSucceeded").GetBoolean());
        Assert.Equal(
            MoodleErrorContract.TokenDecryptionFailed,
            data.GetProperty("diagnosticErrorCode").GetString());
    }

    [Fact]
    public async Task DiagnoseConnectionAsync_NaoMarcaEtapasPosterioresQuandoUrlForInvalida()
    {
        var failure = new MoodleApiException(
            MoodleErrorContract.NetworkError,
            "internal",
            connectionId: "goias-connection",
            connectionAlias: "goias",
            stage: MoodleIntegrationStage.UrlValidation);
        var sut = CreateSut(new FakeCredentialsProvider(error: failure), new FakeRestClient(SiteInfo()));

        var result = await sut.DiagnoseConnectionAsync("goias");

        var data = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("data");
        Assert.True(data.GetProperty("connectionFound").GetBoolean());
        Assert.True(data.GetProperty("active").GetBoolean());
        Assert.False(data.GetProperty("urlValid").GetBoolean());
        Assert.False(data.GetProperty("credentialsPresent").GetBoolean());
        Assert.False(data.GetProperty("decryptionSucceeded").GetBoolean());
        Assert.False(data.GetProperty("tokenAvailable").GetBoolean());
        Assert.False(data.GetProperty("authenticationSucceeded").GetBoolean());
    }

    [Fact]
    public async Task ListFunctionsAsync_ExpoeTodasAsFuncoesDoToken()
    {
        var sut = CreateSut(
            new FakeCredentialsProvider(Connection()),
            new FakeRestClient(SiteInfo()),
            [
                new MoodleFunctionDescriptor("core_course_get_courses_by_field", MoodleFunctionRisk.Read, true),
                new MoodleFunctionDescriptor("mod_assign_save_grade", MoodleFunctionRisk.ControlledWrite, true),
                new MoodleFunctionDescriptor("local_plugin_secret", MoodleFunctionRisk.Unknown, true)
            ]);

        var result = await sut.ListFunctionsAsync();

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.True(structured.TryGetProperty("data", out var data), structured.GetRawText());
        Assert.Equal(3, data.GetArrayLength());
        Assert.Contains(data.EnumerateArray(), item => item.GetProperty("Name").GetString() == "core_course_get_courses_by_field");
        Assert.Contains(data.EnumerateArray(), item => item.GetProperty("Name").GetString() == "mod_assign_save_grade");
        Assert.Contains(data.EnumerateArray(), item => item.GetProperty("Name").GetString() == "local_plugin_secret");
    }

    [Fact]
    public async Task GetCoverageReportAsync_PaginaEExplicitaAusenciaDeContrato()
    {
        var sut = CreateSut(
            new FakeCredentialsProvider(Connection()),
            new FakeRestClient(SiteInfo()),
            [
                new MoodleFunctionDescriptor("core_course_get_courses_by_field", MoodleFunctionRisk.Read, true),
                new MoodleFunctionDescriptor("mod_assign_save_grade", MoodleFunctionRisk.ControlledWrite, true)
            ]);

        var result = await sut.GetCoverageReportAsync(page: 1, pageSize: 1);

        var data = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("data");
        Assert.Equal(2, data.GetProperty("TotalDiscovered").GetInt32());
        Assert.True(data.GetProperty("HasMore").GetBoolean());
        Assert.Equal(1, data.GetProperty("Items").GetArrayLength());
        Assert.Equal(2, data.GetProperty("StateCounts").GetProperty("schema_unavailable").GetInt32());
        Assert.Contains(
            data.GetProperty("Warnings").EnumerateArray(),
            warning => warning.GetString() == "contract_registry_unavailable");
    }

    [Fact]
    public async Task GetCoverageReportAsync_NaoContaFuncaoAdministrativaComoCoberta()
    {
        using var input = JsonDocument.Parse("{\"type\":\"object\"}");
        using var output = JsonDocument.Parse("{\"type\":\"object\"}");
        var contract = new MoodleFunctionContract
        {
            FunctionName = "core_admin_get_site_info",
            Effect = MoodleEffect.Read,
            InputSchema = input.RootElement.Clone(),
            OutputSchema = output.RootElement.Clone(),
            MoodleVersion = "5.0.1",
            Status = MoodleContractStatus.Verified,
            AdministrativeOnly = true,
            PlatformPermission = "tool.moodle.admin.functions",
            ContractHash = string.Empty
        };
        contract = contract with { ContractHash = MoodleFunctionContractRegistry.ComputeContractHash(contract) };

        var sut = CreateSut(
            new FakeCredentialsProvider(Connection()),
            new FakeRestClient(SiteInfo()),
            [new MoodleFunctionDescriptor(contract.FunctionName, MoodleFunctionRisk.Read, true)],
            contractRegistry: new MoodleFunctionContractRegistry([contract]),
            currentUser: new FakeCurrentUser("user"));

        var result = await sut.GetCoverageReportAsync(pageSize: 10);

        var data = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("data");
        Assert.Equal(1, data.GetProperty("StateCounts").GetProperty("policy_blocked").GetInt32());
        var item = Assert.Single(data.GetProperty("Items").EnumerateArray());
        Assert.Equal("policy_blocked", item.GetProperty("CoverageState").GetString());
        Assert.False(item.GetProperty("ExecutionReady").GetBoolean());
        Assert.True(item.GetProperty("AdministrativeOnly").GetBoolean());
        Assert.Contains(
            item.GetProperty("Reasons").EnumerateArray(),
            reason => reason.GetString() == "administrative_function_restricted");
    }

    [Fact]
    public async Task GetOperationResultAsync_ConsultaResultadoPersistidoSemReexecutar()
    {
        var action = new PendingMoodleAction
        {
            ToolName = "moodle_prepare_write",
            CreatedBySubject = "user",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            PayloadJson = "{}",
            PreviewJson = "{}",
            ConfirmationText = "confirm",
            CorrelationId = "correlation"
        };
        action.RecordResult("{\"operation\":\"moodle_call\",\"function\":\"mod_assign_save_grade\",\"status\":\"executed\",\"payload\":{\"result\":\"ok\"}}");
        var pendingActions = new FakePendingActionRepository(action);
        var sut = CreateSut(
            new FakeCredentialsProvider(Connection()),
            new FakeRestClient(SiteInfo()),
            pendingActions: pendingActions,
            currentUser: new FakeCurrentUser("user"));

        var result = await sut.GetOperationResultAsync(action.Id);

        Assert.False(result.IsError);
        var data = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("data");
        Assert.Equal("executed", data.GetProperty("status").GetString());
        Assert.Equal("mod_assign_save_grade", data.GetProperty("function").GetString());
        Assert.Equal("ok", data.GetProperty("payload").GetProperty("result").GetString());
        Assert.False(data.GetProperty("executionUnknown").GetBoolean());
    }

    [Fact]
    public async Task ExecuteReadAsync_EmiteContinuacaoOpacaVinculadaAoContrato()
    {
        using var input = JsonDocument.Parse("{\"type\":\"object\"}");
        using var output = JsonDocument.Parse("{\"type\":\"object\"}");
        var contract = new MoodleFunctionContract
        {
            FunctionName = "core_course_get_courses",
            Effect = MoodleEffect.Read,
            InputSchema = input.RootElement.Clone(),
            OutputSchema = output.RootElement.Clone(),
            Status = MoodleContractStatus.Verified,
            ContractHash = string.Empty,
            Pagination = new MoodlePaginationContract("offset", "offset", "limit", HasMoreProperty: "hasMore")
        };
        contract = contract with { ContractHash = MoodleFunctionContractRegistry.ComputeContractHash(contract) };
        var tokenService = new MoodleContinuationTokenService(DataProtectionProvider.Create("moodle-connector-tests"));
        var sut = CreateSut(
            new FakeCredentialsProvider(Connection()),
            new FakeRestClient(SiteInfo()),
            [new MoodleFunctionDescriptor(contract.FunctionName, MoodleFunctionRisk.Read, true)],
            safeReadExecutor: new FakeSafeReadExecutor(JsonNode.Parse("{\"items\":[{\"id\":1}],\"hasMore\":true}")),
            contractRegistry: new MoodleFunctionContractRegistry([contract]),
            currentUser: new FakeCurrentUser("user"),
            continuationTokens: tokenService);

        using var parameters = JsonDocument.Parse("{\"offset\":0,\"limit\":1}");
        var result = await sut.ExecuteReadAsync(contract.FunctionName, parameters.RootElement);

        var data = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("data");
        var completeness = data.GetProperty("Completeness");
        Assert.True(completeness.GetProperty("Truncated").GetBoolean());
        Assert.True(completeness.GetProperty("HasMore").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(completeness.GetProperty("ContinuationToken").GetString()));

        var tampered = completeness.GetProperty("ContinuationToken").GetString() + "tampered";
        using var empty = JsonDocument.Parse("{}");
        var rejected = await sut.ExecuteReadAsync(contract.FunctionName, empty.RootElement, continuationToken: tampered);
        Assert.True(rejected.IsError);
        Assert.Contains("invalid_continuation_token", rejected.StructuredContent?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckFunctionAsync_ReturnsControlledFunctionWhenEnabledForToken()
    {
        var sut = CreateSut(
            new FakeCredentialsProvider(Connection()),
            new FakeRestClient(SiteInfo()),
            [new MoodleFunctionDescriptor("mod_assign_save_grade", MoodleFunctionRisk.ControlledWrite, true)]);

        var result = await sut.CheckFunctionAsync("mod_assign_save_grade");

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.True(structured.TryGetProperty("data", out var data), structured.GetRawText());
        Assert.Equal((int)MoodleFunctionRisk.ControlledWrite, data.GetProperty("Risk").GetInt32());
        Assert.True(data.GetProperty("IsAvailable").GetBoolean());
    }

    [Fact]
    public async Task DescribeFunctionAsync_SeparaContratoConhecidoDeDisponibilidadeDoToken()
    {
        using var input = JsonDocument.Parse("{\"type\":\"object\"}");
        using var output = JsonDocument.Parse("{\"type\":\"object\"}");
        var contract = new MoodleFunctionContract
        {
            FunctionName = "local_plugin_get_course",
            Effect = MoodleEffect.Read,
            InputSchema = input.RootElement.Clone(),
            OutputSchema = output.RootElement.Clone(),
            MoodleVersion = "5.0.1",
            Status = MoodleContractStatus.Verified,
            ContractHash = string.Empty
        };
        contract = contract with { ContractHash = MoodleFunctionContractRegistry.ComputeContractHash(contract) };
        var sut = CreateSut(
            new FakeCredentialsProvider(Connection()),
            new FakeRestClient(SiteInfo()),
            [new MoodleFunctionDescriptor("core_course_get_courses_by_field", MoodleFunctionRisk.Read, true)],
            contractRegistry: new MoodleFunctionContractRegistry([contract]));

        var result = await sut.DescribeFunctionAsync(contract.FunctionName);

        var data = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("data");
        Assert.False(data.GetProperty("IsAvailable").GetBoolean());
        Assert.Equal((int)MoodleContractStatus.Verified, data.GetProperty("ContractStatus").GetInt32());
        Assert.Equal((int)MoodleEffect.Read, data.GetProperty("ContractEffect").GetInt32());
    }

    [Fact]
    public async Task ListAvailableFlowsAsync_ExpoeFreshnessDoCatalogoDeCapabilities()
    {
        var sut = CreateSut(
            new FakeCredentialsProvider(Connection()),
            new FakeRestClient(SiteInfo()),
            [new MoodleFunctionDescriptor("core_course_get_courses_by_field", MoodleFunctionRisk.Read, true)]);

        var result = await sut.ListAvailableFlowsAsync(forceRefresh: true);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var freshness = structured.GetProperty("freshness");
        Assert.Equal("live", freshness.GetProperty("source").GetString());
        Assert.Equal("capabilities", freshness.GetProperty("dataset").GetString());
        Assert.Equal("available_flows", freshness.GetProperty("recordType").GetString());
        Assert.True(freshness.GetProperty("complete").GetBoolean());
        Assert.True(freshness.GetProperty("decisionSafe").GetBoolean());
    }

    [Fact]
    public async Task ExecuteReadAsync_RedigePayloadPublicoRecursivamente()
    {
        var payload = JsonNode.Parse(
            """
            {
              "token": "token-real",
              "nested": { "privateAccessKey": "private-real" },
              "array": [{ "password": "password-real" }],
              "safe": "visible"
            }
            """);
        var sut = CreateSut(
            new FakeCredentialsProvider(Connection()),
            new FakeRestClient(SiteInfo()),
            safeReadExecutor: new FakeSafeReadExecutor(payload));
        using var parameters = JsonDocument.Parse("{}");

        var result = await sut.ExecuteReadAsync(
            "core_course_get_courses_by_field",
            parameters.RootElement,
            cancellationToken: CancellationToken.None);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        var raw = structured.GetRawText();
        Assert.DoesNotContain("token-real", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("private-real", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("password-real", raw, StringComparison.Ordinal);
        Assert.Contains("visible", raw, StringComparison.Ordinal);
    }

    private static MoodleUniversalTools CreateSut(
        IMoodleConnectorCredentialsProvider credentialsProvider,
        FakeRestClient restClient,
        IReadOnlyList<MoodleFunctionDescriptor>? descriptors = null,
        FakeSafeReadExecutor? safeReadExecutor = null,
        IMoodleFunctionContractRegistry? contractRegistry = null,
        ICurrentUserContext? currentUser = null,
        IPendingMoodleActionRepository? pendingActions = null,
        MoodleContinuationTokenService? continuationTokens = null)
    {
        return new MoodleUniversalTools(
            new FakeCatalog(descriptors),
            safeReadExecutor ?? new FakeSafeReadExecutor(),
            new OperationRegistry(),
            new PolicyEngine(),
            new MoodleBusinessFlowRegistry(),
            credentialsProvider,
            new MoodleConnectionSelection(),
            restClient,
            NullLogger<MoodleUniversalTools>.Instance,
            contractRegistry,
            currentUser,
            pendingActions,
            continuationTokens);
    }

    private static MoodleConnectorCredentials Connection() => new(
        "client",
        "goias-connection",
        "goias",
        "https://ead.fieg.com.br/?sensitive=query",
        "user",
        "password",
        "goias",
        false);

    private static JsonElement SiteInfo()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "sitename": "Moodle Goias",
              "release": "5.0.1",
              "userid": 847,
              "functions": [
                { "name": "core_webservice_get_site_info" },
                { "name": "core_course_get_courses_by_field" }
              ]
            }
            """);
        return document.RootElement.Clone();
    }

    private sealed class FakeCredentialsProvider(
        MoodleConnectorCredentials? connection = null,
        Exception? error = null) : IMoodleConnectorCredentialsProvider
    {
        public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken) =>
            error is null
                ? Task.FromResult(connection!)
                : Task.FromException<MoodleConnectorCredentials>(error);
    }

    private sealed class FakeRestClient(JsonElement payload) : IMoodleRestClient
    {
        public bool LastAllowServiceToken { get; private set; } = true;

        public Task<JsonElement> CallAsync(
            MoodleConnectorCredentials connection,
            string functionName,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken cancellationToken) => Task.FromResult(payload);

        public Task<JsonElement> CallAsync(
            MoodleConnectorCredentials connection,
            string functionName,
            IReadOnlyDictionary<string, object?> parameters,
            bool allowServiceToken,
            CancellationToken cancellationToken)
        {
            LastAllowServiceToken = allowServiceToken;
            return Task.FromResult(payload);
        }
    }

    private sealed class FakeCatalog(IReadOnlyList<MoodleFunctionDescriptor>? descriptors = null) : IMoodleFunctionCatalog
    {
        public Task<MoodleFunctionProfile> GetCurrentAsync(
            bool forceRefresh,
            CancellationToken cancellationToken) => Task.FromResult(new MoodleFunctionProfile(
            "goias-connection",
            "goias",
            "Moodle Goias",
            "5.0.1",
            847,
            descriptors ??
            [
                new MoodleFunctionDescriptor(
                    "core_webservice_get_site_info",
                    MoodleFunctionRisk.Read,
                    true),
                new MoodleFunctionDescriptor(
                    "core_course_get_courses_by_field",
                    MoodleFunctionRisk.Read,
                    true)
            ],
            DateTimeOffset.UtcNow));
    }

    private sealed class FakeSafeReadExecutor(JsonNode? payload = null) : ISafeReadExecutor
    {
        public Task<System.Text.Json.Nodes.JsonNode?> ExecuteAsync(
            string functionName,
            Dictionary<string, object?> parameters,
            string? moodleAlias = null,
            NormalizationContext? context = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(payload);
    }

    private sealed class FakeCurrentUser(string subject) : ICurrentUserContext
    {
        public string Subject => subject;
        public string? Email => null;
        public IReadOnlyCollection<string> Scopes => [];
        public bool HasScope(string scope) => false;
        public bool HasPlatformPermission(string permission) => permission == "tool.pending_actions.manage";
    }

    private sealed class FakePendingActionRepository(PendingMoodleAction action) : IPendingMoodleActionRepository
    {
        public Task<PendingMoodleAction?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<PendingMoodleAction?>(action.Id == id ? action : null);

        public Task AddAsync(PendingMoodleAction action, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<PendingActionConfirmationClaimResult> TryConfirmWithAuditAsync(
            Guid id,
            string confirmedBySubject,
            DateTimeOffset confirmedAt,
            MoodleAuditLog confirmationAudit,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PendingActionConfirmationClaimResult(false, action.Status, action.ConfirmedAt));

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
