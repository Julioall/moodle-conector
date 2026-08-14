using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Automation;
using MoodleConnector.Application.Messages;
using MoodleConnector.Application.Risk.Queries;
using MoodleConnector.Application.Submissions.Queries;

namespace MoodleConnector.Infrastructure.Automation;

internal sealed class AutomationRuntime(
    ConnectorDbContext dbContext,
    IMediator mediator,
    IConnectorExecutionContext executionContext,
    IMoodleConnectionSelection connectionSelection,
    ILogger<AutomationRuntime> logger,
    IOptions<AutomationSchedulerOptions> options) : IAutomationRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<AutomationDefinitionSummary>> ListAsync(
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var definitions = await dbContext.AutomationDefinitions
            .AsNoTracking()
            .Where(item => item.OwnerId == ownerId)
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);
        return definitions.Select(Map).ToArray();
    }

    public async Task<AutomationDefinitionSummary> CreateAsync(
        Guid ownerId,
        AutomationDefinitionInput input,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(input);
        var now = DateTimeOffset.UtcNow;
        var entity = new AutomationDefinitionEntity
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            ConnectionAlias = NormalizeOptional(input.ConnectionAlias),
            CourseId = normalized.CourseId,
            Name = normalized.Name,
            Description = normalized.Description,
            ScheduleType = normalized.ScheduleType,
            RunHourUtc = normalized.RunHourUtc,
            RunMinuteUtc = normalized.RunMinuteUtc,
            RunDayOfWeek = normalized.RunDayOfWeek,
            ConditionType = normalized.ConditionType,
            ActionType = normalized.ActionType,
            ConfigJson = AutomationConfigSerializer.Serialize(normalized.Config!),
            IsEnabled = normalized.IsEnabled,
            NextRunAt = normalized.IsEnabled
                ? AutomationScheduleCalculator.CalculateNext(normalized.ScheduleType, normalized.RunHourUtc, normalized.RunMinuteUtc, normalized.RunDayOfWeek, now)
                : null,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.AutomationDefinitions.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<AutomationDefinitionSummary?> UpdateAsync(
        Guid ownerId,
        Guid automationId,
        AutomationDefinitionInput input,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.AutomationDefinitions
            .SingleOrDefaultAsync(item => item.Id == automationId && item.OwnerId == ownerId, cancellationToken);
        if (entity is null) return null;

        var normalized = Normalize(input);
        var now = DateTimeOffset.UtcNow;
        entity.ConnectionAlias = NormalizeOptional(input.ConnectionAlias);
        entity.CourseId = normalized.CourseId;
        entity.Name = normalized.Name;
        entity.Description = normalized.Description;
        entity.ScheduleType = normalized.ScheduleType;
        entity.RunHourUtc = normalized.RunHourUtc;
        entity.RunMinuteUtc = normalized.RunMinuteUtc;
        entity.RunDayOfWeek = normalized.RunDayOfWeek;
        entity.ConditionType = normalized.ConditionType;
        entity.ActionType = normalized.ActionType;
        entity.ConfigJson = AutomationConfigSerializer.Serialize(normalized.Config!);
        entity.IsEnabled = normalized.IsEnabled;
        entity.NextRunAt = normalized.IsEnabled
            ? AutomationScheduleCalculator.CalculateNext(normalized.ScheduleType, normalized.RunHourUtc, normalized.RunMinuteUtc, normalized.RunDayOfWeek, now)
            : null;
        entity.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<bool> DeleteAsync(
        Guid ownerId,
        Guid automationId,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.AutomationDefinitions
            .SingleOrDefaultAsync(item => item.Id == automationId && item.OwnerId == ownerId, cancellationToken);
        if (entity is null) return false;

        dbContext.AutomationDefinitions.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<AutomationRunSummary>> ListRunsAsync(
        Guid ownerId,
        Guid automationId,
        int limit,
        CancellationToken cancellationToken)
    {
        var runs = await dbContext.AutomationRuns
            .AsNoTracking()
            .Where(item => item.OwnerId == ownerId && item.AutomationId == automationId)
            .OrderByDescending(item => item.CreatedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);
        return runs.Select(run =>
        {
            var counts = ReadSummaryCounts(run.SummaryJson);
            return ToSummary(run, [], counts.Created, counts.Skipped, counts.Failed, []);
        }).ToArray();
    }

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var due = await dbContext.AutomationDefinitions
            .AsNoTracking()
            .Where(item => item.IsEnabled && item.NextRunAt != null && item.NextRunAt <= now)
            .OrderBy(item => item.NextRunAt)
            .Take(Math.Clamp(options.Value.MaxDefinitionsPerTick, 1, 1000))
            .Select(item => new { item.OwnerId, item.Id })
            .ToListAsync(cancellationToken);

        var count = 0;
        foreach (var item in due)
        {
            try
            {
                var result = await RunAsync(item.OwnerId, item.Id, "schedule", false, cancellationToken);
                if (result.Status is "succeeded" or "partial") count++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falha ao executar automação {AutomationId}.", item.Id);
            }
        }

        return count;
    }

    public async Task<AutomationRunSummary> RunAsync(
        Guid ownerId,
        Guid automationId,
        string trigger,
        bool force,
        CancellationToken cancellationToken)
    {
        var definition = await dbContext.AutomationDefinitions
            .SingleOrDefaultAsync(item => item.Id == automationId && item.OwnerId == ownerId, cancellationToken)
            ?? throw new KeyNotFoundException("Automação não encontrada.");
        var now = DateTimeOffset.UtcNow;

        if (!force && !definition.IsEnabled)
            return Skipped(definition, "disabled", "Automação desabilitada.", now);
        if (!force && definition.NextRunAt is { } nextRunAt && nextRunAt > now)
            return Skipped(definition, "not_due", "Automação ainda não está vencida.", nextRunAt);

        var scheduledFor = definition.NextRunAt ?? now;
        var idempotencyKey = $"{definition.Id:N}:{scheduledFor.UtcDateTime:yyyyMMddHHmmss}";
        var run = new AutomationRunEntity
        {
            Id = Guid.NewGuid(),
            AutomationId = definition.Id,
            OwnerId = ownerId,
            IdempotencyKey = idempotencyKey,
            Trigger = trigger,
            Status = "running",
            AttemptCount = 1,
            ScheduledFor = scheduledFor,
            StartedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.AutomationRuns.Add(run);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(run).State = EntityState.Detached;
            var existing = await dbContext.AutomationRuns.AsNoTracking()
                .SingleAsync(item => item.IdempotencyKey == idempotencyKey, cancellationToken);
            return ToSummary(existing, [], 0, 0, 0, []);
        }

        var createdActions = 0;
        var skippedActions = 0;
        var failedActions = 0;
        var pendingActionIds = new List<Guid>();
        object? weeklySummary = null;
        var successful = false;
        try
        {
            var account = await dbContext.UserAccounts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == ownerId, cancellationToken)
                ?? throw new InvalidOperationException("A conta proprietária da automação não foi encontrada.");
            var clientId = account.ConnectorClientId ?? account.Id.ToString("D");
            var previousAlias = connectionSelection.Alias;
            executionContext.Enter(clientId, account.Id.ToString("D"), account.Email, ["moodle.read", "moodle.write"]);
            connectionSelection.Alias = definition.ConnectionAlias;

            try
            {
                var candidates = await EvaluateConditionAsync(definition, cancellationToken);
                var config = AutomationConfigSerializer.Deserialize(definition.ConfigJson);

                foreach (var candidate in candidates)
                {
                    var evidenceClaim = await ClaimActionAsync(
                        definition,
                        run,
                        candidate.TargetRef,
                        AutomationCatalog.RecordEvidenceAction,
                        cancellationToken);
                    if (evidenceClaim is null)
                    {
                        skippedActions++;
                        continue;
                    }

                    try
                    {
                        var evidence = CreateEvidence(definition, run, ownerId, candidate, now);
                        dbContext.PortalEvidence.Add(evidence);
                        await dbContext.SaveChangesAsync(cancellationToken);
                        evidenceClaim.Status = "completed";
                        evidenceClaim.ResultJson = JsonSerializer.Serialize(new { evidenceId = evidence.Id }, JsonOptions);
                        evidenceClaim.UpdatedAt = DateTimeOffset.UtcNow;
                        await dbContext.SaveChangesAsync(cancellationToken);
                        createdActions++;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        await MarkFailedAsync(evidenceClaim, exception.Message, cancellationToken);
                        failedActions++;
                    }
                }

                if (definition.ActionType.Equals(AutomationCatalog.GenerateWeeklySummaryAction, StringComparison.OrdinalIgnoreCase))
                {
                    var summaryClaim = await ClaimActionAsync(definition, run, "weekly-summary", definition.ActionType, cancellationToken);
                    if (summaryClaim is null)
                    {
                        skippedActions++;
                    }
                    else
                    {
                        weeklySummary = new
                        {
                            courseId = definition.CourseId,
                            condition = definition.ConditionType,
                            generatedAt = now,
                            candidateCount = candidates.Count,
                            urgentCount = candidates.Count(item => item.IsOverdue),
                            signals = candidates.SelectMany(item => item.Factors).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray()
                        };
                        summaryClaim.Status = "completed";
                        summaryClaim.ResultJson = JsonSerializer.Serialize(weeklySummary, JsonOptions);
                        summaryClaim.UpdatedAt = DateTimeOffset.UtcNow;
                        await dbContext.SaveChangesAsync(cancellationToken);
                        createdActions++;
                    }
                }
                else if (definition.ActionType.Equals(AutomationCatalog.PrepareMoodleMessageAction, StringComparison.OrdinalIgnoreCase) ||
                    definition.ActionType.Equals(AutomationCatalog.FollowupAndPrepareMessageAction, StringComparison.OrdinalIgnoreCase))
                {
                    if (definition.ActionType.Equals(AutomationCatalog.FollowupAndPrepareMessageAction, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var candidate in candidates)
                        {
                            var claim = await ClaimActionAsync(definition, run, candidate.TargetRef, "create_followup", cancellationToken);
                            if (claim is null) { skippedActions++; continue; }
                            try
                            {
                                var followup = CreateFollowup(definition, ownerId, candidate, now);
                                dbContext.Followups.Add(followup);
                                await dbContext.SaveChangesAsync(cancellationToken);
                                claim.Status = "completed";
                                claim.ResultJson = JsonSerializer.Serialize(new { followupId = followup.Id }, JsonOptions);
                                claim.UpdatedAt = DateTimeOffset.UtcNow;
                                await dbContext.SaveChangesAsync(cancellationToken);
                                createdActions++;
                            }
                            catch (Exception exception) when (exception is not OperationCanceledException)
                            {
                                await MarkFailedAsync(claim, exception.Message, cancellationToken);
                                failedActions++;
                            }
                        }
                    }

                    var recipientIds = candidates
                        .Where(item => !string.IsNullOrWhiteSpace(item.StudentId))
                        .Select(item => item.StudentId!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    if (recipientIds.Length > 0)
                    {
                        var claim = await ClaimActionAsync(definition, run, $"message:{definition.CourseId}", "prepare_moodle_message", cancellationToken);
                        if (claim is null)
                        {
                            skippedActions++;
                        }
                        else
                        {
                            try
                            {
                                var message = string.IsNullOrWhiteSpace(config.MessageText)
                                    ? "Olá! Identificamos uma pendência de acompanhamento no seu curso. Acesse o Moodle para verificar as atividades e, se precisar, conte comigo."
                                    : config.MessageText.Trim();
                                var preview = await mediator.Send(
                                    new PrepareTutorMessageCommand(definition.CourseId, TutorMessageType.Acompanhamento, recipientIds, message),
                                    cancellationToken);
                                claim.Status = "pending_approval";
                                claim.ResultJson = JsonSerializer.Serialize(new { pendingActionId = preview.PendingActionId, recipientCount = preview.RecipientCount }, JsonOptions);
                                claim.UpdatedAt = DateTimeOffset.UtcNow;
                                await dbContext.SaveChangesAsync(cancellationToken);
                                if (preview.PendingActionId is { } pendingActionId)
                                    pendingActionIds.Add(pendingActionId);
                                createdActions++;
                            }
                            catch (Exception exception) when (exception is not OperationCanceledException)
                            {
                                await MarkFailedAsync(claim, exception.Message, cancellationToken);
                                failedActions++;
                            }
                        }
                    }
                }
                else
                {
                    foreach (var candidate in candidates)
                    {
                        if (definition.ActionType.Equals(AutomationCatalog.CreateTasksAction, StringComparison.OrdinalIgnoreCase))
                        {
                            await RefreshExistingTaskAsync(definition, ownerId, candidate, now, cancellationToken);
                        }

                        var claim = await ClaimActionAsync(definition, run, candidate.TargetRef, definition.ActionType, cancellationToken);
                        if (claim is null) { skippedActions++; continue; }
                        try
                        {
                            if (definition.ActionType.Equals(AutomationCatalog.CreateTasksAction, StringComparison.OrdinalIgnoreCase))
                            {
                                var task = CreateTask(definition, ownerId, candidate, now);
                                dbContext.Tasks.Add(task);
                                await dbContext.SaveChangesAsync(cancellationToken);
                                claim.ResultJson = JsonSerializer.Serialize(new { taskId = task.Id }, JsonOptions);
                            }
                            else if (definition.ActionType.Equals(AutomationCatalog.CreateFollowupsAction, StringComparison.OrdinalIgnoreCase))
                            {
                                var followup = CreateFollowup(definition, ownerId, candidate, now);
                                dbContext.Followups.Add(followup);
                                await dbContext.SaveChangesAsync(cancellationToken);
                                claim.ResultJson = JsonSerializer.Serialize(new { followupId = followup.Id }, JsonOptions);
                            }
                            else
                            {
                                throw new InvalidOperationException($"Ação não suportada: {definition.ActionType}.");
                            }

                            claim.Status = "completed";
                            claim.UpdatedAt = DateTimeOffset.UtcNow;
                            await dbContext.SaveChangesAsync(cancellationToken);
                            createdActions++;
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        {
                            await MarkFailedAsync(claim, exception.Message, cancellationToken);
                            failedActions++;
                        }
                    }
                }

                successful = failedActions == 0;
                run.Status = failedActions == 0 ? "succeeded" : createdActions > 0 ? "partial" : "failed";
                run.SummaryJson = JsonSerializer.Serialize(new { createdActions, skippedActions, failedActions, pendingActionIds, weeklySummary }, JsonOptions);
            }
            finally
            {
                connectionSelection.Alias = previousAlias;
                executionContext.Clear();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            run.Status = "failed";
            run.ErrorCode = exception.GetType().Name;
            run.ErrorMessage = exception.Message;
            failedActions++;
        }

        var finishedAt = DateTimeOffset.UtcNow;
        run.FinishedAt = finishedAt;
        run.UpdatedAt = finishedAt;
        if (!successful && run.Status == "failed")
        {
            run.ErrorCode ??= "automation_failed";
            definition.NextRunAt = finishedAt.AddMinutes(5);
        }
        else
        {
            definition.LastRunAt = finishedAt;
            definition.NextRunAt = AutomationScheduleCalculator.CalculateNext(
                definition.ScheduleType,
                definition.RunHourUtc,
                definition.RunMinuteUtc,
                definition.RunDayOfWeek,
                finishedAt);
        }
        definition.UpdatedAt = finishedAt;
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToSummary(run, pendingActionIds, createdActions, skippedActions, failedActions, []);
    }

    private async Task<IReadOnlyList<AutomationCandidate>> EvaluateConditionAsync(
        AutomationDefinitionEntity definition,
        CancellationToken cancellationToken)
    {
        var config = AutomationConfigSerializer.Deserialize(definition.ConfigJson);
        if (definition.ConditionType.Equals(AutomationCatalog.OverdueSubmissionsCondition, StringComparison.OrdinalIgnoreCase))
        {
            var result = await mediator.Send(new GetStudentsWithPendingSubmissionsQuery(
                definition.CourseId,
                DueDaysAhead: config.DueDaysAhead,
                MaxStudentsToAnalyze: config.MaxStudentsToAnalyze,
                IncludeAwaitingGrading: false,
                MaxAssignmentsToAnalyze: config.MaxAssignmentsToAnalyze), cancellationToken);
            return result.Students
                .SelectMany(student => student.PendingAssignments
                    .Where(item => item.IsOverdue)
                    .Select(item => new AutomationCandidate(
                        $"{student.StudentId}:{item.AssignmentId}",
                        student.StudentId,
                        student.FullName,
                        item.AssignmentId,
                        item.AssignmentName,
                        item.DueDate,
                        true,
                        [$"Atividade pendente: {item.AssignmentName}"])))
                .ToArray();
        }

        if (definition.ConditionType.Equals(AutomationCatalog.AwaitingGradingCondition, StringComparison.OrdinalIgnoreCase))
        {
            var result = await mediator.Send(new GetStudentsWithPendingSubmissionsQuery(
                definition.CourseId,
                MaxStudentsToAnalyze: config.MaxStudentsToAnalyze,
                IncludeAwaitingGrading: true,
                MaxAssignmentsToAnalyze: config.MaxAssignmentsToAnalyze), cancellationToken);
            return result.AwaitingGrading
                .Select(item => new AutomationCandidate(
                    $"{item.StudentId}:{item.Item.AssignmentId}",
                    item.StudentId,
                    item.FullName,
                    item.Item.AssignmentId,
                    item.Item.AssignmentName,
                    item.Item.DueDate,
                    false,
                    ["Submissão aguardando correção"]))
                .ToArray();
        }

        if (definition.ConditionType.Equals(AutomationCatalog.WeeklySignalsCondition, StringComparison.OrdinalIgnoreCase))
        {
            var result = await mediator.Send(new GetStudentsAtRiskReportQuery(
                definition.CourseId,
                config.MaxStudentsToAnalyze,
                config.InactivityThresholdDays,
                config.MinGradePercentage), cancellationToken);
            return result.Reports
                .Select(item => new AutomationCandidate(
                    item.StudentId,
                    item.StudentId,
                    item.FullName,
                    null,
                    null,
                    null,
                    item.RiskLevel == RiskLevel.Alto,
                    item.Factors))
                .ToArray();
        }

        throw new InvalidOperationException($"Condição não suportada: {definition.ConditionType}.");
    }

    private async Task<AutomationActionEntity?> ClaimActionAsync(
        AutomationDefinitionEntity definition,
        AutomationRunEntity run,
        string targetRef,
        string actionType,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = $"{definition.Id:N}:{actionType}:{targetRef}";
        var existing = await dbContext.AutomationActions.SingleOrDefaultAsync(item => item.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status is "completed" or "pending_approval" or "created") return null;
            existing.RunId = run.Id;
            existing.Status = "retrying";
            existing.ErrorMessage = null;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var action = new AutomationActionEntity
        {
            Id = Guid.NewGuid(),
            AutomationId = definition.Id,
            RunId = run.Id,
            OwnerId = definition.OwnerId,
            IdempotencyKey = idempotencyKey,
            ActionType = actionType,
            TargetRef = targetRef,
            Status = "created",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.AutomationActions.Add(action);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return action;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(action).State = EntityState.Detached;
            return null;
        }
    }

    private async Task MarkFailedAsync(AutomationActionEntity action, string message, CancellationToken cancellationToken)
    {
        action.Status = "failed";
        action.ErrorMessage = message;
        action.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshExistingTaskAsync(
        AutomationDefinitionEntity definition,
        Guid ownerId,
        AutomationCandidate candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = $"{definition.Id:N}:{AutomationCatalog.CreateTasksAction}:{candidate.TargetRef}";
        var action = await dbContext.AutomationActions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.IdempotencyKey == idempotencyKey && item.Status == "completed", cancellationToken);
        if (action is null || string.IsNullOrWhiteSpace(action.ResultJson)) return;

        Guid taskId;
        try
        {
            using var document = JsonDocument.Parse(action.ResultJson);
            if (!document.RootElement.TryGetProperty("taskId", out var taskIdElement) ||
                !Guid.TryParse(taskIdElement.GetString(), out taskId)) return;
        }
        catch (JsonException)
        {
            return;
        }

        var task = await dbContext.Tasks.SingleOrDefaultAsync(
            item => item.Id == taskId && item.OwnerId == ownerId,
            cancellationToken);
        if (task is null || string.Equals(task.Status, "done", StringComparison.OrdinalIgnoreCase)) return;

        var refreshed = CreateTask(definition, ownerId, candidate, now);
        task.Description = refreshed.Description;
        task.Priority = refreshed.Priority;
        task.DueAt = refreshed.DueAt;
        task.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static TaskEntity CreateTask(
        AutomationDefinitionEntity definition,
        Guid ownerId,
        AutomationCandidate candidate,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Title = candidate.AssignmentName is null
                ? $"{definition.Name}: acompanhar {candidate.StudentName}"
                : $"Acompanhar entrega: {candidate.AssignmentName} — {candidate.StudentName}",
            Description = $"Automação Moodle-first '{definition.Name}'. Curso {definition.CourseId}. Estudante {candidate.StudentId}. {string.Join(" ", candidate.Factors)}",
            Status = "todo",
            Priority = candidate.IsOverdue ? "urgent" : "high",
            DueAt = candidate.DueDate ?? now.AddDays(1),
            CreatedAt = now,
            UpdatedAt = now
        };

    private static FollowupEntity CreateFollowup(
        AutomationDefinitionEntity definition,
        Guid ownerId,
        AutomationCandidate candidate,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            StudentRef = candidate.StudentId ?? candidate.TargetRef,
            CourseRef = definition.CourseId,
            Kind = "automation",
            Notes = $"{definition.Name}: {string.Join(" ", candidate.Factors)}",
            OccurredAt = now,
            CreatedAt = now
        };

    private static PortalEvidenceEntity CreateEvidence(
        AutomationDefinitionEntity definition,
        AutomationRunEntity run,
        Guid ownerId,
        AutomationCandidate candidate,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            ConnectionAlias = definition.ConnectionAlias,
            CourseId = definition.CourseId,
            StudentId = candidate.StudentId,
            ActivityId = candidate.AssignmentId,
            Kind = definition.ConditionType,
            Title = candidate.AssignmentName is null
                ? $"Sinal de acompanhamento: {candidate.StudentName ?? candidate.StudentId ?? candidate.TargetRef}"
                : $"Sinal em {candidate.AssignmentName}: {candidate.StudentName ?? candidate.StudentId ?? candidate.TargetRef}",
            Details = string.Join(" ", candidate.Factors),
            Source = "moodle.automation",
            AutomationRunId = run.Id,
            ObservedAt = now,
            CreatedAt = now
        };

    private static AutomationDefinitionInput Normalize(AutomationDefinitionInput input)
    {
        input = input with
        {
            ScheduleType = input.ScheduleType.Trim().ToLowerInvariant(),
            ConditionType = input.ConditionType.Trim().ToLowerInvariant(),
            ActionType = input.ActionType.Trim().ToLowerInvariant()
        };
        if (string.IsNullOrWhiteSpace(input.CourseId)) throw new ArgumentException("Curso Moodle é obrigatório.", nameof(input.CourseId));
        if (string.IsNullOrWhiteSpace(input.Name)) throw new ArgumentException("Nome da automação é obrigatório.", nameof(input.Name));
        if (!AutomationCatalog.Conditions.Contains(input.ConditionType)) throw new ArgumentException("Condição de automação inválida.", nameof(input.ConditionType));
        if (!AutomationCatalog.Actions.Contains(input.ActionType)) throw new ArgumentException("Ação de automação inválida.", nameof(input.ActionType));

        AutomationScheduleCalculator.Validate(input.ScheduleType, input.RunHourUtc, input.RunMinuteUtc, input.RunDayOfWeek);
        var config = input.Config ?? new AutomationRuleConfig();
        config = config with
        {
            DueDaysAhead = Math.Clamp(config.DueDaysAhead, 0, 30),
            MaxStudentsToAnalyze = Math.Clamp(config.MaxStudentsToAnalyze, 1, 500),
            MaxAssignmentsToAnalyze = Math.Clamp(config.MaxAssignmentsToAnalyze, 1, 200),
            InactivityThresholdDays = Math.Clamp(config.InactivityThresholdDays, 1, 90),
            MinGradePercentage = Math.Clamp(config.MinGradePercentage, 0, 100),
            MessageText = string.IsNullOrWhiteSpace(config.MessageText) ? null : config.MessageText.Trim()
        };
        return input with
        {
            CourseId = input.CourseId.Trim(),
            Name = input.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            ScheduleType = input.ScheduleType.Trim().ToLowerInvariant(),
            ConditionType = input.ConditionType.Trim().ToLowerInvariant(),
            ActionType = input.ActionType.Trim().ToLowerInvariant(),
            Config = config
        };
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AutomationDefinitionSummary Map(AutomationDefinitionEntity entity) => new(
        entity.Id,
        entity.OwnerId,
        entity.ConnectionAlias,
        entity.CourseId,
        entity.Name,
        entity.Description,
        entity.ScheduleType,
        entity.RunHourUtc,
        entity.RunMinuteUtc,
        entity.RunDayOfWeek,
        entity.ConditionType,
        entity.ActionType,
        AutomationConfigSerializer.Deserialize(entity.ConfigJson),
        entity.IsEnabled,
        entity.NextRunAt,
        entity.LastRunAt,
        entity.CreatedAt,
        entity.UpdatedAt);

    private static AutomationRunSummary Skipped(
        AutomationDefinitionEntity definition,
        string code,
        string message,
        DateTimeOffset scheduledFor) => new(
        Guid.Empty,
        definition.Id,
        "skipped",
        "manual",
        0,
        0,
        0,
        0,
        [],
        code,
        message,
        scheduledFor,
        null,
        null);

    private static AutomationRunSummary ToSummary(
        AutomationRunEntity run,
        IReadOnlyList<Guid> pendingActionIds,
        int createdActions,
        int skippedActions,
        int failedActions,
        string[] _) => new(
        run.Id,
        run.AutomationId,
        run.Status,
        run.Trigger,
        run.AttemptCount,
        createdActions,
        skippedActions,
        failedActions,
        pendingActionIds,
        run.ErrorCode,
        run.ErrorMessage,
        run.ScheduledFor,
        run.StartedAt,
        run.FinishedAt,
        run.SummaryJson);

    private static (int Created, int Skipped, int Failed) ReadSummaryCounts(string? summaryJson)
    {
        if (string.IsNullOrWhiteSpace(summaryJson)) return (0, 0, 0);
        try
        {
            using var document = JsonDocument.Parse(summaryJson);
            var root = document.RootElement;
            return (
                root.TryGetProperty("createdActions", out var created) ? created.GetInt32() : 0,
                root.TryGetProperty("skippedActions", out var skipped) ? skipped.GetInt32() : 0,
                root.TryGetProperty("failedActions", out var failed) ? failed.GetInt32() : 0);
        }
        catch (JsonException)
        {
            return (0, 0, 0);
        }
    }

    private sealed record AutomationCandidate(
        string TargetRef,
        string? StudentId,
        string? StudentName,
        string? AssignmentId,
        string? AssignmentName,
        DateTimeOffset? DueDate,
        bool IsOverdue,
        IReadOnlyList<string> Factors);
}
