using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Automation;

namespace MoodleConnector.Infrastructure.Automation;

internal sealed class AutomationSchedulerService(
    IServiceScopeFactory scopeFactory,
    IOptions<AutomationSchedulerOptions> options,
    ILogger<AutomationSchedulerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("AutomationSchedulerService desabilitado por configuração.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Clamp(options.Value.PollIntervalSeconds, 5, 3600));
        using var timer = new PeriodicTimer(interval);
        logger.LogInformation("AutomationSchedulerService iniciado. Intervalo={IntervalSeconds}s", interval.TotalSeconds);

        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var runtime = scope.ServiceProvider.GetRequiredService<IAutomationRuntime>();
                var count = await runtime.RunDueAsync(stoppingToken);
                if (count > 0)
                {
                    logger.LogInformation("AutomationSchedulerService executou {Count} automação(ões).", count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falha no ciclo do AutomationSchedulerService.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));

        logger.LogInformation("AutomationSchedulerService encerrado.");
    }
}
