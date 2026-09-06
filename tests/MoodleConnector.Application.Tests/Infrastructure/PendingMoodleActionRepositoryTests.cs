using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;
using MoodleConnector.Domain;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class PendingMoodleActionRepositoryTests
{
    [Fact]
    public async Task ListRecoverableGradingPublications_FiltraAutorizadasELeasesExpirados()
    {
        var options = new DbContextOptionsBuilder<ConnectorDbContext>()
            .UseInMemoryDatabase($"pending-actions-{Guid.NewGuid():N}")
            .Options;
        var now = DateTimeOffset.UtcNow;
        await using (var seed = new ConnectorDbContext(options))
        {
            var authorized = Create("criar_previa_lancamento_lote");
            authorized.Authorize("teacher-1", now);

            // Compatibility: actions created before the durable Authorized
            // state may still be Confirmed when the process restarts.
            var legacyConfirmed = Create("confirmar_lancamento_lote_moodle");
            legacyConfirmed.Confirm("teacher-1", now);

            var expired = Create("confirmar_lancamento_lote_moodle");
            expired.Authorize("teacher-1", now);
            Assert.True(expired.BeginExecution("old-worker", now.AddMinutes(-20), TimeSpan.FromMinutes(5)));

            var active = Create("confirmar_lancamento_lote_moodle");
            active.Authorize("teacher-1", now);
            Assert.True(active.BeginExecution("active-worker", now, TimeSpan.FromMinutes(5)));

            var generic = Create("confirmar_mensagem");
            generic.Authorize("teacher-1", now);

            seed.PendingMoodleActions.AddRange(authorized, legacyConfirmed, expired, active, generic);
            await seed.SaveChangesAsync();
        }

        await using var db = new ConnectorDbContext(options);
        var repository = new PendingMoodleActionRepository(db);
        var result = await repository.ListRecoverableGradingPublicationsAsync(now, 100, CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, action => action.ToolName == "criar_previa_lancamento_lote");
        Assert.Contains(result, action => action.Status == PendingActionStatus.Confirmed);
        Assert.Contains(result, action => action.ToolName == "confirmar_lancamento_lote_moodle" && action.ExecutionOwner == "old-worker");
        Assert.DoesNotContain(result, action => action.ExecutionOwner == "active-worker");
        Assert.DoesNotContain(result, action => action.ToolName == "confirmar_mensagem");
    }

    [Fact]
    public async Task ListTerminalPublicationIds_EncontraClaimsQuePrecisamDeLimpezaAposCrash()
    {
        var options = new DbContextOptionsBuilder<ConnectorDbContext>()
            .UseInMemoryDatabase($"terminal-publications-{Guid.NewGuid():N}")
            .Options;
        var executedPublicationId = Guid.NewGuid();
        var partialPublicationId = Guid.NewGuid();
        var unknownPublicationId = Guid.NewGuid();

        var executed = Create(
            "criar_previa_lancamento_lote",
            JsonSerializer.Serialize(new { publicationId = executedPublicationId }));
        executed.MarkExecuted();
        var partial = Create(
            "confirmar_lancamento_lote_moodle",
            JsonSerializer.Serialize(new { publicationId = partialPublicationId }));
        partial.MarkPartiallyCompleted("um item falhou");
        var unknown = Create(
            "confirmar_lancamento_lote_moodle",
            JsonSerializer.Serialize(new { publicationId = unknownPublicationId }));
        unknown.MarkExecutionUnknown("resposta perdida");

        await using (var seed = new ConnectorDbContext(options))
        {
            seed.PendingMoodleActions.AddRange(executed, partial, unknown);
            await seed.SaveChangesAsync();
        }

        await using var db = new ConnectorDbContext(options);
        var repository = new PendingMoodleActionRepository(db);
        var result = await repository.ListTerminalGradingPublicationIdsAsync(100, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(executedPublicationId, result);
        Assert.Contains(partialPublicationId, result);
        Assert.DoesNotContain(unknownPublicationId, result);
    }

    [Fact]
    public async Task TryAuthorizeWithAudit_ReenfileiraPublicacaoParcialAposRetryExplicito()
    {
        var options = new DbContextOptionsBuilder<ConnectorDbContext>()
            .UseInMemoryDatabase($"partial-retry-{Guid.NewGuid():N}")
            .ConfigureWarnings(builder => builder.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var action = Create("confirmar_lancamento_lote_moodle");
        action.MarkPartiallyCompleted("um item falhou");
        await using (var seed = new ConnectorDbContext(options))
        {
            seed.PendingMoodleActions.Add(action);
            await seed.SaveChangesAsync();
        }

        await using var db = new ConnectorDbContext(options);
        var repository = new PendingMoodleActionRepository(db);
        var result = await repository.TryAuthorizeWithAuditAsync(
            action.Id,
            "teacher-1",
            DateTimeOffset.UtcNow.AddHours(1),
            new MoodleAuditLog
            {
                CorrelationId = action.CorrelationId,
                ToolName = action.ToolName,
                RiskLevel = ToolRiskLevel.CriticalHumanConfirmedWrite,
                ActorSubject = "teacher-1",
                Status = "authorized"
            },
            CancellationToken.None);

        Assert.True(result.ConfirmedByCaller);
        Assert.Equal(PendingActionStatus.Authorized, result.Status);
        var reloaded = await repository.GetByIdAsync(action.Id, CancellationToken.None);
        Assert.Equal(PendingActionStatus.Authorized, reloaded!.Status);
    }

    private static PendingMoodleAction Create(string toolName, string payloadJson = "{}") => new()
    {
        ToolName = toolName,
        RiskLevel = ToolRiskLevel.CriticalHumanConfirmedWrite,
        CreatedBySubject = "teacher-1",
        PayloadJson = payloadJson,
        PreviewJson = "{}",
        ConfirmationText = "CONFIRMAR_PUBLICACAO",
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        IdempotencyKey = Guid.NewGuid().ToString("N"),
        CorrelationId = Guid.NewGuid().ToString("N")
    };
}
