using MediatR;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;
using MoodleConnector.Application.Registry;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Presentation.Endpoints;

/// <summary>
/// Revisão assistida e confirmação de lançamentos de notas do Portal.
/// </summary>
internal static class PortalGradingEndpoints
{
    public static void MapGrading(WebApplication app, string rateLimitPolicy)
    {
        app.MapGet("/api/grading/batches", async (
            HttpContext context,
            ConnectorDbContext dbContext,
            IGradingReviewRepository gradingRepository,
            CancellationToken cancellationToken) =>
        {
            if (!HasAppPermission(context, AppPermissionCatalog.GradingView)) return Results.Forbid();
            var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
            if (identity is null) return Results.Unauthorized();

            var batches = await gradingRepository.ListBatchesByCreatorAsync(
                identity.Id.ToString(), cancellationToken);

            return Results.Ok(batches.Select(b => new
            {
                batchJobId = b.Id,
                status = b.Status.ToString(),
                courseId = b.CourseId,
                totalItems = b.TotalItems,
                processedItems = b.ProcessedItems,
                readyItems = b.ReadyItems,
                blockedItems = b.BlockedItems,
                failedItems = b.FailedItems,
                createdAt = b.CreatedAt
            }).ToArray());
        }).RequireRateLimiting(rateLimitPolicy);

        app.MapGet("/api/grading/batches/{id:guid}", async (
            Guid id,
            HttpContext context,
            ConnectorDbContext dbContext,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (!HasAppPermission(context, AppPermissionCatalog.GradingView)) return Results.Forbid();
            var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
            if (identity is null) return Results.Unauthorized();

            try
            {
                var result = await mediator.Send(
                    new GetAssistedGradingBatchStatusQuery(id, 1, 100), cancellationToken);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, error = ex.Message });
            }
        }).RequireRateLimiting(rateLimitPolicy);

        app.MapGet("/api/grading/items/{id:guid}", async (
            Guid id,
            HttpContext context,
            ConnectorDbContext dbContext,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (!HasAppPermission(context, AppPermissionCatalog.GradingView)) return Results.Forbid();
            var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
            if (identity is null) return Results.Unauthorized();

            try
            {
                var result = await mediator.Send(
                    new GetAssistedGradingItemQuery(id), cancellationToken);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, error = ex.Message });
            }
        }).RequireRateLimiting(rateLimitPolicy);

        app.MapPut("/api/grading/items/{id:guid}/review", async (
            Guid id,
            ReviewGradingItemInput input,
            HttpContext context,
            ConnectorDbContext dbContext,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (!HasAppPermission(context, AppPermissionCatalog.GradingManage)) return Results.Forbid();
            var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
            if (identity is null) return Results.Unauthorized();
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);

            try
            {
                var result = await mediator.Send(
                    new UpdateAssistedGradingDraftCommand(
                        id,
                        input.FinalGrade,
                        input.FinalFeedback ?? "",
                        input.TeacherDecision ?? "approved",
                        input.ReviewNotes,
                        input.ExpectedReviewStatus ?? "NotReviewed",
                        input.ExpectedDraftVersionHash),
                    cancellationToken);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, error = ex.Message });
            }
        }).RequireRateLimiting(rateLimitPolicy);

        app.MapPost("/api/grading/batches/{id:guid}/preview", async (
            Guid id,
            PreviewGradingBatchInput input,
            HttpContext context,
            ConnectorDbContext dbContext,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (!HasAppPermission(context, AppPermissionCatalog.GradingManage)) return Results.Forbid();
            var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
            if (identity is null) return Results.Unauthorized();
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);

            try
            {
                var result = await mediator.Send(
                    new CreateGradingLaunchPreviewCommand(
                        id,
                        input.GradingItemIds ?? [],
                        input.OnlyReviewed,
                        input.AllowOverwriteExisting),
                    cancellationToken);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, error = ex.Message });
            }
        }).RequireRateLimiting(rateLimitPolicy);

        app.MapPost("/api/grading/batches/{id:guid}/confirm", async (
            Guid id,
            ConfirmGradingBatchInput input,
            HttpContext context,
            ConnectorDbContext dbContext,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (!HasAppPermission(context, AppPermissionCatalog.GradingManage)) return Results.Forbid();
            var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
            if (identity is null) return Results.Unauthorized();
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);

            if (input.PendingActionId == Guid.Empty || string.IsNullOrWhiteSpace(input.ConfirmationText))
            {
                return Results.BadRequest(new { ok = false, error = "Informe pendingActionId e confirmationText." });
            }

            try
            {
                var result = await mediator.Send(
                    new ConfirmMoodleBatchLaunchCommand(
                        input.PendingActionId,
                        input.ConfirmationText),
                    cancellationToken);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, error = ex.Message });
            }
        }).RequireRateLimiting(rateLimitPolicy);

        app.MapPost("/api/grading/individual/prepare", async (
            PrepareIndividualGradeInput input,
            HttpContext context,
            ConnectorDbContext dbContext,
            IConnectionRegistry connectionRegistry,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (!HasAppPermission(context, AppPermissionCatalog.GradingManage)) return Results.Forbid();
            var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
            if (identity is null) return Results.Unauthorized();
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
            var resolved = await connectionRegistry.ResolveConnectionAsync(input.ConnectionRef, cancellationToken);
            if (resolved is null) return AppErrorResults.NotFound("connection_not_found", "Conexão Moodle não encontrada.");

            try
            {
                var result = await mediator.Send(new PrepareIndividualGradeCommand(
                    input.CourseId,
                    input.AssignmentId,
                    input.StudentId,
                    input.ProposedGrade,
                    input.FeedbackText,
                    input.JustificationText), cancellationToken);
                return Results.Ok(new AppEnvelope<IndividualGradePrepareResult>(result, new(DateTimeOffset.UtcNow, input.ConnectionRef ?? resolved.Alias)));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { ok = false, error = new { code = "invalid_grade_request", message = ex.Message } });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { ok = false, error = new { code = "grade_prepare_failed", message = ex.Message } });
            }
        }).RequireRateLimiting(rateLimitPolicy);

        app.MapPost("/api/grading/individual/confirm", async (
            ConfirmIndividualGradeInput input,
            HttpContext context,
            ConnectorDbContext dbContext,
            IConnectionRegistry connectionRegistry,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (!HasAppPermission(context, AppPermissionCatalog.GradingManage)) return Results.Forbid();
            var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
            if (identity is null) return Results.Unauthorized();
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
            if (input.PendingActionId == Guid.Empty || string.IsNullOrWhiteSpace(input.ConfirmationText))
                return Results.BadRequest(new { ok = false, error = new { code = "invalid_confirmation", message = "A ação pendente e o texto exato de confirmação são obrigatórios." } });
            var resolved = await connectionRegistry.ResolveConnectionAsync(input.ConnectionRef, cancellationToken);
            if (resolved is null) return AppErrorResults.NotFound("connection_not_found", "Conexão Moodle não encontrada.");

            try
            {
                var result = await mediator.Send(new ConfirmIndividualGradeCommand(input.PendingActionId, input.ConfirmationText), cancellationToken);
                return Results.Ok(new AppEnvelope<IndividualGradeSendResult>(result, new(DateTimeOffset.UtcNow, input.ConnectionRef ?? resolved.Alias)));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { ok = false, error = new { code = "grade_confirm_failed", message = ex.Message } });
            }
        }).RequireRateLimiting(rateLimitPolicy);

    }

    private static Task<AppIdentity?> ResolveAppIdentityAsync(
        HttpContext context,
        ConnectorDbContext dbContext,
        CancellationToken cancellationToken) =>
        PortalEndpointAuthorization.ResolveAppIdentityAsync(context, dbContext, cancellationToken);

    private static bool HasAppPermission(HttpContext context, string permission) =>
        PortalEndpointAuthorization.HasAppPermission(context, permission);
}
