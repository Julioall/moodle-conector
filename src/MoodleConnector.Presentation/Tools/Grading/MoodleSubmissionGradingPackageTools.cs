using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.Grading;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Application.Submissions;
using MoodleConnector.Application.Submissions.Queries;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;
using MoodleConnector.Presentation.Configuration;

namespace MoodleConnector.Presentation.Tools.Grading;

[McpServerToolType]
public sealed class MoodleSubmissionGradingPackageTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver,
    IMoodleResourceGateway resourceGateway,
    IMoodleAssignmentSettingsGateway settingsGateway,
    IGradingReviewRepository gradingRepository,
    ICurrentUserContext currentUser,
    IOptions<MoodleUniversalApiFeatureOptions> features,
    IOptions<GradingLimitsOptions> limits,
    IGradingOperationTelemetry? telemetry = null)
{
    [MoodleToolMetadata(
        Family = "assignments",
        Classification = "R4",
        Kind = "specialized",
        CanonicalOperation = "assignments.submissions.get_grading_package",
        ExposureReason = "Provides pedagogical context and opaque MCP links; no extracted attachment text or Moodle write is returned.",
        RequiredMoodleCapabilities = "mod_assign_get_submissions")]
    [McpServerTool(Name = "get_submission_grading_package", Title = "Get Submission Grading Package", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolResponse<SubmissionGradingPackage>))]
    [Description("Retorna contexto pedagogico e resource_links opacos dos anexos de uma submissao. Nao inclui Base64, texto extraido, URL privada, token nem ferramenta de escrita Moodle.")]
    public async Task<CallToolResult> GetAsync(
        [Description("Identificador do curso Moodle. Obrigatorio quando submissionId nao estiver informado.")] string? courseId = null,
        [Description("Identificador da atividade Moodle. Obrigatorio quando submissionId nao estiver informado.")] string? assignmentId = null,
        [Description("Identificador do estudante Moodle. Obrigatorio quando submissionId nao estiver informado.")] string? studentId = null,
        [Description("Identificador da submissao. Pode ser usado sozinho quando a submissao ja pertence a um draft autorizado.")] string? submissionId = null,
        [Description("Alias Moodle opcional.")] string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        if (!features.Value.McpResourceSubmissionDeliveryEnabled)
        {
            // Emite métrica de fallback para rastrear o volume de chamadas que
            // ainda dependem do pipeline legado por feature flag desabilitada.
            telemetry?.RecordPhase("legacy_fallback", "submission_delivery", "feature_flag", 0);
            return ToolResultHelper.Error<SubmissionGradingPackage>("A entrega por MCP Resource esta desabilitada. Use o pipeline legado enquanto o rollout nao estiver habilitado.");
        }
        if (string.IsNullOrWhiteSpace(courseId) || string.IsNullOrWhiteSpace(assignmentId) || string.IsNullOrWhiteSpace(studentId))
        {
            if (!long.TryParse(submissionId, out var numericSubmissionId) || numericSubmissionId <= 0)
                return ToolResultHelper.Error<SubmissionGradingPackage>("Informe submissionId ou a combinacao courseId, assignmentId e studentId.");
            var existingItem = await gradingRepository.FindItemBySubmissionAsync(numericSubmissionId, cancellationToken);
            if (existingItem is null) return ToolResultHelper.Error<SubmissionGradingPackage>("submissionId nao encontrado entre os drafts autorizados. Informe curso, atividade e estudante para uma consulta nova.");
            var batch = await gradingRepository.GetBatchAsync(existingItem.BatchId, cancellationToken);
            if (batch is null) return ToolResultHelper.Error<SubmissionGradingPackage>("Acesso negado a submissionId informado.");
            if (!string.Equals(batch.CreatedBySubject, currentUser.Subject, StringComparison.Ordinal) &&
                !currentUser.HasScope("grading.admin") && !currentUser.HasPlatformPermission("tool.assignments.grade"))
                return ToolResultHelper.Error<SubmissionGradingPackage>("Acesso negado a submissionId informado.");
            courseId = existingItem.CourseId.ToString();
            assignmentId = existingItem.AssignmentId.ToString();
            studentId = existingItem.MoodleUserId.ToString();
        }
        moodleSelection.Alias = moodleAlias;
        var actorMoodleId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (actorMoodleId is null) return ToolResultHelper.Error<SubmissionGradingPackage>("Usuario nao autenticado para consultar a submissao.");
        try
        {
            var page = await mediator.Send(new ListAssignmentSubmissionsQuery(actorMoodleId.Value.ToString(), courseId!, assignmentId!, AssignmentSubmissionFilter.All, 1, 100, null, null, true, true), cancellationToken);
            if (page is null) return ToolResultHelper.Error<SubmissionGradingPackage>("Nao foi possivel carregar as entregas da atividade.");
            var submission = page.Submissions.SingleOrDefault(item => string.Equals(item.UserId, studentId, StringComparison.Ordinal));
            if (submission is null) return ToolResultHelper.Error<SubmissionGradingPackage>("Submissao do estudante nao encontrada no curso e atividade informados.");
            if (!string.IsNullOrWhiteSpace(submissionId) && !string.Equals(submission.SubmissionId, submissionId, StringComparison.Ordinal)) return ToolResultHelper.Error<SubmissionGradingPackage>("O submissionId informado nao corresponde ao estudante e atividade.");
            if ((submission.Files?.Count ?? 0) > Math.Max(1, limits.Value.MaxFilesPerSubmission)) return ToolResultHelper.Error<SubmissionGradingPackage>("A submissao excede o limite configurado de arquivos.");
            var settings = await settingsGateway.GetAssignmentSettingsAsync(actorMoodleId.Value.ToString(), courseId!, assignmentId!, cancellationToken);
            var attachments = new List<SubmissionResourceLink>();
            // Acumula sha256 de cada resource final (após expansão de ZIP) para o hash da submissão.
            // O SHA-256 aqui é o registrado via Moodle no RegisterAsync; o hash definitivo do binário
            // é confirmado no resources/read e propagado ao draft via SubmissionContentHash.
            var attachmentSha256s = new List<string>();
            foreach (var file in submission.Files ?? [])
            {
                MoodleResourceDescriptor descriptor;
                try
                {
                    descriptor = await resourceGateway.RegisterAsync(new MoodleResourceRegistration("submission_attachment", file.Filename, file.MimeType ?? "application/octet-stream", file.FileUrl, ToLong(courseId), ToLong(assignmentId), ToLong(submission.SubmissionId), ToLong(studentId), SizeBytes: file.SizeBytes), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    telemetry?.RecordPhase("legacy_fallback", "submission_delivery", "resource_failure", 0);
                    throw;
                }
                var resources = IsZip(descriptor.MimeType) && features.Value.McpResourceZipEnabled
                    ? await resourceGateway.ExpandZipAsync(descriptor.Uri, cancellationToken)
                    : [descriptor];
                foreach (var resolved in resources)
                {
                    attachments.Add(new SubmissionResourceLink(resolved.Uri, resolved.Filename, resolved.MimeType, resolved.SizeBytes));
                    // SHA-256 pode ser null quando o Moodle não devolve hash na listagem;
                    // o hash é confirmado definitivamente no resources/read.
                    attachmentSha256s.Add(resolved.Sha256 ?? string.Empty);
                }
            }
            var warnings = new List<string>();
            if (attachments.Count == 0) warnings.Add("A submissao nao possui anexos entregues por MCP Resource.");
            if (submission.EvaluationState != SubmissionEvaluationState.AwaitingGrading) warnings.Add($"Estado pedagogico atual: {submission.EvaluationState}.");
            // Calcula o hash da submissão com o material disponível no momento do pacote.
            // Quando os sha256 dos arquivos estiverem ausentes (Moodle não devolveu hash na listagem),
            // o hash cobre apenas metadados da submissão; a integridade binária é selada no resources/read.
            string? contentHash = SubmissionContentHash.Compute(
                attachmentSha256s,
                submission.OnlineText,
                submission.AttemptNumber,
                submission.ModifiedAt);
            var missingBinaryHashes = attachmentSha256s.Any(h => string.IsNullOrEmpty(h));
            if (missingBinaryHashes)
                warnings.Add("submissionContentHash calculado sem SHA-256 binario dos arquivos; sera revalidado no resources/read.");
            var package = new SubmissionGradingPackage(courseId, assignmentId, studentId, submission.SubmissionId, submission.Status, submission.EvaluationState.ToString(), submission.SubmittedAt, submission.ModifiedAt, submission.AttemptNumber, submission.OnlineText, contentHash, settings?.Name, settings?.Description, settings?.MaxGrade, settings?.IsGradable, attachments, warnings);
            var response = new ToolResponse<SubmissionGradingPackage>("ok", package, warnings, null, DateTimeOffset.UtcNow);
            var content = new List<ContentBlock> { new TextContentBlock { Text = $"Pacote de correcao preparado para a submissao {submission.SubmissionId ?? "sem id"}: {attachments.Count} anexo(s) disponivel(is) por MCP Resource." } };
            content.AddRange(attachments.Select(link => new ResourceLinkBlock { Uri = link.Uri, Name = link.Name, MimeType = link.MimeType, Size = link.Size }));
            return new CallToolResult { Content = content, StructuredContent = JsonSerializer.SerializeToElement(response), IsError = false };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { return ToolResultHelper.Error<SubmissionGradingPackage>(exception); }
    }

    private static long? ToLong(string? value) => long.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;
    private static bool IsZip(string mimeType) => mimeType.Equals("application/zip", StringComparison.OrdinalIgnoreCase) || mimeType.Equals("application/x-zip-compressed", StringComparison.OrdinalIgnoreCase);
}

public sealed record SubmissionResourceLink([property: JsonPropertyName("uri")] string Uri, [property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("mimeType")] string MimeType, [property: JsonPropertyName("size")] long? Size);
public sealed record SubmissionGradingPackage([property: JsonPropertyName("courseId")] string CourseId, [property: JsonPropertyName("assignmentId")] string AssignmentId, [property: JsonPropertyName("studentId")] string StudentId, [property: JsonPropertyName("submissionId")] string? SubmissionId, [property: JsonPropertyName("status")] string Status, [property: JsonPropertyName("evaluationState")] string EvaluationState, [property: JsonPropertyName("submittedAt")] DateTimeOffset? SubmittedAt, [property: JsonPropertyName("modifiedAt")] DateTimeOffset? ModifiedAt, [property: JsonPropertyName("attemptNumber")] int? AttemptNumber, [property: JsonPropertyName("onlineText")] string? OnlineText, [property: JsonPropertyName("submissionContentHash")] string? SubmissionContentHash, [property: JsonPropertyName("assignmentName")] string? AssignmentName, [property: JsonPropertyName("assignmentStatement")] string? AssignmentStatement, [property: JsonPropertyName("maxGrade")] decimal? MaxGrade, [property: JsonPropertyName("isGradable")] bool? IsGradable, [property: JsonPropertyName("attachments")] IReadOnlyList<SubmissionResourceLink> Attachments, [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings);
