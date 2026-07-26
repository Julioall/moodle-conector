using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.Pedagogy;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Infrastructure.Configuration;
using MoodleConnector.Infrastructure.DocumentExtraction;
using MoodleConnector.Infrastructure.Pedagogy;
using MoodleConnector.Infrastructure.Reports;
using MoodleConnector.Infrastructure.MoodleApi;
using Polly;
using Polly.Extensions.Http;

namespace MoodleConnector.Infrastructure;

public static class DependencyInjection
{
    private static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan HandlerLifetime = TimeSpan.FromMinutes(5);

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<PostgresOptions>()
            .Bind(configuration.GetSection(PostgresOptions.SectionName));

        services
            .AddOptions<ConnectorSecretsOptions>()
            .Bind(configuration.GetSection(ConnectorSecretsOptions.SectionName));

        services
            .AddOptions<MoodleApiOptions>()
            .Bind(configuration.GetSection(MoodleApiOptions.SectionName));

        services
            .AddOptions<AssignmentWriteFeatureOptions>()
            .Bind(configuration.GetSection(AssignmentWriteFeatureOptions.SectionName));

        services
            .AddOptions<MoodleProxyOptions>()
            .Bind(configuration.GetSection(MoodleProxyOptions.SectionName));

        var postgresOptions = configuration
            .GetSection(PostgresOptions.SectionName)
            .Get<PostgresOptions>() ?? new PostgresOptions();

        var moodleApiOptions = configuration
            .GetSection(MoodleApiOptions.SectionName)
            .Get<MoodleApiOptions>() ?? new MoodleApiOptions();

        var moodleProxyOptions = configuration
            .GetSection(MoodleProxyOptions.SectionName)
            .Get<MoodleProxyOptions>() ?? new MoodleProxyOptions();

        var moodleApiResilience = CreateResilienceSettings(
            moodleApiOptions.HttpTimeoutSeconds,
            moodleApiOptions.HttpRetryCount,
            moodleApiOptions.CircuitBreakerHandledEventsAllowedBeforeBreaking,
            moodleApiOptions.CircuitBreakerDurationSeconds);

        var moodleProxyResilience = CreateResilienceSettings(
            moodleProxyOptions.HttpTimeoutSeconds,
            moodleProxyOptions.HttpRetryCount,
            moodleProxyOptions.CircuitBreakerHandledEventsAllowedBeforeBreaking,
            moodleProxyOptions.CircuitBreakerDurationSeconds);

        if (string.IsNullOrWhiteSpace(postgresOptions.ConnectionString))
        {
            throw new InvalidOperationException("Postgres:ConnectionString nao configurado.");
        }

        services.AddDbContext<ConnectorDbContext>(options =>
        {
            options.UseNpgsql(postgresOptions.ConnectionString);
            options.UseOpenIddict();
        });

        services.AddMemoryCache();
        services.AddSingleton<IPedagogicGuidanceSearch>(_ =>
            new MarkdownPedagogicGuidanceSearch(Path.Combine(AppContext.BaseDirectory, "public", "pedagogic")));
        services.AddScoped<IPendingMoodleActionRepository, PendingMoodleActionRepository>();
        services.AddScoped<IGradingReviewRepository, GradingReviewRepository>();
        services.AddScoped<IMoodleAuditLogRepository, MoodleAuditLogRepository>();
        services.AddScoped<IUserMemoryRepository, UserMemoryRepository>();
        services.AddScoped<IUserMemoryDocumentRepository, UserMemoryDocumentRepository>();
        services.AddScoped<IAuthorizationAuditService, AuthorizationAuditService>();
        services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        services.AddScoped<IMoodleUserResolver, MoodleUserResolver>();
        services
            .AddHttpClient<IMoodleRestClient, MoodleRestClient>(ConfigureMoodleApiClient)
            .AddMoodleResilience(moodleApiResilience);
        services.AddScoped<IMoodleFunctionCatalog, MoodleFunctionCatalog>();
        services.AddScoped<IMoodleFunctionExecutor, MoodleFunctionExecutor>();
        services.AddScoped<IMoodleUniversalWriteService, MoodleUniversalWriteService>();
        services.AddSingleton<IMoodleResourceResolver, MoodleResourceResolver>();
        services
            .AddHttpClient<IMoodleCurrentUserIdGateway, MoodleCurrentUserIdGateway>(ConfigureMoodleApiClient)
            .AddMoodleResilience(moodleApiResilience);
        services.AddScoped<IMcpConnectorClientResolver, DatabaseConnectorClientResolver>();
        services.AddScoped<IConnectorClientRegistrationService, DatabaseConnectorClientRegistrationService>();
        services.AddScoped<IMoodleConnectorCredentialsProvider, HttpContextMoodleConnectorCredentialsProvider>();
        services.AddScoped<IGradingTechnicalDiscoveryEnvironment, GradingTechnicalDiscoveryEnvironment>();
        services.AddScoped<IMoodleConnectionSelection, MoodleConnectionSelection>();
        services.AddScoped<IMoodleReportBuilderClient, MoodleReportBuilderClient>();
        services.AddSingleton<IConnectorSecretProtector, AesGcmConnectorSecretProtector>();

