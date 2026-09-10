using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.Grading;
using MoodleConnector.Application.Tools;
using MoodleConnector.Presentation.Tools;
using MoodleConnector.Presentation.Configuration;

namespace MoodleConnector.Presentation.Tools.Grading;

public sealed record GradingCorrectionsCsvExportResult(
    [property: JsonPropertyName("batchJobId")] Guid BatchJobId,
    [property: JsonPropertyName("generatedAt")] DateTimeOffset GeneratedAt,
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("contentType")] string ContentType,
    [property: JsonPropertyName("fileSizeBytes")] long FileSizeBytes,
    [property: JsonPropertyName("totalItems")] int TotalItems,
    [property: JsonPropertyName("generatedItems")] int GeneratedItems,
    [property: JsonPropertyName("pendingItems")] int PendingItems,
    [property: JsonPropertyName("blockedItems")] int BlockedItems,
    [property: JsonPropertyName("columns")] IReadOnlyList<string> Columns);

[McpServerToolType]
public sealed class MoodleGradingTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver,
    MoodleSnapshotToolContext? snapshotContext = null,
    IOptions<GradingLimitsOptions>? gradingLimits = null,
    IMoodleConnectorCredentialsProvider? credentialsProvider = null)
{
    private readonly GradingLimitsOptions _gradingLimits = gradingLimits?.Value ?? new GradingLimitsOptions();

    [McpServerTool(
        Name = "start_pending_grading_run",
        Title = "Start Pending Grading Run",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<StartPendingGradingRunResult>))]
    [Description("Inicia o fluxo de correcao de entregas pendentes. Quando courseId for informado, limita toda a descoberta e os sublotes a esse curso; quando omitido, percorre os cursos acessiveis. Nao escreve no Moodle. Use o gradingRunId retornado para paginar, preparar e salvar todos os sublotes; batchJobIds continuam disponiveis para compatibilidade. Em seguida escolha um destino: export_grading_corrections_csv para CSV externo ou create_batch_grade_launch_preview para revisar a publicacao no Moodle. Se o usuario tiver pedido explicitamente uma reavaliacao, use includeAlreadyGraded=true para incluir submissaoes ja corrigidas; a sobrescrita no Moodle continua exigindo allowOverwriteExisting=true na previa e CONFIRMAR_PUBLICACAO.")]
    public Task<CallToolResult> IniciarFluxoCorrecaoPendentesAsync(
        [Description("Identificador opcional do curso a processar. Quando informado, nenhum outro curso e consultado.")]
        string? courseId = null,
        [Description("IDs opcionais das atividades a processar dentro do curso. Aceita instanceId ou cmid; quando omitido, inclui todas as atividades avaliativas.")]
        string[]? assignmentIds = null,
        [Description("Numero maximo de cursos a percorrer. Use 0 para todos os cursos acessiveis.")]
        int maxCourses = 0,
        [Description("Numero maximo de entregas por sublote, de 1 a 400. A ferramenta cria sublotes adicionais ate percorrer todas as entregas pendentes.")]
        int maxItemsPerBatch = 400,
        [Description("Quando true, inclui contexto de rubrica na montagem dos sublotes.")]
        bool includeRubric = true,
        [Description("Quando true, baixa e extrai arquivos das entregas.")]
        bool includeSubmissionFiles = true,
        [Description("Quando true, inclui materiais auxiliares do curso. Mesmo quando false, o conector procura automaticamente arquivos de enunciado cujo nome/número corresponda à atividade; materiais genéricos só entram quando esta opção está ativa.")]
        bool includeCourseMaterials = false,
        [Description("Instrucoes adicionais do professor/tutor para orientar a correcao.")]
        string? teacherInstructions = null,
        [Description("Prioridade sugerida: low, normal ou high.")]
        string priority = "normal",
        [Description("Use true somente quando o usuario pedir explicitamente para reavaliar correcoes ja publicadas. Consulta mod_assign_get_submissions sem filtrar gradingstatus e cria novos itens de correcao para as submissaoes entregues, sem alterar o Moodle nesta etapa.")]
        bool includeAlreadyGraded = false,
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrao do usuario.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return StartPendingGradingRunCoreAsync(
            courseId,
            maxCourses,
            maxItemsPerBatch,
            includeRubric,
            includeSubmissionFiles,
            includeCourseMaterials,
            teacherInstructions,
            priority,
            includeAlreadyGraded,
            moodleAlias,
            assignmentIds,
            cancellationToken);
    }

    [McpServerTool(
        Name = "requeue_blocked_grading_items",
        Title = "Requeue Blocked Grading Items",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<RequeueBlockedGradingItemsResult>))]
    [Description("Reabre somente os itens de analise informados que estao bloqueados ou falharam, devolvendo-os para a IA. Nao altera o Moodle, nao reabre itens publicados nem itens com revisao final; exige os gradingItemIds explicitamente. Para falha de publicacao de uma revisao final, use requeue_failed_grading_publication_items.")]
    public async Task<CallToolResult> ReenfileirarItensBloqueadosAsync(
        [Description("Identificador do lote original ou gradingRunId agregado.")]
        Guid batchJobId,
        [Description("IDs dos itens bloqueados a reprocessar; nunca informe itens ja publicados.")]
        Guid[] gradingItemIds,
        CancellationToken cancellationToken = default)
    {
        if (batchJobId == Guid.Empty)
        {
            return ToolResultHelper.Error<RequeueBlockedGradingItemsResult>("Informe um identificador de lote valido.");
        }

        if (gradingItemIds.Length == 0)
        {
            return ToolResultHelper.Error<RequeueBlockedGradingItemsResult>("Informe pelo menos um gradingItemId para reprocessar.");
        }

        RequeueBlockedGradingItemsResult data;
        try
        {
            data = await mediator.Send(
                new RequeueBlockedGradingItemsCommand(batchJobId, gradingItemIds),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            return ToolResultHelper.Error<RequeueBlockedGradingItemsResult>(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ToolResultHelper.Error<RequeueBlockedGradingItemsResult>(ex.Message);
        }
        catch
        {
            return ToolResultHelper.Error<RequeueBlockedGradingItemsResult>("Nao foi possivel reabrir os itens bloqueados neste momento.");
        }

        var response = new ToolResponse<RequeueBlockedGradingItemsResult>(
            data.RequeuedItems > 0 || data.AlreadyQueuedItems > 0 ? "ok" : "partial_failure",
            data,
            data.Failures.Select(failure => failure.Message).ToArray(),
            AuditId: null,
            DateTimeOffset.UtcNow);
        return new CallToolResult
        {
            Content = [new TextContentBlock
            {
                Text = $"{data.RequeuedItems} item(ns) reaberto(s), {data.AlreadyQueuedItems} ja aguardando IA e {data.FailedItems} com falha."
            }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    [McpServerTool(
        Name = "find_grading_correction_by_submission",
        Title = "Find Grading Correction By Submission",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<FindGradingCorrectionBySubmissionResult>))]
    [Description("Localiza somente no ledger local do conector uma correcao anterior pela submissao Moodle. Retorna gradingItemId, batchJobId e gradingRunId quando o lote pertence ao usuario atual; nao consulta nem altera o Moodle. Use esta ferramenta quando uma nova tentativa for barrada por duplicidade e o identificador antigo nao estiver disponivel.")]
    public async Task<CallToolResult> LocalizarCorrecaoPorSubmissaoAsync(
        [Description("submissionId numerico informado pelo Moodle.")]
        string submissionId,
        [Description("courseId opcional para desempatar resultados.")]
        string? courseId = null,
        [Description("assignmentId ou instanceId opcional para desempatar resultados.")]
        string? assignmentId = null,
        [Description("Alias do Moodle onde a submissao foi processada.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(submissionId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSubmissionId) ||
            parsedSubmissionId <= 0)
        {
            return ToolResultHelper.Error<FindGradingCorrectionBySubmissionResult>("Informe um submissionId numerico positivo.");
        }

        var courseIsValid = TryParseOptionalPositiveId(courseId, "courseId", out var parsedCourseId, out var courseError);
        var assignmentIsValid = TryParseOptionalPositiveId(assignmentId, "assignmentId", out var parsedAssignmentId, out var assignmentError);
        if (!courseIsValid || !assignmentIsValid)
        {
            return ToolResultHelper.Error<FindGradingCorrectionBySubmissionResult>(courseError ?? assignmentError!);
        }

        moodleSelection.Alias = moodleAlias;
        MoodleConnectorCredentials? credentials = null;
        if (credentialsProvider is not null)
        {
            try
            {
                credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // A local lookup remains useful even when the current
                // connection cannot be resolved; the handler still scopes by
                // the authenticated connector subject.
            }
        }

        try
        {
            var data = await mediator.Send(
                new FindGradingCorrectionBySubmissionQuery(
                    parsedSubmissionId,
                    parsedCourseId,
                    parsedAssignmentId,
                    credentials?.ConnectionId,
                    credentials?.ClientId,
                    credentials?.Alias),
                cancellationToken);
            var response = new ToolResponse<FindGradingCorrectionBySubmissionResult>(
                data.Found ? "ok" : "not_found",
                data,
                [],
                AuditId: null,
                DateTimeOffset.UtcNow);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = data.Message }],
                StructuredContent = JsonSerializer.SerializeToElement(response),
                IsError = false
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            return ToolResultHelper.Error<FindGradingCorrectionBySubmissionResult>(ex.Message);
        }
        catch
        {
            return ToolResultHelper.Error<FindGradingCorrectionBySubmissionResult>("Nao foi possivel consultar o ledger de correcoes neste momento.");
        }
    }

    [McpServerTool(
        Name = "requeue_failed_grading_publication_items",
        Title = "Requeue Failed Grading Publication Items",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<RequeueFailedGradingPublicationItemsResult>))]
    [Description("Recupera itens revisados cuja publicacao no Moodle falhou de forma comprovada. Mantem nota e feedback finais, recusa execucao desconhecida e nao escreve no Moodle; depois gere uma nova previa e confirme novamente. Use find_grading_correction_by_submission quando voce so tiver o submissionId.")]
    public async Task<CallToolResult> ReenfileirarFalhasDePublicacaoAsync(
        [Description("batchJobId ou gradingRunId que contem os itens.")]
        Guid batchJobId,
        [Description("IDs dos itens cuja publicacao falhou; informe-os explicitamente.")]
        Guid[] gradingItemIds,
        CancellationToken cancellationToken = default)
    {
        if (batchJobId == Guid.Empty || gradingItemIds.Length == 0)
        {
            return ToolResultHelper.Error<RequeueFailedGradingPublicationItemsResult>("Informe um lote valido e pelo menos um gradingItemId.");
        }

        try
        {
            var data = await mediator.Send(
                new RequeueFailedGradingPublicationItemsCommand(batchJobId, gradingItemIds),
                cancellationToken);
            var response = new ToolResponse<RequeueFailedGradingPublicationItemsResult>(
                data.RequeuedItems > 0 || data.AlreadyQueuedItems > 0 ? "ok" : "partial_failure",
                data,
                data.Failures.Select(failure => failure.Message).ToArray(),
                AuditId: null,
                DateTimeOffset.UtcNow);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = data.NextStep }],
                StructuredContent = JsonSerializer.SerializeToElement(response),
                IsError = false
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            return ToolResultHelper.Error<RequeueFailedGradingPublicationItemsResult>(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ToolResultHelper.Error<RequeueFailedGradingPublicationItemsResult>(ex.Message);
        }
        catch
        {
            return ToolResultHelper.Error<RequeueFailedGradingPublicationItemsResult>("Nao foi possivel recuperar as falhas de publicacao neste momento.");
        }
    }

    [McpServerTool(
        Name = "cancel_assisted_grading_batch",
        Title = "Cancel Assisted Grading Batch",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<CancelAssistedGradingBatchResult>))]
    [MoodleToolMetadata(
        Family = "grading",
        Classification = "R3",
        Kind = "destructive-write",
        CanonicalOperation = "grading.cancel_local_batch",
        RequiredPlatformPermission = "tool.assignments.grade",
        Evidence = "Cancela uma execucao local e, somente com confirmacao literal, remove a projeção local de lotes que nunca foram publicados; nunca escreve nem apaga dados no Moodle.")]
    [Description("Cancela uma execucao local de correcao para impedir novos workers e publicacoes pendentes. Por padrao preserva o historico. Para remover os dados locais de uma execucao que nao possui itens publicados, use purgeLocalData=true e informe confirmationText exatamente como CANCELAR_CORRECOES_LOCAIS. Itens publicados, escritas de resultado desconhecido, leases ativos e publicacoes pendentes bloqueiam o expurgo; o Moodle nunca e alterado por esta ferramenta." )]
    public async Task<CallToolResult> CancelarLoteCorrecaoAsync(
        [Description("Identificador do batchJobId legado ou do gradingRunId agregado retornado por start_pending_grading_run.")]
        Guid batchJobId,
        [Description("Quando true, remove a projecao local depois dos guardas de seguranca. O padrao e false e apenas cancela.")]
        bool purgeLocalData = false,
        [Description("Obrigatorio quando purgeLocalData=true: CANCELAR_CORRECOES_LOCAIS.")]
        string? confirmationText = null,
        [Description("Motivo operacional para a auditoria local da solicitacao.")]
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (batchJobId == Guid.Empty)
        {
            return ToolResultHelper.Error<CancelAssistedGradingBatchResult>("Informe um identificador de lote ou execucao valido.");
        }

        CancelAssistedGradingBatchResult data;
        try
        {
            data = await mediator.Send(
                new CancelAssistedGradingBatchCommand(batchJobId, purgeLocalData, confirmationText, reason),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            return ToolResultHelper.Error<CancelAssistedGradingBatchResult>(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ToolResultHelper.Error<CancelAssistedGradingBatchResult>(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return ToolResultHelper.Error<CancelAssistedGradingBatchResult>(ex.Message);
        }
        catch
        {
            return ToolResultHelper.Error<CancelAssistedGradingBatchResult>("Nao foi possivel cancelar a execucao local neste momento.");
        }

        var response = new ToolResponse<CancelAssistedGradingBatchResult>(
            data.Purged ? "ok" : "cancelled",
            data,
            data.Warnings ?? [],
            AuditId: null,
            DateTimeOffset.UtcNow);
        var text = data.Purged
            ? $"Execucao cancelada e removida localmente: {data.DeletedItems} item(ns), {data.DeletedBatches} sublote(s) e {data.DeletedRuns} execucao(oes). Nenhuma alteracao foi feita no Moodle."
            : data.Message;
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    [McpServerTool(
        Name = "cancel_grading_batch",
        Title = "Cancel Grading Batch",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<CancelAssistedGradingBatchResult>))]
    [Description("Alias de compatibilidade para cancel_assisted_grading_batch. Aceita batchJobId ou gradingRunId, cancela o processamento local e preserva os guardas contra publicacoes duplicadas.")]
    public Task<CallToolResult> CancelarLoteCorrecaoCompatAsync(
        [Description("batchJobId ou gradingRunId retornado pelo conector.")]
        Guid batchJobId,
        CancellationToken cancellationToken = default)
    {
        return CancelarLoteCorrecaoAsync(batchJobId, false, null, null, cancellationToken);
    }

    [McpServerTool(
        Name = "create_batch_grade_launch_preview",
        Title = "Create Batch Grade Launch Preview",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<CreateGradingLaunchPreviewResult>))]
    [Description("Prepara uma unica previa revisavel para publicar as correcoes salvas no Moodle. Aceita tanto rascunhos de IA quanto correcoes ja revisadas, sem escrever no Moodle. A resposta lista aluno, nota, feedback e avisos; so confirm_batch_grade_launch pode efetivar o envio.")]
    public Task<CallToolResult> CriarPreviaLancamentoLoteAsync(
        [Description("Identificador do lote de correcao assistida ou do gradingRunId agregado retornado por start_pending_grading_run.")]
        Guid batchJobId,
        [Description("Itens especificos a incluir. Quando vazio, inclui todos os itens prontos do lote.")]
        Guid[]? gradingItemIds = null,
        [Description("Quando true, inclui somente itens ja revisados. O padrao inclui rascunhos salvos para que a confirmacao humana seja a revisao final antes da publicacao.")]
        bool onlyReviewed = false,
        [Description("Quando true, a confirmacao autoriza explicitamente sobrescrever notas e feedbacks que ja existem no Moodle.")]
        bool allowOverwriteExisting = false,
        [Description("Quando true, permite criar previa global mesmo se a cobertura declarada do gradingRun estiver incompleta. Use somente apos revisar expectedItems, preparedItems, missingItems e missingBatchCount.")]
        bool allowPartial = false,
        CancellationToken cancellationToken = default)
    {
        return CreateLaunchPreviewCoreAsync(
            batchJobId,
            gradingItemIds ?? [],
            onlyReviewed,
            allowOverwriteExisting,
            allowPartial,
            cancellationToken);
    }

    [McpServerTool(
        Name = "confirm_batch_grade_launch",
        Title = "Confirm Batch Grade Launch",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ConfirmMoodleBatchLaunchResult>))]
    [Description("Confirma uma previa pendente e autoriza a publicacao duravel usando o texto literal de confirmacao. A chamada nao espera os writes: um worker recuperavel revalida versao do rascunho, submissao, notas existentes e tentativa antes de cada escrita; itens inseguros nao sao sobrescritos.")]
    public Task<CallToolResult> ConfirmarLancamentoLoteMoodleAsync(
        [Description("Identificador da acao pendente retornada por criar_previa_lancamento_lote.")]
        Guid pendingActionId,
        [Description("Texto exato de confirmacao retornado na previa.")]
        string confirmationText,
        CancellationToken cancellationToken = default)
    {
        return ConfirmLaunchCoreAsync(pendingActionId, confirmationText, cancellationToken);
    }

    // ============================================================
    // Tool: prepare_ai_grading_batch
    // ============================================================

    [McpServerTool(
        Name = "prepare_ai_grading_batch",
        Title = "Prepare AI Grading Batch",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<AiGradingBatchPackageResult>))]
    [Description("Retorna uma pagina do pacote estruturado de uma correcao via IA: criterios, nota maxima, resources individuais da submissao e assignments[].contextResources compartilhados por atividade. Cada item aponta os materiais aplicaveis em contextResourceRefs; eles nao se repetem por aluno. Resources de submissao informam artifactId e devem fundamentar proposal.evidence. O identificador aceita um batchJobId legado ou o gradingRunId agregado; use page/nextPage para percorrer ate 10.000 itens sem carregar tudo na resposta. totalItems e eligibleItems representam somente itens ainda aptos para analise da IA; itemsOnPage informa quantos objetos existem efetivamente em items; itemsExcludedByStatus informa itens ja salvos ou em outro status que nao serao reprocessados. Para validar cobertura total, compare expectedItems com preparedItems e confirme missingItems=0, sem exigir que totalItems seja igual a expectedItems. Leia os resources antes de gerar nota e feedback. Se a entrega for de outra atividade, gere nota 0 quando houver escala e feedback especifico explicando a incompatibilidade, salvando como rascunho para revisao. Ao salvar, copie todas e somente as URIs de submission resources para proposal.resourceUris; contextResources podem ser citados apenas nas evidencias. Depois use save_ai_grading_batch e escolha um destino: export_grading_corrections_csv se o usuario pediu CSV ou create_batch_grade_launch_preview para revisar a publicacao no Moodle. Nao escreve no Moodle.")]
    public async Task<CallToolResult> PrepararLoteCorrecaoIaAsync(
        [Description("Identificador retornado por start_pending_grading_run: pode ser batchJobId (compatibilidade) ou gradingRunId (recomendado para consolidar todos os sublotes).")]
        Guid batchJobId,
        [Description("Pagina iniciando em 1. Use nextPage quando hasMore=true.")]
        int page = 1,
        [Description("Itens por pagina, de 1 a 400. O padrao 400 limita memoria e tamanho da resposta para execucoes grandes.")]
        int pageSize = 400,
        CancellationToken cancellationToken = default)
    {
        if (batchJobId == Guid.Empty)
        {
            return ToolResultHelper.Error<AiGradingBatchPackageResult>("Informe um identificador de lote valido.");
        }

        AiGradingBatchPackageResult data;
        try
        {
            data = await mediator.Send(
                new PrepareAiGradingBatchQuery(batchJobId, page, pageSize),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            return ToolResultHelper.Error<AiGradingBatchPackageResult>(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ToolResultHelper.Error<AiGradingBatchPackageResult>(ex.Message);
        }
        catch
        {
            return ToolResultHelper.Error<AiGradingBatchPackageResult>("Nao foi possivel preparar o pacote IA do lote neste momento.");
        }

        var response = new ToolResponse<AiGradingBatchPackageResult>(
            "ok",
            data,
            data.Warnings,
            AuditId: null,
            DateTimeOffset.UtcNow);

        var content = new List<ContentBlock> { new TextContentBlock { Text = BuildPrepareAiBatchNarration(data) } };
        content.AddRange(data.Items.SelectMany(item => item.Resources ?? []).Select(link => new ResourceLinkBlock { Uri = link.Uri, Name = link.Name, MimeType = link.MimeType, Size = link.Size }));
        return new CallToolResult
        {
            Content = content,
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    // ============================================================
    // Tool: save_ai_grading_batch
    // ============================================================

    [McpServerTool(
        Name = "save_ai_grading_batch",
        Title = "Save AI Grading Batch",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<SaveAiGradingBatchResult>))]
    [Description("Salva nota e feedback gerados pela IA como correcoes internas. O identificador aceita batchJobId legado ou gradingRunId agregado e os itens podem vir de qualquer sublote autorizado da execucao. Nao escreve no Moodle. Depois de salvar, escolha somente um destino: export_grading_corrections_csv para gerar CSV externo ou create_batch_grade_launch_preview para revisar uma publicacao no Moodle.")]
    public async Task<CallToolResult> SalvarCorrecoesIaLoteAsync(
        [Description("Identificador retornado por start_pending_grading_run: batchJobId legado ou gradingRunId agregado (recomendado).")]
        Guid batchJobId,
    [Description("Array de correcoes. O formato legado usa gradingItemId, nota e feedback. Quando disponivel, proposal deve conter a versao/hash do contexto, criterios, evidencias, cobertura e feedback estruturado; em evidence use artifactId real do pacote e descreva a observacao. Entregas incompatíveis devem receber nota 0 quando houver escala e feedback explicativo, permanecendo em revisao humana. Para um rascunho MCP que possa gerar previa de lancamento, proposal.resourceUris deve conter todas e somente as URIs do pacote com resourceType 'submission'; nao inclua recursos de contexto. A escala numerica continua sendo validada pelo Moodle.")]
        AiGradingItemInput[] items,
        CancellationToken cancellationToken = default)
    {
        if (batchJobId == Guid.Empty)
        {
            return ToolResultHelper.Error<SaveAiGradingBatchResult>("Informe um identificador de lote valido.");
        }

        if (items.Length == 0)
        {
            return ToolResultHelper.Error<SaveAiGradingBatchResult>("Informe pelo menos um item de correcao.");
        }

        SaveAiGradingBatchResult data;
        try
        {
            data = await mediator.Send(
                new SaveAiGradingBatchCommand(batchJobId, items),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            return ToolResultHelper.Error<SaveAiGradingBatchResult>(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ToolResultHelper.Error<SaveAiGradingBatchResult>(ex.Message);
        }
        catch
        {
            return ToolResultHelper.Error<SaveAiGradingBatchResult>("Nao foi possivel salvar as correcoes IA neste momento.");
        }

        var response = new ToolResponse<SaveAiGradingBatchResult>(
            data.SavedItems > 0 ? "ok" : "partial_failure",
            data,
            data.Warnings,
            AuditId: null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildSaveAiGradingNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            // Falhas de itens são reportadas em warnings/updatedItems para o
            // cliente reconciliar, sem virar RuntimeException no bridge MCP.
            IsError = false
        };
    }

    [McpServerTool(
        Name = "export_grading_corrections_csv",
        Title = "Export Grading Corrections CSV",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GradingCorrectionsCsvExportResult>))]
    [Description("Gera e entrega um CSV UTF-8 com as correcoes locais no formato nome;nota;feedback. O identificador aceita batchJobId legado ou gradingRunId agregado e consolida todos os sublotes autorizados. Use apos save_ai_grading_batch. Esta ferramenta somente le rascunhos locais e nunca confirma nem envia dados ao Moodle.")]
    public async Task<CallToolResult> ExportarCorrecoesCsvAsync(
        [Description("Identificador do lote retornado por start_pending_grading_run.")]
        Guid batchJobId,
        CancellationToken cancellationToken = default)
    {
        if (batchJobId == Guid.Empty)
        {
            return ToolResultHelper.Error<GradingCorrectionsCsvExportResult>("Informe um identificador de lote valido.");
        }

        GradingCorrectionsCsvResult data;
        try
        {
            data = await mediator.Send(
                new GetGradingCorrectionsCsvQuery(batchJobId),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            return ToolResultHelper.Error<GradingCorrectionsCsvExportResult>(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ToolResultHelper.Error<GradingCorrectionsCsvExportResult>(ex.Message);
        }
        catch
        {
            return ToolResultHelper.Error<GradingCorrectionsCsvExportResult>("Nao foi possivel gerar o CSV de correcoes neste momento.");
        }

        var generatedAt = data.GeneratedAt;
        var fileName = $"correcoes_{data.BatchJobId:N}_{generatedAt:yyyyMMdd-HHmmss}.csv";
        var contentType = "text/csv; charset=utf-8";
        var csvBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            .GetBytes(BuildCorrectionsCsv(data.Rows));
        var export = new GradingCorrectionsCsvExportResult(
            data.BatchJobId,
            generatedAt,
            fileName,
            contentType,
            csvBytes.LongLength,
            data.TotalItems,
            data.GeneratedItems,
            data.PendingItems,
            data.BlockedItems,
            ["nome", "nota", "feedback"]);
        var response = new ToolResponse<GradingCorrectionsCsvExportResult>(
            "ok",
            export,
            [],
            AuditId: null,
            DateTimeOffset.UtcNow);
        var resource = BlobResourceContents.FromBytes(
            csvBytes,
            $"mcp://moodle-connector/grading-corrections/{Guid.NewGuid():N}/{fileName}",
            contentType);

        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = $"CSV de correcoes gerado: {fileName} ({data.GeneratedItems} gerada(s), {data.PendingItems} pendente(s), {data.BlockedItems} bloqueada(s))."
                },
                new EmbeddedResourceBlock { Resource = resource }
            ],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }




    private async Task<CallToolResult> StartPendingGradingRunCoreAsync(
        string? courseId,
        int maxCourses,
        int maxItemsPerBatch,
        bool includeRubric,
        bool includeSubmissionFiles,
        bool includeCourseMaterials,
        string? teacherInstructions,
        string priority,
        bool includeAlreadyGraded,
        string? moodleAlias,
        IReadOnlyList<string>? assignmentIds,
        CancellationToken cancellationToken)
    {
        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return ToolResultHelper.Error<StartPendingGradingRunResult>("Usuario nao autenticado para iniciar a correcao de pendencias.");
        }

        Guid? snapshotOwnerId = null;
        string? snapshotClientId = null;
        string? snapshotConnectionAlias = null;
        if (snapshotContext is not null)
        {
            try
            {
                var scope = await snapshotContext.TryResolveAsync(moodleAlias, cancellationToken);
                if (scope is not null)
                {
                    snapshotOwnerId = scope.Identity.Id;
                    snapshotClientId = scope.ClientId;
                    snapshotConnectionAlias = scope.ConnectionAlias;
                }
            }
            catch
            {
                // Legacy callers without a local portal identity retain the
                // existing live command path.
            }
        }

        StartPendingGradingRunResult data;
        try
        {
            data = await mediator.Send(
                new StartPendingGradingRunCommand(
                    moodleUserId.Value.ToString(),
                    maxCourses,
                    maxItemsPerBatch,
                    includeRubric,
                    includeSubmissionFiles,
                    includeCourseMaterials,
                    teacherInstructions,
                    priority,
                    UseSubmissionSnapshots: snapshotOwnerId is not null,
                    SnapshotOwnerId: snapshotOwnerId,
                    SnapshotClientId: snapshotClientId,
                    SnapshotConnectionAlias: snapshotConnectionAlias,
                    CourseId: courseId,
                    AssignmentIds: assignmentIds,
                    IncludeAlreadyGraded: includeAlreadyGraded),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException ex)
        {
            return ToolResultHelper.Error<StartPendingGradingRunResult>(ex.Message);
        }
        catch (Exception exception)
        {
            return ToolResultHelper.Error<StartPendingGradingRunResult>(exception);
        }

        var hasResolutionFailure = data.AssignmentResolution.Any(item => !item.Resolved);
        var hasCourseFailure = data.Courses.Any(course =>
            course.Status is "course_read_failed" or "partial_failure");
        var responseStatus = hasResolutionFailure || hasCourseFailure
            ? "partial_failure"
            : data.Warnings.Count > 0
                ? "ok_with_skips"
                : "ok";
        var response = new ToolResponse<StartPendingGradingRunResult>(
            responseStatus,
            data,
            data.Warnings,
            AuditId: null,
            DateTimeOffset.UtcNow);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildStartPendingGradingRunNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }



    /// <summary>
    /// A snapshot accelerates the common path but must never be a prerequisite
    /// for discovering work that needs grading. When it cannot cover the
    /// request, resolve the assignment instances from Moodle and reuse the
    /// authoritative per-assignment reader.
    /// </summary>






    private async Task<CallToolResult> CreateLaunchPreviewCoreAsync(
        Guid batchJobId,
        IReadOnlyList<Guid> gradingItemIds,
        bool onlyReviewed,
        bool allowOverwriteExisting,
        bool allowPartial,
        CancellationToken cancellationToken)
    {
        if (batchJobId == Guid.Empty)
        {
            return ToolResultHelper.Error<CreateGradingLaunchPreviewResult>("Informe um identificador de lote valido.");
        }

        CreateGradingLaunchPreviewResult data;
        try
        {
            data = await mediator.Send(
                new CreateGradingLaunchPreviewCommand(
                    batchJobId,
                    gradingItemIds,
                    onlyReviewed,
                    allowOverwriteExisting,
                    allowPartial),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ToolResultHelper.Error<CreateGradingLaunchPreviewResult>("Nao foi possivel criar a previa de lancamento neste momento.");
        }

        var response = new ToolResponse<CreateGradingLaunchPreviewResult>(
            data.PendingActionId == Guid.Empty ? "blocked" : "pending_confirmation",
            data,
            data.Warnings,
            AuditId: null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildLaunchPreviewNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            // Ausencia de itens prontos e um estado de negocio recuperavel, nao
            // um argumento invalido da chamada MCP.
            IsError = false
        };
    }

    private async Task<CallToolResult> ConfirmLaunchCoreAsync(
        Guid pendingActionId,
        string confirmationText,
        CancellationToken cancellationToken)
    {
        if (pendingActionId == Guid.Empty)
        {
            return ToolResultHelper.Error<ConfirmMoodleBatchLaunchResult>("Informe uma acao pendente valida.");
        }

        if (string.IsNullOrWhiteSpace(confirmationText))
        {
            return ToolResultHelper.Error<ConfirmMoodleBatchLaunchResult>("Informe o texto literal de confirmacao.");
        }

        ConfirmMoodleBatchLaunchResult data;
        try
        {
            data = await mediator.Send(
                new ConfirmMoodleBatchLaunchCommand(
                    pendingActionId,
                    confirmationText,
                    ExecuteImmediately: false),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            return ToolResultHelper.Error<ConfirmMoodleBatchLaunchResult>(ex.Message);
        }
        catch
        {
            return ToolResultHelper.Error<ConfirmMoodleBatchLaunchResult>("Nao foi possivel confirmar o lancamento no Moodle neste momento.");
        }

        var response = new ToolResponse<ConfirmMoodleBatchLaunchResult>(
            data.Status == "authorized"
                ? "authorized"
                : data.FailedItems == 0 ? "ok" : "partial_failure",
            data,
            data.Failures
                .Select(failure => failure.Message)
                .Concat(data.Warnings ?? [])
                .ToArray(),
            data.AuditId,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildConfirmLaunchNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            // Falhas por item sao um resultado de negocio concluido e precisam chegar
            // ao cliente no StructuredContent. IsError fica reservado a falhas da tool.
            IsError = false
        };
    }



    private static string BuildStartPendingGradingRunNarration(StartPendingGradingRunResult response)
    {
        if (response.Batches.Count == 0)
        {
            return response.ExistingCorrections.Count > 0
                ? $"Nenhum sublote novo foi criado em {response.CoursesScanned} curso(s): a submissao ja possui correcao local. Consulte existingCorrections para usar o batchJobId/gradingRunId anterior."
                : $"Nenhuma entrega pendente elegivel foi encontrada em {response.CoursesScanned} curso(s).";
        }

        return $"Fluxo de correcoes pendentes iniciado (gradingRunId: {response.GradingRunId}): {response.TotalItems} entrega(s) em {response.CoursesScanned} curso(s), " +
               $"distribuidas em {response.Batches.Count} sublote(s). " +
               "Use o gradingRunId para percorrer as paginas sem interromper o fluxo pelos cursos ou itens bloqueados.";
    }








    private static string BuildLaunchPreviewNarration(CreateGradingLaunchPreviewResult response)
    {
        if (response.PendingActionId == Guid.Empty)
        {
            if (!response.DecisionSafe)
            {
                return $"Previa global bloqueada por cobertura incompleta: esperados {response.ExpectedItems}, preparados {response.PreparedItems}, ausentes {response.MissingItems} em {response.MissingBatchCount} sublote(s). Corrija a cobertura ou use allowPartial=true de forma explicita.";
            }

            return "Nenhum item pronto para lancamento foi encontrado.";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Previa de lancamento criada com {response.ReadyItems} item(ns).");
        builder.AppendLine();
        foreach (var item in response.Launches)
        {
            builder.AppendLine($"- Aluno: {item.StudentName ?? item.StudentId}");
            builder.AppendLine($"  Nota: {(item.Grade?.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR")) ?? "sem nota")}");
            builder.AppendLine($"  Feedback: {item.FeedbackText}");
            builder.AppendLine($"  Situacao: {item.Situation}");
        }

        if (response.Warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Avisos:");
            foreach (var warning in response.Warnings)
            {
                builder.AppendLine($"- {warning}");
            }
        }

        builder.AppendLine();
        builder.Append($"Para publicar todas as correcoes seguras desta previa, envie exatamente: {response.ConfirmationText}");
        return builder.ToString();
    }

    private static string BuildConfirmLaunchNarration(ConfirmMoodleBatchLaunchResult response)
    {
        if (response.Status == "authorized")
        {
            return "Publicacao autorizada e colocada na fila duravel. Nenhuma alteracao foi feita no Moodle nesta chamada; o status sera atualizado conforme cada item for processado.";
        }

        var outcome = response.FailedItems == 0
            ? "concluido"
            : response.SentItems == 0
                ? "nao realizado"
                : "concluido parcialmente";
        var summary = $"Lancamento Moodle {outcome}: {response.SentItems} enviado(s), {response.FailedItems} falha(s).";
        var reasons = response.Failures
            .Select(failure => failure.Message)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => message.Trim().TrimEnd('.'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var warnings = (response.Warnings ?? [])
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => message.Trim().TrimEnd('.'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (reasons.Length == 0 && warnings.Length == 0)
        {
            return summary;
        }

        var parts = new List<string>();
        if (reasons.Length > 0)
        {
            parts.Add($"Motivo(s): {string.Join("; ", reasons.Take(2))}");
        }

        if (warnings.Length > 0)
        {
            parts.Add($"Aviso(s): {string.Join("; ", warnings.Take(2))}");
        }

        var suffix = reasons.Length > 2 || warnings.Length > 2
            ? " Ha outros detalhes no resultado estruturado."
            : string.Empty;
        return $"{summary} {string.Join(". ", parts)}.{suffix}";
    }


    private static string BuildPrepareAiBatchNarration(AiGradingBatchPackageResult data)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Pacote IA — Lote {data.BatchJobId}");
        sb.AppendLine();
        sb.AppendLine($"**{data.ItemsOnPage} item(ns)** nesta pagina; **{data.EligibleItems}** ainda elegiveis para analise pela IA.");
        if (data.ItemsExcludedByStatus > 0)
        {
            sb.AppendLine($"**{data.ItemsExcludedByStatus} item(ns)** ja fora da fila da IA nao foram repetidos. Cobertura persistida: **{data.PreparedItems}/{data.ExpectedItems}**; faltantes: **{data.MissingItems}**.");
        }
        sb.AppendLine();

        foreach (var item in data.Items)
        {
            var resourceInfo = item.Resources?.Count > 0
                ? $"{item.Resources.Count} arquivo(s) original(is) via MCP Resource" +
                  (item.Resources.Any(resource => resource.ArtifactId is not null)
                      ? $"; artifactId(s): {string.Join(", ", item.Resources.Where(resource => resource.ArtifactId is not null).Select(resource => resource.ArtifactId))}"
                      : string.Empty)
                : "sem arquivo original disponível";
            var gradeInfo = item.GradingMode == "feedback_only"
                ? "modo: somente feedback (sem nota)"
                : item.MaxGrade > 0
                    ? $"nota maxima: {item.MaxGrade}"
                    : "nota maxima: nao confirmada (sugestao numerica bloqueada)";
            sb.AppendLine($"- **Aluno {item.StudentId}** (item {item.GradingItemId}): {resourceInfo}, {gradeInfo}");
        }

        sb.AppendLine();
        sb.AppendLine(data.Instructions);
        return sb.ToString();
    }

    private static string BuildSaveAiGradingNarration(SaveAiGradingBatchResult data)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Resultado — Salvar Correcoes IA");
        sb.AppendLine();
        sb.AppendLine($"- **Salvos:** {data.SavedItems}");
        sb.AppendLine($"- **Ignorados:** {data.SkippedItems}");
        sb.AppendLine($"- **Falhas:** {data.FailedItems}");
        sb.AppendLine();
        sb.AppendLine($"**Proximo passo:** {data.NextStep}");
        if (data.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Avisos:** " + string.Join("; ", data.Warnings.Take(3)));
        }
        return sb.ToString();
    }

    internal static string BuildCorrectionsCsv(IReadOnlyList<GradingCorrectionsCsvRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("nome;nota;feedback");
        foreach (var row in rows)
        {
            builder.Append(CsvField(row.Nome));
            builder.Append(';');
            builder.Append(row.Nota?.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR")) ?? string.Empty);
            builder.Append(';');
            builder.Append(CsvField(row.Feedback));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string CsvField(string? value) =>
        $"\"{(value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static bool TryParseOptionalPositiveId(
        string? value,
        string name,
        out long? parsed,
        out string? error)
    {
        parsed = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0)
        {
            parsed = number;
            return true;
        }

        error = $"O {name} deve ser numerico e positivo.";
        return false;
    }



}
