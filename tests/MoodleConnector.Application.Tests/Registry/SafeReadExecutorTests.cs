using System.Text.Json;
using System.Text.Json.Nodes;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Registry;
using MoodleConnector.Domain;
using MoodleConnector.Domain.Registry;

namespace MoodleConnector.Application.Tests.Registry;

public sealed class SafeReadExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldPassThroughPipeline_WhenOperationIsSafe()
    {
        var connectionId = Guid.NewGuid();
        var mockConnectionInfo = new ConnectionInfo(connectionId, "test-alias", "https://test.moodle");
        var safeOp = new MoodleOperation("safe_read_func", "cat", OperationType.Read, ToolRiskLevel.ReadOnly, OperationPolicy.Direct, "prof1");
        var creds = new MoodleConnectorCredentials("client", connectionId.ToString(), "test-alias", "https://test.moodle", "user1", "pass", "target", false);
        var snap = new CapabilitySnapshot(connectionId, "user1", new HashSet<string> { "safe_read_func" }, DateTimeOffset.UtcNow);

        var connectionReg = new FakeConnectionRegistry(mockConnectionInfo);
        var opReg = new FakeOperationRegistry(safeOp);
        var policy = new FakePolicyEngine(new PolicyEvaluationResult(PolicyDecision.Allow, "OK"));
        var credsProvider = new FakeCredentialsProvider(creds);
        var capReg = new FakeCapabilityRegistry(snap);
        var restClient = new FakeRestClient(JsonDocument.Parse("{\"key\":\"value\"}").RootElement);
        var normalizer = new FakeResponseNormalizer(JsonNode.Parse("{\"normalized\":\"true\"}"));

        var executor = new SafeReadExecutor(connectionReg, opReg, capReg, policy, normalizer, credsProvider, restClient);

        // Act
        var result = await executor.ExecuteAsync("safe_read_func", new Dictionary<string, object?>());

        // Assert
        Assert.NotNull(result);
        Assert.Equal("true", result!["normalized"]!.ToString());
        Assert.True(restClient.WasCalled);
    }

    private sealed class FakeConnectionRegistry(ConnectionInfo info) : IConnectionRegistry
    {
        public Task<ConnectionInfo?> ResolveConnectionAsync(string? alias, CancellationToken cancellationToken = default) => Task.FromResult<ConnectionInfo?>(info);
    }

    private sealed class FakeOperationRegistry(MoodleOperation op) : IOperationRegistry
    {
        public MoodleOperation? GetOperation(string operationName) => op;
        public IReadOnlyList<MoodleOperation> GetAllOperations() => [op];
    }

    private sealed class FakePolicyEngine(PolicyEvaluationResult result) : IPolicyEngine
    {
        public PolicyEvaluationResult Evaluate(MoodleOperation? operation) => result;
    }

    private sealed class FakeCredentialsProvider(MoodleConnectorCredentials creds) : IMoodleConnectorCredentialsProvider
    {
        public Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken) => Task.FromResult(creds);
    }

    private sealed class FakeCapabilityRegistry(CapabilitySnapshot snap) : ICapabilityRegistry
    {
        public Task<CapabilitySnapshot> GetSnapshotAsync(ConnectionInfo connectionInfo, string userToken, CancellationToken cancellationToken = default) => Task.FromResult(snap);

        public void Invalidate(ConnectionInfo connectionInfo, string userToken)
        {
        }
    }

    private sealed class FakeResponseNormalizer(JsonNode? result) : IResponseNormalizer
    {
        public JsonNode? Normalize(string profileName, JsonNode? rawResponse, NormalizationContext? context = null) => result;
    }

    private sealed class FakeRestClient(JsonElement result) : IMoodleRestClient
    {
        public bool WasCalled { get; private set; }

        public Task<JsonElement> CallAsync(MoodleConnectorCredentials connection, string functionName, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
        {
            return CallAsync(connection, functionName, parameters, true, cancellationToken);
        }

        public Task<JsonElement> CallAsync(MoodleConnectorCredentials credentials, string functionName, IReadOnlyDictionary<string, object?> parameters, bool allowServiceToken, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(result);
        }
    }
}
