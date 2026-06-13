using Microsoft.Extensions.DependencyInjection;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.Grading;
using MoodleConnector.Application.PendingActions;

namespace MoodleConnector.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        services.AddOptions<AssignmentWriteFeatureOptions>();
        services.AddScoped<IPendingActionService, PendingActionService>();
        services.AddScoped<IActionConfirmationService, ActionConfirmationService>();
        services.AddSingleton<IGradingAnalysisService, StructuredGradingAnalysisService>();

        return services;
    }
}
