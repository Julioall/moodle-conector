using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;

namespace MoodleConnector.Application.Grading;

/// <summary>
/// Limpa periodicamente o texto extraído de artifacts antigos. A operação é
/// deliberadamente redacional: o registro técnico permanece para auditoria,
/// cobertura e reconciliação, sem reter o conteúdo acadêmico indefinidamente.
/// </summary>
public sealed class GradingRetentionWorkerService(
    IServiceScopeFactory scopeFactory,
    IOptions<GradingLimitsOptions> limits,
    ILogger<GradingRetentionWorkerService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunCleanupAsync(stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunCleanupAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            var retentionDays = Math.Clamp(limits.Value.RawFileRetentionDays, 1, 3650);
            var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetService<IGradingRetentionStore>();
            if (store is null)
            {
                return;
            }

            var redacted = await store.RedactExpiredArtifactTextAsync(cutoff, cancellationToken);
            var resources = scope.ServiceProvider.GetService<IMoodleResourceRepository>();
            var expiredResources = resources is null
                ? 0
                : await resources.RemoveExpiredAsync(DateTimeOffset.UtcNow, cancellationToken);
            if (redacted > 0 || expiredResources > 0)
            {
                logger.LogInformation(
                    "Retenção de correção assistida aplicada a {Count} artifact(s) e {ResourceCount} resource(s); cutoff={Cutoff}.",
                    redacted,
                    expiredResources,
                    cutoff);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao aplicar retenção de artifacts de correção assistida.");
        }
    }
}
