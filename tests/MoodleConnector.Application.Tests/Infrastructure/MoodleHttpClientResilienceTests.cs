using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class MoodleHttpClientResilienceTests
{
    [Fact]
    public void AddInfrastructure_ConfiguraTimeoutsPorGatewayMoodle()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(BuildConfiguration());

        using var provider = services.BuildServiceProvider();
        var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();

        var restClient = httpClientFactory.CreateClient("IMoodleRestClient");
        var submissionFileClient = httpClientFactory.CreateClient("IMoodleSubmissionFileGateway");
        var proxyClient = httpClientFactory.CreateClient("IMoodleProxyGateway");

        Assert.Equal(TimeSpan.FromSeconds(7), restClient.Timeout);
        Assert.Equal(new Uri("https://moodle.tests/"), restClient.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(7), submissionFileClient.Timeout);
        Assert.Equal(new Uri("https://moodle.tests/"), submissionFileClient.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(11), proxyClient.Timeout);
        Assert.Equal(new Uri("https://proxy.tests/"), proxyClient.BaseAddress);
    }

    [Fact]
    public void AddInfrastructure_RejeitaModoEstritoSemManifesto()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddInfrastructure(BuildConfiguration(requireVerifiedContracts: true)));

        Assert.Contains("ContractManifestPath", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IConfiguration BuildConfiguration(bool requireVerifiedContracts = false)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Postgres:ConnectionString"] = "Host=localhost;Port=5432;Database=moodle_connector_tests;Username=postgres;Password=postgres",
                ["MoodleApi:BaseUrl"] = "https://moodle.tests",
                ["MoodleApi:RequireVerifiedContracts"] = requireVerifiedContracts.ToString(),
                ["MoodleApi:HttpTimeoutSeconds"] = "7",
                ["MoodleApi:HttpRetryCount"] = "1",
                ["MoodleApi:CircuitBreakerHandledEventsAllowedBeforeBreaking"] = "2",
                ["MoodleApi:CircuitBreakerDurationSeconds"] = "5",
                ["MoodleProxy:BaseUrl"] = "https://proxy.tests",
                ["MoodleProxy:HttpTimeoutSeconds"] = "11",
                ["MoodleProxy:HttpRetryCount"] = "1",
                ["MoodleProxy:CircuitBreakerHandledEventsAllowedBeforeBreaking"] = "2",
                ["MoodleProxy:CircuitBreakerDurationSeconds"] = "5"
            })
            .Build();
    }
}
