using Microsoft.Extensions.DependencyInjection;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.Grading;
using MoodleConnector.Application.Memory;
using MoodleConnector.Application.PendingActions;
using Microsoft.Extensions.Options;

namespace MoodleConnector.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        services.AddOptions<AssignmentWriteFeatureOptions>();
        services.AddOptions<GradingLimitsOptions>();
        services.AddScoped<IPendingActionService, PendingActionService>();
        services.AddScoped<IActionConfirmationService, ActionConfirmationService>();
        services.AddScoped<IUserMemoryService, UserMemoryService>();
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