        services
            .AddHttpClient<IMoodleCoursesGateway, MoodleCoursesGateway>(ConfigureMoodleApiClient)
            .AddMoodleResilience(moodleApiResilience);

        services
            .AddHttpClient<IMoodleParticipantsGateway, MoodleParticipantsGateway>(ConfigureMoodleApiClient)
            .AddMoodleResilience(moodleApiResilience);

        services
            .AddHttpClient<IMoodleCourseContentsGateway, MoodleCourseContentsGateway>(ConfigureMoodleApiClient)
            .AddMoodleResilience(moodleApiResilience);

        services
            .AddHttpClient<IMoodleForumGateway, MoodleForumGateway>(ConfigureMoodleApiClient)
            .AddMoodleResilience(moodleApiResilience);

        services
            .AddHttpClient<IMoodleAssignmentSubmissionsGateway, MoodleAssignmentSubmissionsGateway>(ConfigureMoodleApiClient)
            .AddMoodleResilience(moodleApiResilience);

        services
            .AddHttpClient<IMoodleAssignmentGradingGateway, MoodleAssignmentGradingGateway>(ConfigureMoodleApiClient)
            .AddMoodleResilience(moodleApiResilience);

        services
            .AddHttpClient<IMoodleAssignmentGradeReadGateway, MoodleAssignmentGradeReadGateway>(ConfigureMoodleApiClient)
            .AddMoodleResilience(moodleApiResilience);

        services
            .AddHttpClient<IMoodleAssignmentSubmissionStatusGateway, MoodleAssignmentSubmissionStatusGateway>(ConfigureMoodleApiClient)
            .AddMoodleResilience(moodleApiResilience);

        services
            .AddHttpClient<IMoodleGradingCapabilitiesGateway, MoodleGradingCapabilitiesGateway>(ConfigureMoodleApiClient)
            .AddMoodleResilience(moodleApiResilience);

        services
            .AddHttpClient<IMoodleSubmissionFileGateway, MoodleSubmissionFileGateway>(ConfigureMoodleApiClient)
            .AddMoodleResilience(moodleApiResilience);

        services
            .AddHttpClient<IMoodleAssignmentSettingsGateway, MoodleAssignmentSettingsGateway>(ConfigureMoodleApiClient)
            .AddMoodleResilience(moodleApiResilience);

        services
            .AddHttpClient<IMoodleGradebookGateway, MoodleGradebookGateway>(ConfigureMoodleApiClient)
            .AddMoodleResilience(moodleApiResilience);

        services
            .AddHttpClient<IMoodleCompletionGateway, MoodleCompletionGateway>(ConfigureMoodleApiClient)
            .AddMoodleResilience(moodleApiResilience);

        services
            .AddHttpClient<IMoodleMessageGateway, MoodleMessageGateway>(ConfigureMoodleApiClient)
            .AddMoodleResilience(moodleApiResilience);

        services.AddSingleton<IDocumentExtractionService, DocumentExtractionService>();

        services
            .AddOptions<OcrOptions>()
            .Bind(configuration.GetSection(OcrOptions.SectionName));

