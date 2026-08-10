using Microsoft.Extensions.DependencyInjection;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.Grading;
using MoodleConnector.Application.Memory;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Application.PendingActions;
using MoodleConnector.Application.Registry;
using Microsoft.Extensions.Options;

namespace MoodleConnector.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        services.AddOptions<AssignmentWriteFeatureOptions>();
        services.AddOptions<MessageWriteFeatureOptions>();
        services.AddOptions<MoodleUniversalApiFeatureOptions>();
        services.AddSingleton<IMoodleBusinessFlowRegistry, MoodleBusinessFlowRegistry>();
        services.AddSingleton<IOperationRegistry, OperationRegistry>();
        services.AddScoped<IConnectionRegistry, ConnectionRegistry>();
        services.AddScoped<ICapabilityRegistry, CapabilityRegistry>();
        services.AddSingleton<IPolicyEngine, PolicyEngine>();
        services.AddSingleton<IResponseNormalizer, ResponseNormalizer>();
        services.AddScoped<ISafeReadExecutor, SafeReadExecutor>();
        services.AddOptions<GradingLimitsOptions>();
        services.AddScoped<IPendingActionService, PendingActionService>();
        services.AddScoped<IActionConfirmationService, ActionConfirmationService>();
        services.AddScoped<IUserMemoryService, UserMemoryService>();
        services.AddScoped<IUserMemoryDocumentService, UserMemoryDocumentService>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IGradingAnalysisService, StructuredGradingAnalysisService>();
        services.AddSingleton<ICriteriaGenerationService, HeuristicCriteriaGenerationService>();
        services.AddSingleton<IAssignmentContextSelectionService, HeuristicAssignmentContextSelectionService>();
        services.AddSingleton<GradingBatchChannel>();
        services.AddScoped<GradingItemProcessor>();
        services.AddScoped<IGradingBatchOrchestrator, BackgroundGradingBatchOrchestrator>();
        services.AddScoped<IGradingContextBuilder, GradingContextBuilder>();
        services.AddHostedService<GradingBatchWorkerService>();

        return services;
    }
}