        services.AddSingleton<IOcrService, TesseractOcrService>();

        services
            .AddHttpClient<IMoodleAccessTokenProvider, MoodleAccessTokenProvider>(ConfigureMoodleApiClient)
            .AddMoodleResilience(moodleApiResilience);

        services
            .AddHttpClient<IMoodleProxyGateway, MoodleProxyGateway>(ConfigureMoodleProxyClient)
            .AddMoodleResilience(moodleProxyResilience);

        services.AddScoped<IAccountService, AccountService>();

        services
            .AddHttpClient<IMoodleCredentialValidator, MoodleCredentialValidator>(ConfigureMoodleApiClient)
            .AddMoodleResilience(moodleApiResilience);

        return services;
    }

    private static void ConfigureMoodleApiClient(IServiceProvider provider, HttpClient client)
    {
        var options = provider.GetRequiredService<IOptions<MoodleApiOptions>>().Value;
        ConfigureBaseAddress(client, options.BaseUrl);
    }

    private static void ConfigureMoodleProxyClient(IServiceProvider provider, HttpClient client)
    {
        var options = provider.GetRequiredService<IOptions<MoodleProxyOptions>>().Value;
        ConfigureBaseAddress(client, options.BaseUrl);
    }

    private static void ConfigureBaseAddress(HttpClient client, string baseUrl)
    {
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            client.BaseAddress = baseUri;
        }
    }

    private static IHttpClientBuilder AddMoodleResilience(
        this IHttpClientBuilder builder,
        MoodleHttpResilienceSettings settings)
    {
        return builder
            .ConfigureHttpClient(client => client.Timeout = settings.Timeout)
            .ConfigurePrimaryHttpMessageHandler(() => CreatePooledHandler())
            .SetHandlerLifetime(HandlerLifetime)
            .AddPolicyHandler(CreateCircuitBreakerPolicy(settings))
            .AddPolicyHandler(CreateRetryPolicy(settings));
    }

    private static MoodleHttpResilienceSettings CreateResilienceSettings(
        int timeoutSeconds,
        int retryCount,
        int circuitBreakerHandledEventsAllowedBeforeBreaking,
        int circuitBreakerDurationSeconds)
    {
        return new MoodleHttpResilienceSettings(
            Timeout: TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 300)),
            RetryCount: Math.Clamp(retryCount, 0, 5),
            CircuitBreakerHandledEventsAllowedBeforeBreaking: Math.Clamp(circuitBreakerHandledEventsAllowedBeforeBreaking, 0, 50),
            CircuitBreakerDuration: TimeSpan.FromSeconds(Math.Clamp(circuitBreakerDurationSeconds, 5, 300)));
    }

    private static IAsyncPolicy<HttpResponseMessage> CreateRetryPolicy(MoodleHttpResilienceSettings settings)
    {
        if (settings.RetryCount == 0)
        {
            return Policy.NoOpAsync<HttpResponseMessage>();
        }

        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                settings.RetryCount,
                retryAttempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, retryAttempt - 1)));
    }

    private static IAsyncPolicy<HttpResponseMessage> CreateCircuitBreakerPolicy(MoodleHttpResilienceSettings settings)
    {
        if (settings.CircuitBreakerHandledEventsAllowedBeforeBreaking == 0)
        {
            return Policy.NoOpAsync<HttpResponseMessage>();
        }

        var handledEventsAllowedBeforeBreaking = Math.Max(2, settings.CircuitBreakerHandledEventsAllowedBeforeBreaking);

        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(handledEventsAllowedBeforeBreaking, settings.CircuitBreakerDuration);
    }

    private static SocketsHttpHandler CreatePooledHandler()
    {
        return new SocketsHttpHandler
        {
            PooledConnectionLifetime = PooledConnectionLifetime,
            PooledConnectionIdleTimeout = PooledConnectionIdleTimeout
        };
    }

    private sealed record MoodleHttpResilienceSettings(
        TimeSpan Timeout,
        int RetryCount,
        int CircuitBreakerHandledEventsAllowedBeforeBreaking,
        TimeSpan CircuitBreakerDuration);
}
