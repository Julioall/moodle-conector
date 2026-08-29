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
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;
using MoodleConnector.Domain.Grading;
using MoodleConnector.Presentation.Tools;
using MoodleConnector.Presentation.Configuration;

namespace MoodleConnector.Presentation.Tools.Grading;

[McpServerToolType]
public sealed class MoodleGradingTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver,
    MoodleSnapshotToolContext? snapshotContext = null,
    IOptions<GradingLimitsOptions>? gradingLimits = null)
{
    private readonly GradingLimitsOptions _gradingLimits = gradingLimits?.Value ?? new GradingLimitsOptions();
    [McpServerTool(
        Name = "discover_grading_functions",
        Title = "Discover Grading Functions",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<DiscoverMoodleGradingFunctionsResponse>))]
    [MoodleToolMetadata(
        Family = "grading",
        Classification = "R6",
        Kind = "diagnostic",
        CanonicalOperation = "grading.diagnostics.capabilities",
        Structural = false,
        ExposureStatus = "Diagnostic",
        ExposureReason = "Descoberta tecnica de pre-requisitos da correcao assistida; suporte e troubleshooting, nao intencao de correcao.",
        Evidence = "Implementacao MoodleGradingTools.DescobrirFuncoesMoodleCorrecaoAsync; preservada em Full e callable por compatibilidade.")]
    [Description("Verifica quais funcoes Moodle necessarias para correcao assistida estao habilitadas no servico atual. Nao baixa entregas nem executa escrita.")]
    public Task<CallToolResult> DescobrirFuncoesMoodleCorrecaoAsync(
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrao do usuario.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return DiscoverCoreAsync(moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "execute_grading_discovery",
        Title = "Execute Grading Discovery",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GradingTechnicalDiscoveryReport>))]
    [MoodleToolMetadata(
        Family = "grading",
        Classification = "R6",
        Kind = "diagnostic",
        CanonicalOperation = "grading.diagnostics.discovery_report",
        Structural = false,
        ExposureStatus = "Diagnostic",
        ExposureReason = "Relatorio tecnico detalhado de capacidades e pre-requisitos; o fluxo de grading exposto usa a estrategia registrada.",
        Evidence = "Implementacao MoodleGradingTools.ExecutarDescobertaTecnicaCorrecaoAsync; preservada em Full e callable por compatibilidade.")]
    [Description("Consolida a descoberta tecnica da correcao assistida: funcoes Moodle, anexos, mod_assign_save_grade, permissao de escrita, rubricas/escalas e modo de token. Nao baixa arquivos nem escreve no Moodle.")]
    public Task<CallToolResult> ExecutarDescobertaTecnicaCorrecaoAsync(
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrao do usuario.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return TechnicalDiscoveryCoreAsync(moodleAlias, cancellationToken);
    }

    public Task<CallToolResult> ListarEntregasCorrigiveisAsync(
        [Description("Identificador do curso Moodle.")]
        string courseId,
        [Description("Identificadores das tarefas Moodle.")]
        string[] assignmentIds,
        [Description("Filtro de status: all, submitted, pending, late, awaiting_grading.")]
        string status = "awaiting_grading",
        [Description("Quando true, força filtro apenas para entregas aguardando correção.")]
        bool onlyAwaitingGrading = true,
        [Description("Quando false, remove entregas atrasadas da lista.")]
        bool includeLate = true,
        [Description("Página de resultados, iniciando em 1.")]
        int page = 1,
        [Description("Tamanho da página, de 1 a 100.")]
        int perPage = 25,
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrão do usuário.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListarEntregasCorrigiveisCoreAsync(
            courseId,
            assignmentIds,
            status,
            onlyAwaitingGrading,
            includeLate,
            page,
            perPage,
            moodleAlias,
            cancellationToken);
    }

    [McpServerTool(
        Name = "list_all_gradable_submissions",
        Title = "List All Gradable Submissions",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListarEntregasCorrigiveisResponse>))]
    [Description("Lê as entregas aguardando correção diretamente do snapshot persistente do curso. Não consulta o Moodle nesta chamada; se o snapshot estiver ausente, incompleto ou desatualizado, agenda uma atualização assíncrona e informa a cobertura disponível." )]
    public Task<CallToolResult> ListarEntregasCorrigiveisDoSnapshotAsync(
        [Description("Identificador do curso Moodle. Pode ser courseId, shortName ou idnumber.")]
        string courseId,
        [Description("Identificadores opcionais das tarefas. Quando vazio, lê todas as tarefas disponíveis no snapshot.")]
        string[]? assignmentIds = null,
        [Description("Filtro de status: all, submitted, pending, late ou awaiting_grading.")]
        string status = "awaiting_grading",
        [Description("Quando true, considera somente entregas aguardando correção.")]
        bool onlyAwaitingGrading = true,
        [Description("Quando false, remove entregas atrasadas da leitura.")]
        bool includeLate = true,
        [Description("Página de resultados, iniciando em 1.")]
        int page = 1,
        [Description("Tamanho da página, de 1 a 100.")]
        int perPage = 25,
        [Description("Alias do Moodle usado para localizar o snapshot. A chamada não consulta o Moodle.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return ListarEntregasCorrigiveisDoSnapshotCoreAsync(
            courseId,
            assignmentIds ?? [],
            status,
            onlyAwaitingGrading,
            includeLate,
            page,
            perPage,
            moodleAlias,
            cancellationToken);
    }

    [McpServerTool(
        Name = "create_assisted_grading_batch",
        Title = "Create Assisted Grading Batch",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<CreateAssistedGradingBatchResult>))]
    [Description("Cria um lote interno pequeno de correcao assistida a partir de entregas Moodle. Nao executa analise de IA nem escreve nota/feedback no Moodle.")]
    public Task<CallToolResult> CriarLoteCorrecaoAssistidaAsync(
        [Description("Identificador do curso. Deve ser numerico nesta versao inicial.")]
        string courseId,
        [Description("Identificadores numéricos das tarefas Moodle. Aceita tanto o ID do módulo (cmid) quanto o ID interno da tarefa (assignment instance ID).")]
        string[] assignmentIds,
        [Description("Submission IDs especificas a incluir. Quando vazio, inclui entregas retornadas pelo filtro.")]
        string[]? submissionIds = null,
        [Description("Limite de itens do lote, de 1 a 400.")]
        int maxItems = 25,
        [Description("Quando true, inclui apenas entregas aguardando correcao.")]
        bool onlyAwaitingGrading = true,
        [Description("Quando true, inclui contexto de rubrica na montagem do lote.")]
        bool includeRubric = true,
        [Description("Quando true, considera anexos de submissao para analise.")]
        bool includeSubmissionFiles = true,
        [Description("Quando true, inclui materiais do curso como contexto auxiliar.")]
        bool includeCourseMaterials = false,
        [Description("Instrucoes adicionais do professor/tutor para orientar a correcao.")]
        string? teacherInstructions = null,
        [Description("Prioridade sugerida para processamento: low, normal ou high.")]
        string priority = "normal",
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrao do usuario.")]
        string? moodleAlias = null,
        [Description("Chave estável da solicitação para reutilizar o mesmo lote caso o cliente perca a resposta (por exemplo, após HTTP 504).")]
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        return CreateBatchCoreAsync(
            courseId,
            assignmentIds,
            submissionIds ?? [],
            maxItems,
            onlyAwaitingGrading,
            includeRubric,
            includeSubmissionFiles,
            includeCourseMaterials,
            teacherInstructions,
            priority,
            moodleAlias,
            idempotencyKey,
            cancellationToken);
    }

    [McpServerTool(
        Name = "start_pending_grading_run",
        Title = "Start Pending Grading Run",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<StartPendingGradingRunResult>))]
    [Description("Inicia o fluxo de correcao de todas as entregas pendentes em todos os cursos acessiveis. Percorre os cursos, cria um sublote por curso com entregas aguardando correcao e continua quando um curso ou uma atividade falhar. Nao gera nota nem escreve no Moodle. Para cada batchJobId retornado, prepare a IA, salve rascunhos, revise com o professor e confirme o lancamento. Ao final use export_pending_grading_run_report com todos os batchJobIds.")]
    public Task<CallToolResult> IniciarFluxoCorrecaoPendentesAsync(
        [Description("Numero maximo de cursos a percorrer. Use 0 para todos os cursos acessiveis.")]
        int maxCourses = 0,
        [Description("Numero maximo de entregas por sublote, de 1 a 400. A ferramenta cria sublotes adicionais ate percorrer todas as entregas pendentes.")]
        int maxItemsPerBatch = 400,
        [Description("Quando true, inclui contexto de rubrica na montagem dos sublotes.")]
        bool includeRubric = true,
        [Description("Quando true, baixa e extrai arquivos das entregas.")]
        bool includeSubmissionFiles = true,
        [Description("Quando true, inclui materiais próximos do curso como contexto auxiliar.")]
        bool includeCourseMaterials = false,
        [Description("Instrucoes adicionais do professor/tutor para orientar a correcao.")]
        string? teacherInstructions = null,
        [Description("Prioridade sugerida: low, normal ou high.")]
        string priority = "normal",
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrao do usuario.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return StartPendingGradingRunCoreAsync(
            maxCourses,
            maxItemsPerBatch,
            includeRubric,
            includeSubmissionFiles,
            includeCourseMaterials,
            teacherInstructions,
            priority,
            moodleAlias,
            cancellationToken);
    }

    [McpServerTool(
        Name = "get_grading_batch_status",
        Title = "Get Grading Batch Status",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<AssistedGradingBatchStatusResult>))]
    [Description("Consulta o status e os itens de um lote interno de correcao assistida.")]
    public Task<CallToolResult> ConsultarStatusLoteCorrecaoAsync(
        [Description("Identificador do lote retornado por criar_lote_correcao_assistida.")]
        Guid batchJobId,
        [Description("Pagina de itens, iniciando em 1.")]
        int pagina = 1,
        [Description("Tamanho da pagina, de 1 a 100.")]
        int tamanhoPagina = 20,
        CancellationToken cancellationToken = default)
    {
        return GetBatchStatusCoreAsync(batchJobId, pagina, tamanhoPagina, cancellationToken);
    }

    [McpServerTool(
        Name = "export_grading_coordination_report",
        Title = "Export Grading Coordination Report",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<AssistedGradingCoordinationReportResult>))]
    [Description("Gera um relatorio consolidado do lote de correcao assistida para coordenacao, com contadores, itens que exigem atencao e criterios com lacunas. Nao escreve no Moodle.")]
    public Task<CallToolResult> ExportarRelatorioCorrecaoCoordenacaoAsync(
        [Description("Identificador do lote retornado por criar_lote_correcao_assistida.")]
        Guid batchJobId,
        CancellationToken cancellationToken = default)
    {
        return GetCoordinationReportCoreAsync(batchJobId, cancellationToken);
    }

    [McpServerTool(
        Name = "export_pending_grading_run_report",
        Title = "Export Pending Grading Run Report",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<PendingGradingRunReportResult>))]
    [Description("Consolida o resultado final de varios sublotes criados por start_pending_grading_run. Retorna listas completas das entregas corrigidas e lancadas no Moodle e das nao corrigidas, com o motivo para ajuste manual. Nao escreve no Moodle.")]
    public async Task<CallToolResult> ExportarRelatorioFluxoCorrecaoPendentesAsync(
        [Description("Todos os batchJobIds retornados por start_pending_grading_run.")]
        Guid[] batchJobIds,
        CancellationToken cancellationToken = default)
    {
        if (batchJobIds.Length == 0 || batchJobIds.All(id => id == Guid.Empty))
        {
            return ToolResultHelper.Error<PendingGradingRunReportResult>("Informe ao menos um lote de correcao valido.");
        }

        PendingGradingRunReportResult data;
        try
        {
            data = await mediator.Send(new GetPendingGradingRunReportQuery(batchJobIds), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            return ToolResultHelper.Error<PendingGradingRunReportResult>(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ToolResultHelper.Error<PendingGradingRunReportResult>(ex.Message);
        }
        catch
        {
            return ToolResultHelper.Error<PendingGradingRunReportResult>("Nao foi possivel consolidar o relatorio final de correcoes pendentes neste momento.");
        }

        var response = new ToolResponse<PendingGradingRunReportResult>(
            "ok",
            data,
            [],
            AuditId: null,
            DateTimeOffset.UtcNow);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildPendingGradingRunReportNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
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
    [Description("Cancela um lote interno de correcao assistida que ainda nao foi completamente processado. Nao escreve no Moodle.")]
    public Task<CallToolResult> CancelarLoteCorrecaoAssistidaAsync(
        [Description("Identificador do lote retornado por criar_lote_correcao_assistida.")]
        Guid batchJobId,
        CancellationToken cancellationToken = default)
    {
        return CancelBatchCoreAsync(batchJobId, cancellationToken);
    }

    [McpServerTool(
        Name = "get_assisted_grading_item",
        Title = "Get Assisted Grading Item",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<AssistedGradingItemDetailResult>))]
    [Description("Consulta os dados minimos e o rascunho de um item de correcao assistida. Nao retorna anexos completos nem escreve no Moodle.")]
    public Task<CallToolResult> ConsultarItemCorrecaoAssistidaAsync(
        [Description("Identificador do item retornado pelo status do lote.")]
        Guid gradingItemId,
        [Description("Identificador opcional do lote esperado para validar vinculo do item.")]
        Guid? batchJobId = null,
        CancellationToken cancellationToken = default)
    {
        return GetGradingItemCoreAsync(gradingItemId, batchJobId, cancellationToken);
    }

    [McpServerTool(
        Name = "update_grading_draft",
        Title = "Update Grading Draft",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<AssistedGradingItemDetailResult>))]
    [Description("Salva a revisao humana de nota e feedback em um item interno de correcao assistida. Nao escreve no Moodle.")]
    public Task<CallToolResult> AtualizarRascunhoCorrecaoAsync(
        [Description("Identificador do item de correcao.")]
        Guid gradingItemId,
        [Description("Nota final revisada pelo professor/tutor.")]
        decimal? finalGrade,
        [Description("Feedback final revisado pelo professor/tutor.")]
        string finalFeedback,
        [Description("Decisao do professor/tutor, por exemplo approved ou needs_changes.")]
        string teacherDecision,
        [Description("Observacoes internas do professor/tutor sobre a revisao.")]
        string? reviewNotes = null,
        [Description("Status de revisao visto antes da edicao. Use NotReviewed ao revisar um rascunho ainda nao revisado.")]
        string expectedReviewStatus = "NotReviewed",
        [Description("Hash da versao do rascunho lida pelo cliente; bloqueia sobrescrita concorrente quando divergente.")]
        string? expectedDraftVersionHash = null,
        CancellationToken cancellationToken = default)
    {
        return UpdateDraftCoreAsync(
            gradingItemId,
            finalGrade,
            finalFeedback,
            teacherDecision,
            reviewNotes,
            expectedReviewStatus,
            expectedDraftVersionHash,
            cancellationToken);
    }

    [McpServerTool(
        Name = "update_grading_drafts_batch",
        Title = "Update Grading Drafts Batch",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<BatchDraftUpdateResult>))]
    [Description("Salva, em uma unica chamada, as revisoes humanas de nota e feedback de varios itens. Nao escreve no Moodle.")]
    public async Task<CallToolResult> AtualizarRascunhosCorrecaoLoteAsync(
        [Description("Identificador do lote de correcao assistida.")]
        Guid batchJobId,
        [Description("Revisoes selecionadas pelo professor/tutor.")]
        ReviewedGradingDraftInput[] items,
        CancellationToken cancellationToken = default)
    {
        if (items.Length == 0)
        {
            return ToolResultHelper.Error<BatchDraftUpdateResult>("Selecione pelo menos uma correcao para salvar.");
        }

        var commandItems = items.Select(item => new UpdateAssistedGradingDraftItemInput(
            item.GradingItemId,
            item.FinalGrade,
            item.FinalFeedback,
            item.TeacherDecision,
            item.ReviewNotes,
            item.ExpectedReviewStatus,
            item.ExpectedDraftVersionHash
        )).ToArray();

        var result = await mediator.Send(new UpdateAssistedGradingDraftsBatchCommand(batchJobId, commandItems), cancellationToken);

        var data = new BatchDraftUpdateResult(
            result.SuccessCount,
            result.FailureCount,
            result.SavedIds,
            result.Failures.Select(f => new BatchDraftUpdateFailure(f.GradingItemId, f.Message)).ToArray()
        );

        var response = new ToolResponse<BatchDraftUpdateResult>(
            result.FailureCount == 0 ? "ok" : "partial_failure",
            data,
            result.Failures.Select(failure => failure.Message).ToArray(),
            AuditId: null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock
            {
                Text = result.FailureCount == 0
                    ? $"{result.SuccessCount} correcao(oes) revisada(s) foram salvas e estao prontas para preparar o envio."
                    : $"Salvei {result.SuccessCount} correcao(oes), mas {result.FailureCount} precisam ser revisadas novamente."
            }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = result.SuccessCount == 0
        };
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
    [Description("Cria uma acao pendente com previa revisavel para lancar nota e feedback no Moodle. Nao executa escrita oficial. PRE-REQUISITO: o professor deve ter revisado os feedbacks usando revisar_feedbacks_lote antes de chamar esta tool.")]
    public Task<CallToolResult> CriarPreviaLancamentoLoteAsync(
        [Description("Identificador do lote de correcao assistida.")]
        Guid batchJobId,
        [Description("Itens especificos a incluir. Quando vazio, inclui todos os itens prontos do lote.")]
        Guid[]? gradingItemIds = null,
        [Description("Quando true, inclui apenas itens revisados.")]
        bool onlyReviewed = true,
        [Description("Quando true, a confirmacao autoriza explicitamente sobrescrever notas e feedbacks que ja existem no Moodle.")]
        bool allowOverwriteExisting = false,
        CancellationToken cancellationToken = default)
    {
        return CreateLaunchPreviewCoreAsync(
            batchJobId,
            gradingItemIds ?? [],
            onlyReviewed,
            allowOverwriteExisting,
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
    [Description("Confirma uma acao pendente e lanca nota/feedback no Moodle usando o texto literal de confirmacao.")]
    public Task<CallToolResult> ConfirmarLancamentoLoteMoodleAsync(
        [Description("Identificador da acao pendente retornada por criar_previa_lancamento_lote.")]
        Guid pendingActionId,
        [Description("Texto exato de confirmacao retornado na previa.")]
        string confirmationText,
        CancellationToken cancellationToken = default)
    {
        return ConfirmLaunchCoreAsync(pendingActionId, confirmationText, cancellationToken);
    }

    [McpServerTool(
        Name = "get_grading_audit",
        Title = "Get Grading Audit",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GradingAuditResult>))]
    [Description("Consulta eventos sanitizados de auditoria de correcao pelo auditId retornado na confirmacao de lancamento.")]
    public Task<CallToolResult> ConsultarAuditoriaCorrecaoAsync(
        [Description("AuditId/correlation id retornado pela confirmacao de lancamento.")]
        string auditId,
        [Description("Pagina de eventos, iniciando em 1.")]
        int pagina = 1,
        [Description("Tamanho da pagina, de 1 a 100.")]
        int tamanhoPagina = 20,
        CancellationToken cancellationToken = default)
    {
        return GetAuditCoreAsync(auditId, pagina, tamanhoPagina, cancellationToken);
    }

    [McpServerTool(
        Name = "get_grading_batch_audit",
        Title = "Get Grading Batch Audit",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GradingAuditResult>))]
    [Description("Consulta eventos sanitizados de auditoria de correcao pelo batchJobId.")]
    public Task<CallToolResult> ConsultarAuditoriaCorrecaoLoteAsync(
        [Description("Identificador do lote de correcao assistida.")]
        Guid batchJobId,
        [Description("Pagina de eventos, iniciando em 1.")]
        int pagina = 1,
        [Description("Tamanho da pagina, de 1 a 100.")]
        int tamanhoPagina = 20,
        CancellationToken cancellationToken = default)
    {
        return GetAuditByBatchCoreAsync(batchJobId, pagina, tamanhoPagina, cancellationToken);
    }

    // ============================================================
    // Tool: preparar_lote_correcao_ia
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
    [Description("Retorna o pacote estruturado de um lote para correcao via IA: textos extraidos das entregas, enunciado, criterios e nota maxima por aluno. Use apos criar_lote_correcao_assistida para obter o contexto completo e gerar nota e feedback no chat. Nao escreve no Moodle.")]
    public async Task<CallToolResult> PrepararLoteCorrecaoIaAsync(
        [Description("Identificador do lote retornado por criar_lote_correcao_assistida.")]
        Guid batchJobId,
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
                new PrepareAiGradingBatchQuery(batchJobId),
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

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildPrepareAiBatchNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    // ============================================================
    // Tool: salvar_correcoes_ia_lote
    // ============================================================

    [McpServerTool(
        Name = "save_ai_grading_batch",
        Title = "Save AI Grading Batch",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<SaveAiGradingBatchResult>))]
    [Description("Salva nota e feedback gerados pela IA como rascunho interno para cada aluno do lote. Nao escreve no Moodle. OBRIGATORIO: apos salvar, sempre chame revisar_feedbacks_lote para exibir a interface de revisao humana. Nunca pule a revisao.")]
    public async Task<CallToolResult> SalvarCorrecoesIaLoteAsync(
        [Description("Identificador do lote retornado por criar_lote_correcao_assistida.")]
        Guid batchJobId,
        [Description("Array de correcoes. O formato legado usa gradingItemId, nota e feedback. Quando disponivel, proposal deve conter a versao/hash do contexto, criterios, evidencias, cobertura e feedback estruturado; a escala numerica continua sendo validada pelo Moodle.")]
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
            IsError = data.SavedItems == 0 && data.FailedItems > 0
        };
    }

    private async Task<CallToolResult> DiscoverCoreAsync(
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return ToolResultHelper.Error<DiscoverMoodleGradingFunctionsResponse>("Usuario nao autenticado para descobrir funcoes de correcao.");
        }

        MoodleGradingCapabilitiesReport report;
        try
        {
            report = await mediator.Send(
                new DiscoverMoodleGradingCapabilitiesQuery(moodleUserId.Value.ToString()),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ToolResultHelper.Error<DiscoverMoodleGradingFunctionsResponse>("Nao foi possivel consultar as funcoes Moodle de correcao neste momento.");
        }

        var data = ToResponse(report);
        var response = new ToolResponse<DiscoverMoodleGradingFunctionsResponse>(
            "ok",
            data,
            BuildWarnings(data),
            AuditId: null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private async Task<CallToolResult> TechnicalDiscoveryCoreAsync(
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return ToolResultHelper.Error<GradingTechnicalDiscoveryReport>("Usuario nao autenticado para executar descoberta tecnica de correcao.");
        }

        GradingTechnicalDiscoveryReport report;
        try
        {
            report = await mediator.Send(
                new GradingTechnicalDiscoveryQuery(moodleUserId.Value.ToString()),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ToolResultHelper.Error<GradingTechnicalDiscoveryReport>("Nao foi possivel executar a descoberta tecnica de correcao neste momento.");
        }

        var response = new ToolResponse<GradingTechnicalDiscoveryReport>(
            "ok",
            report,
            report.Warnings,
            AuditId: null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildTechnicalDiscoveryNarration(report) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private async Task<CallToolResult> CreateBatchCoreAsync(
        string courseId,
        IReadOnlyList<string> assignmentIds,
        IReadOnlyList<string> submissionIds,
        int maxItems,
        bool onlyAwaitingGrading,
        bool includeRubric,
        bool includeSubmissionFiles,
        bool includeCourseMaterials,
        string? teacherInstructions,
        string priority,
        string? moodleAlias,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return ToolResultHelper.Error<CreateAssistedGradingBatchResult>("Informe um identificador de curso.");
        }

        if (assignmentIds.Count == 0 || assignmentIds.All(string.IsNullOrWhiteSpace))
        {
            return ToolResultHelper.Error<CreateAssistedGradingBatchResult>("Informe pelo menos uma tarefa.");
        }

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return ToolResultHelper.Error<CreateAssistedGradingBatchResult>("Usuario nao autenticado para criar lote de correcao.");
        }

        var effectiveCourseId = courseId;
        IReadOnlyList<string> effectiveAssignmentIds = assignmentIds;
        IReadOnlyList<AssignmentSubmissionSummary>? prefetchedSubmissions = null;
        if (snapshotContext is not null && assignmentIds.Count == 1)
        {
            try
            {
                var scope = await snapshotContext.TryResolveAsync(moodleAlias, cancellationToken);
                if (scope is not null)
                {
                    effectiveCourseId = await snapshotContext.ResolveCourseIdAsync(scope, courseId, cancellationToken);
                    var snapshot = await snapshotContext.GetSubmissionsAsync(scope, effectiveCourseId, cancellationToken);
                    var assignment = snapshot is { Data: not null, IsStale: false }
                        ? AssignmentSubmissionSnapshotProjector.FindAssignment(snapshot.Data, assignmentIds[0])
                        : null;
                    if (assignment is { IsComplete: true })
                    {
                        effectiveAssignmentIds = [assignment.AssignmentId];
                        prefetchedSubmissions = assignment.Submissions;
                    }
                }
            }
            catch
            {
                // Snapshot e uma otimizacao. Uma falha local de cache nao
                // deve esconder a leitura Moodle nem produzir lista vazia.
                prefetchedSubmissions = null;
                effectiveCourseId = courseId;
                effectiveAssignmentIds = assignmentIds;
            }
        }

        CreateAssistedGradingBatchResult data;
        using var creationDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        creationDeadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_gradingLimits.BatchCreationTimeoutSeconds, 5, 95)));
        try
        {
            data = await mediator.Send(
                new CreateAssistedGradingBatchCommand(
                    moodleUserId.Value.ToString(),
                    effectiveCourseId,
                    effectiveAssignmentIds,
                    submissionIds,
                    maxItems,
                    onlyAwaitingGrading,
                    includeRubric,
                    includeSubmissionFiles,
                    includeCourseMaterials,
                    teacherInstructions,
                    priority,
                    prefetchedSubmissions,
                    idempotencyKey),
                creationDeadline.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return ToolResultHelper.Error<CreateAssistedGradingBatchResult>(
                "A criacao do lote excedeu o prazo seguro de processamento. Nenhuma ausencia de pendencias foi inferida; repita com a mesma chave de idempotencia.",
                errorCode: MoodleErrorContract.RequestTimeout);
        }
        catch (MoodleApiException exception)
        {
            return ToolResultHelper.Error<CreateAssistedGradingBatchResult>(exception);
        }
        catch (Exception exception)
        {
            return ToolResultHelper.Error<CreateAssistedGradingBatchResult>(exception);
        }

        var response = new ToolResponse<CreateAssistedGradingBatchResult>(
            string.Equals(data.Status, "PartialFailure", StringComparison.OrdinalIgnoreCase)
                ? "partial_failure"
                : "ok",
            data,
            data.Warnings,
            AuditId: null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildCreateBatchNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private async Task<CallToolResult> StartPendingGradingRunCoreAsync(
        int maxCourses,
        int maxItemsPerBatch,
        bool includeRubric,
        bool includeSubmissionFiles,
        bool includeCourseMaterials,
        string? teacherInstructions,
        string priority,
        string? moodleAlias,
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
                    SnapshotConnectionAlias: snapshotConnectionAlias),
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
        catch
        {
            return ToolResultHelper.Error<StartPendingGradingRunResult>("Nao foi possivel iniciar o fluxo de correcao de pendencias neste momento.");
        }

        var response = new ToolResponse<StartPendingGradingRunResult>(
            data.Warnings.Count == 0 ? "ok" : "partial_failure",
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

    private async Task<CallToolResult> ListarEntregasCorrigiveisCoreAsync(
        string courseId,
        IReadOnlyList<string> assignmentIds,
        string status,
        bool onlyAwaitingGrading,
        bool includeLate,
        int page,
        int perPage,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return ToolResultHelper.Error<ListarEntregasCorrigiveisResponse>("Informe um identificador de curso.");
        }

        var normalizedAssignmentIds = assignmentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedAssignmentIds.Length == 0)
        {
            return ToolResultHelper.Error<ListarEntregasCorrigiveisResponse>("Informe pelo menos uma tarefa para listar entregas corrigiveis.");
        }

        if (!TryParseSubmissionFilter(status, out var parsedFilter))
        {
            return ToolResultHelper.Error<ListarEntregasCorrigiveisResponse>("Filtro de status invalido. Use all, submitted, pending, late ou awaiting_grading.");
        }

        var filter = onlyAwaitingGrading ? AssignmentSubmissionFilter.NeedsGrading : parsedFilter;
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(perPage, 1, 100);

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return ToolResultHelper.Error<ListarEntregasCorrigiveisResponse>("Usuario nao autenticado para listar entregas corrigiveis.");
        }

        var items = new List<EntregaCorrigivelItem>();
        var warnings = new List<string>();
        var failedAssignments = new List<EntregaCorrigivelFalha>();
        var failedAssignmentCount = 0;
        Exception? firstFailure = null;
        var resolvedCourseId = courseId;
        MoodleSnapshotToolScope? snapshotScope = null;
        MoodleSnapshotEnvelope<CourseAssignmentSubmissionsSnapshot>? submissionsSnapshot = null;
        var refreshQueued = false;
        var usedSnapshot = false;
        var usedLive = false;

        if (snapshotContext is not null)
        {
            try
            {
                snapshotScope = await snapshotContext.TryResolveAsync(moodleAlias, cancellationToken);
                if (snapshotScope is not null)
                {
                    resolvedCourseId = await snapshotContext.ResolveCourseIdAsync(snapshotScope, courseId, cancellationToken);
                    submissionsSnapshot = await snapshotContext.GetSubmissionsAsync(snapshotScope, resolvedCourseId, cancellationToken);
                    if (submissionsSnapshot is null || submissionsSnapshot.Data is null || submissionsSnapshot.IsStale)
                    {
                        refreshQueued = await snapshotContext.QueueAsync(
                            snapshotScope,
                            moodleUserId.Value.ToString(),
                            MoodleSnapshotDatasets.Submissions,
                            resolvedCourseId,
                            priority: 10,
                            force: submissionsSnapshot is not null,
                            cancellationToken);
                    }
                }
            }
            catch
            {
                // Snapshot access is an optimization. The live query below
                // remains authoritative when the local portal context is not
                // available or cannot be read.
                snapshotScope = null;
                submissionsSnapshot = null;
            }
        }

        foreach (var assignmentId in normalizedAssignmentIds)
        {
            var requestPage = 1;
            while (true)
            {
                AssignmentSubmissionsPage? submissions;
                try
                {
                    var snapshotItem = submissionsSnapshot?.Data is null
                        ? null
                        : AssignmentSubmissionSnapshotProjector.FindAssignment(submissionsSnapshot.Data, assignmentId);
                    if (snapshotItem is { IsComplete: true } &&
                        (filter != AssignmentSubmissionFilter.NeedsGrading ||
                         snapshotItem.Coverage?.NeedsGradingComplete == true))
                    {
                        submissions = AssignmentSubmissionSnapshotProjector.ToPage(
                            snapshotItem,
                            resolvedCourseId,
                            filter,
                            requestPage,
                            100,
                            since: null,
                            before: null,
                            includeLate,
                            includeUngraded: true);
                        usedSnapshot = true;
                    }
                    else
                    {
                        submissions = await mediator.Send(
                            new ListAssignmentSubmissionsQuery(
                                moodleUserId.Value.ToString(),
                                resolvedCourseId,
                                assignmentId,
                                filter,
                                requestPage,
                                100,
                                Since: null,
                                Before: null,
                                IncludeLate: includeLate,
                                IncludeUngraded: true),
                            cancellationToken);
                        usedLive = true;
                        if (snapshotScope is not null &&
                            (snapshotItem is null || !snapshotItem.IsComplete ||
                            (filter == AssignmentSubmissionFilter.NeedsGrading &&
                             snapshotItem.Coverage?.NeedsGradingComplete != true)))
                        {
                            refreshQueued |= await snapshotContext!.QueueAsync(
                                snapshotScope,
                                moodleUserId.Value.ToString(),
                                MoodleSnapshotDatasets.Submissions,
                                resolvedCourseId,
                                priority: 10,
                                cancellationToken: cancellationToken);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failedAssignmentCount++;
                    firstFailure ??= exception;
                    var failure = MoodleErrorContract.Describe(exception);
                    failedAssignments.Add(new EntregaCorrigivelFalha(
                        assignmentId,
                        failure.ErrorCode,
                        failure.Message));
                    warnings.Add($"Nao foi possivel listar entregas da tarefa {assignmentId} neste momento (codigo: {failure.ErrorCode}).");
                    break;
                }

                if (submissions is null)
                {
                    failedAssignmentCount++;
                    const string errorCode = "assignment_not_found";
                    const string message = "A tarefa nao foi encontrada ou nao esta acessivel para o usuario atual.";
                    failedAssignments.Add(new EntregaCorrigivelFalha(assignmentId, errorCode, message));
                    warnings.Add($"Tarefa {assignmentId} nao encontrada para o usuario atual (codigo: {errorCode}).");
                    break;
                }

                foreach (var submission in submissions.Submissions)
                {
                    items.Add(new EntregaCorrigivelItem(
                        submissions.CourseId,
                        submissions.AssignmentId,
                        submission.SubmissionId,
                        submission.UserId,
                        submission.FullName,
                        submission.Status,
                        submission.GradingStatus,
                        submission.Submitted,
                        submission.NeedsGrading,
                        submission.Late,
                        submission.AttemptNumber,
                        submission.SubmittedAt,
                        submission.ModifiedAt,
                        submission.FileCount,
                        submission.HasOnlineText));
                }

                if (!submissions.HasMore)
                {
                    break;
                }

                requestPage++;
            }
        }

        var orderedItems = items
            .OrderBy(item => item.StudentName ?? item.StudentId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.AssignmentId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var pagedItems = orderedItems
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToArray();

        var data = new ListarEntregasCorrigiveisResponse(
            safePage,
            safePageSize,
            orderedItems.Length,
            HasMore: safePage * safePageSize < orderedItems.Length,
            new EntregaCorrigivelContadores(
                Total: orderedItems.Length,
                AwaitingGrading: orderedItems.Count(item => item.NeedsGrading),
                Submitted: orderedItems.Count(item => item.Submitted),
                NotSubmitted: orderedItems.Count(item => !item.Submitted),
                Late: orderedItems.Count(item => item.Late)),
            new EntregaCorrigivelPermissoes(
                CanCreateBatch: true,
                CanCommitToMoodle: false,
                CommitStatus: "requires_sandbox_validation",
                CommitReason: "O lancamento em lote ainda nao foi validado em uma tarefa sandbox. A disponibilidade de funcoes e da conexao com escrita nao libera producao automaticamente."),
            failedAssignments,
            pagedItems);

        if (pagedItems.Any(item => item.StudentName is null))
        {
            warnings.Add("Os nomes dos estudantes nao estao disponiveis. Isso pode ocorrer por restricao de privacidade do Moodle ou por limitacao do token de acesso. Os estudantes sao identificados apenas pelo ID.");
        }

        if (failedAssignmentCount == normalizedAssignmentIds.Length)
        {
            return firstFailure is null
                ? ToolResultHelper.Error<ListarEntregasCorrigiveisResponse>(
                    warnings.FirstOrDefault() ?? "Nao foi possivel listar entregas corrigiveis neste momento.")
                : ToolResultHelper.Error<ListarEntregasCorrigiveisResponse>(firstFailure);
        }

        var responseStatus = failedAssignmentCount > 0 ? "partial_failure" : "ok";

        var freshness = submissionsSnapshot is not null && usedSnapshot && !usedLive
            ? new ToolFreshness(
                "snapshot",
                submissionsSnapshot.UpdatedAt,
                Math.Max(0, (long)(DateTimeOffset.UtcNow - submissionsSnapshot.UpdatedAt).TotalSeconds),
                submissionsSnapshot.IsStale,
                refreshQueued,
                submissionsSnapshot.IsComplete,
                submissionsSnapshot.RecordCount)
            : snapshotScope is not null
                ? new ToolFreshness(
                    "live",
                    null,
                    null,
                    false,
                    refreshQueued,
                    failedAssignmentCount == 0,
                    orderedItems.Length)
                : null;

        var response = new ToolResponse<ListarEntregasCorrigiveisResponse>(
            responseStatus,
            data,
            warnings,
            AuditId: null,
            DateTimeOffset.UtcNow,
            Freshness: freshness);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildListarEntregasCorrigiveisNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private async Task<CallToolResult> ListarEntregasCorrigiveisDoSnapshotCoreAsync(
        string courseId,
        IReadOnlyList<string> assignmentIds,
        string status,
        bool onlyAwaitingGrading,
        bool includeLate,
        int page,
        int perPage,
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return ToolResultHelper.Error<ListarEntregasCorrigiveisResponse>(
                "Informe um identificador de curso.",
                errorCode: "invalid_course_id");
        }

        if (!TryParseSubmissionFilter(status, out var parsedFilter))
        {
            return ToolResultHelper.Error<ListarEntregasCorrigiveisResponse>(
                "Filtro de status invalido. Use all, submitted, pending, late ou awaiting_grading.",
                errorCode: "invalid_submission_filter");
        }

        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(perPage, 1, 100);
        var filter = onlyAwaitingGrading ? AssignmentSubmissionFilter.NeedsGrading : parsedFilter;

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return ToolResultHelper.Error<ListarEntregasCorrigiveisResponse>(
                "Usuario nao autenticado para ler o snapshot de entregas.",
                errorCode: MoodleErrorContract.AuthenticationFailed);
        }

        if (snapshotContext is null)
        {
            return ToolResultHelper.Error<ListarEntregasCorrigiveisResponse>(
                "A leitura snapshot-only nao esta disponivel neste cliente.",
                errorCode: MoodleErrorContract.SnapshotUnavailable);
        }

        MoodleSnapshotToolScope? scope;
        try
        {
            scope = await snapshotContext.TryResolveAsync(moodleAlias, cancellationToken);
        }
        catch
        {
            scope = null;
        }

        if (scope is null)
        {
            return ToolResultHelper.Error<ListarEntregasCorrigiveisResponse>(
                "Nao foi possivel resolver a identidade local para ler o snapshot de entregas.",
                errorCode: MoodleErrorContract.SnapshotUnavailable);
        }

        var resolvedCourseId = await snapshotContext.ResolveCourseIdAsync(scope, courseId, cancellationToken);
        MoodleSnapshotEnvelope<CourseAssignmentSubmissionsSnapshot>? snapshot;
        try
        {
            snapshot = await snapshotContext.GetSubmissionsAsync(scope, resolvedCourseId, cancellationToken);
        }
        catch
        {
            snapshot = null;
        }

        if (snapshot is null)
        {
            var queued = await snapshotContext.QueueAsync(
                scope,
                moodleUserId.Value.ToString(),
                MoodleSnapshotDatasets.Submissions,
                resolvedCourseId,
                priority: 5,
                cancellationToken: cancellationToken);
            var queueMessage = queued
                ? " Uma atualização assíncrona foi agendada."
                : string.Empty;
            return ToolResultHelper.Error<ListarEntregasCorrigiveisResponse>(
                $"Ainda não existe snapshot de entregas para o curso {resolvedCourseId}. A leitura não consultou o Moodle.{queueMessage}",
                errorCode: MoodleErrorContract.SnapshotUnavailable);
        }

        var refreshQueued = snapshot.IsStale || !snapshot.IsComplete
            ? await snapshotContext.QueueAsync(
                scope,
                moodleUserId.Value.ToString(),
                MoodleSnapshotDatasets.Submissions,
                resolvedCourseId,
                priority: 5,
                force: true,
                cancellationToken: cancellationToken)
            : false;

        var normalizedAssignmentIds = assignmentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var selectedAssignments = normalizedAssignmentIds.Length == 0
            ? snapshot.Data.Assignments
            : normalizedAssignmentIds
                .Select(id => AssignmentSubmissionSnapshotProjector.FindAssignment(snapshot.Data, id))
                .Where(item => item is not null)
                .Cast<AssignmentSubmissionsSnapshotItem>()
                .ToArray();

        var failedAssignments = new List<EntregaCorrigivelFalha>();
        if (normalizedAssignmentIds.Length > 0)
        {
            foreach (var id in normalizedAssignmentIds)
            {
                if (AssignmentSubmissionSnapshotProjector.FindAssignment(snapshot.Data, id) is null)
                {
                    failedAssignments.Add(new EntregaCorrigivelFalha(
                        id,
                        "assignment_not_in_snapshot",
                        "A tarefa solicitada ainda não está disponível no snapshot persistente."));
                }
            }
        }

        var allItems = new List<EntregaCorrigivelItem>();
        foreach (var assignment in selectedAssignments)
        {
            if (!assignment.IsComplete)
            {
                failedAssignments.Add(new EntregaCorrigivelFalha(
                    assignment.AssignmentId,
                    assignment.ErrorCode ?? "snapshot_incomplete",
                    assignment.ErrorMessage ?? "Os dados desta tarefa estão incompletos no snapshot."));
                continue;
            }

            for (var assignmentPage = 1; ; assignmentPage++)
            {
                var pageData = AssignmentSubmissionSnapshotProjector.ToPage(
                    assignment,
                    resolvedCourseId,
                    filter,
                    assignmentPage,
                    100,
                    since: null,
                    before: null,
                    includeLate,
                    includeUngraded: true);
                allItems.AddRange(pageData.Submissions.Select(submission => new EntregaCorrigivelItem(
                    resolvedCourseId,
                    assignment.AssignmentId,
                    submission.SubmissionId,
                    submission.UserId,
                    submission.FullName,
                    submission.Status,
                    submission.GradingStatus,
                    submission.Submitted,
                    submission.NeedsGrading,
                    submission.Late,
                    submission.AttemptNumber,
                    submission.SubmittedAt,
                    submission.ModifiedAt,
                    submission.FileCount,
                    submission.HasOnlineText)));

                if (!pageData.HasMore)
                {
                    break;
                }
            }
        }

        var orderedItems = allItems
            .OrderBy(item => item.StudentName ?? item.StudentId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.AssignmentId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var pagedItems = orderedItems
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToArray();
        var data = new ListarEntregasCorrigiveisResponse(
            safePage,
            safePageSize,
            orderedItems.Length,
            safePage * safePageSize < orderedItems.Length,
            new EntregaCorrigivelContadores(
                orderedItems.Length,
                orderedItems.Count(item => item.NeedsGrading),
                orderedItems.Count(item => item.Submitted),
                orderedItems.Count(item => !item.Submitted),
                orderedItems.Count(item => item.Late)),
            new EntregaCorrigivelPermissoes(
                CanCreateBatch: true,
                CanCommitToMoodle: false,
                CommitStatus: "requires_sandbox_validation",
                CommitReason: "A leitura snapshot-only nao executa escrita no Moodle."),
            failedAssignments,
            pagedItems);

        var warnings = failedAssignments
            .Select(failure => $"{failure.AssignmentId}: {failure.Message}")
            .ToList();
        if (snapshot.IsStale)
        {
            warnings.Add("O snapshot está desatualizado; a atualização foi apenas agendada e nenhum dado live foi buscado nesta chamada.");
        }

        var freshness = new ToolFreshness(
            "snapshot",
            snapshot.UpdatedAt,
            Math.Max(0, (long)(DateTimeOffset.UtcNow - snapshot.UpdatedAt).TotalSeconds),
            snapshot.IsStale,
            refreshQueued,
            snapshot.IsComplete && failedAssignments.Count == 0,
            snapshot.RecordCount);
        var response = new ToolResponse<ListarEntregasCorrigiveisResponse>(
            failedAssignments.Count == 0 ? "ok" : "partial_failure",
            data,
            warnings,
            AuditId: null,
            DateTimeOffset.UtcNow,
            Freshness: freshness);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildListarEntregasCorrigiveisNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private async Task<CallToolResult> GetBatchStatusCoreAsync(
        Guid batchJobId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (batchJobId == Guid.Empty)
        {
            return ToolResultHelper.Error<AssistedGradingBatchStatusResult>("Informe um identificador de lote valido.");
        }

        AssistedGradingBatchStatusResult data;
        try
        {
            data = await mediator.Send(
                new GetAssistedGradingBatchStatusQuery(batchJobId, page, pageSize),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ToolResultHelper.Error<AssistedGradingBatchStatusResult>("Nao foi possivel consultar o lote de correcao assistida neste momento.");
        }

        var response = new ToolResponse<AssistedGradingBatchStatusResult>(
            "ok",
            data,
            [],
            AuditId: null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildBatchStatusNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private async Task<CallToolResult> GetCoordinationReportCoreAsync(
        Guid batchJobId,
        CancellationToken cancellationToken)
    {
        if (batchJobId == Guid.Empty)
        {
            return ToolResultHelper.Error<AssistedGradingCoordinationReportResult>("Informe um identificador de lote valido.");
        }

        AssistedGradingCoordinationReportResult data;
        try
        {
            data = await mediator.Send(
                new GetAssistedGradingCoordinationReportQuery(batchJobId),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            return ToolResultHelper.Error<AssistedGradingCoordinationReportResult>(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ToolResultHelper.Error<AssistedGradingCoordinationReportResult>(ex.Message);
        }
        catch
        {
            return ToolResultHelper.Error<AssistedGradingCoordinationReportResult>("Nao foi possivel exportar o relatorio consolidado de correcao neste momento.");
        }

        var response = new ToolResponse<AssistedGradingCoordinationReportResult>(
            "ok",
            data,
            [],
            AuditId: null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildCoordinationReportNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private async Task<CallToolResult> CancelBatchCoreAsync(
        Guid batchJobId,
        CancellationToken cancellationToken)
    {
        if (batchJobId == Guid.Empty)
        {
            return ToolResultHelper.Error<CancelAssistedGradingBatchResult>("Informe um identificador de lote valido.");
        }

        CancelAssistedGradingBatchResult data;
        try
        {
            data = await mediator.Send(
                new CancelAssistedGradingBatchCommand(batchJobId),
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
        catch
        {
            return ToolResultHelper.Error<CancelAssistedGradingBatchResult>("Nao foi possivel cancelar o lote de correcao assistida neste momento.");
        }

        var response = new ToolResponse<CancelAssistedGradingBatchResult>(
            "ok",
            data,
            [],
            AuditId: null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildCancelBatchNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private async Task<CallToolResult> GetGradingItemCoreAsync(
        Guid gradingItemId,
        Guid? batchJobId,
        CancellationToken cancellationToken)
    {
        if (gradingItemId == Guid.Empty)
        {
            return ToolResultHelper.Error<AssistedGradingItemDetailResult>("Informe um identificador de item valido.");
        }

        AssistedGradingItemDetailResult data;
        try
        {
            data = await mediator.Send(
                new GetAssistedGradingItemQuery(gradingItemId, batchJobId),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            return ToolResultHelper.Error<AssistedGradingItemDetailResult>(ex.Message);
        }
        catch
        {
            return ToolResultHelper.Error<AssistedGradingItemDetailResult>("Nao foi possivel consultar o item de correcao assistida neste momento.");
        }

        var response = new ToolResponse<AssistedGradingItemDetailResult>(
            "ok",
            data,
            [],
            AuditId: null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildGradingItemNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private async Task<CallToolResult> UpdateDraftCoreAsync(
        Guid gradingItemId,
        decimal? finalGrade,
        string finalFeedback,
        string teacherDecision,
        string? reviewNotes,
        string expectedReviewStatus,
        string? expectedDraftVersionHash,
        CancellationToken cancellationToken)
    {
        if (gradingItemId == Guid.Empty)
        {
            return ToolResultHelper.Error<AssistedGradingItemDetailResult>("Informe um identificador de item valido.");
        }

        if (string.IsNullOrWhiteSpace(finalFeedback))
        {
            return ToolResultHelper.Error<AssistedGradingItemDetailResult>("Informe o feedback final revisado.");
        }

        if (string.IsNullOrWhiteSpace(teacherDecision))
        {
            return ToolResultHelper.Error<AssistedGradingItemDetailResult>("Informe a decisao do professor/tutor.");
        }

        AssistedGradingItemDetailResult data;
        try
        {
            data = await mediator.Send(
                new UpdateAssistedGradingDraftCommand(
                    gradingItemId,
                    finalGrade,
                    finalFeedback,
                    teacherDecision,
                    reviewNotes,
                    expectedReviewStatus,
                    expectedDraftVersionHash),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            return ToolResultHelper.Error<AssistedGradingItemDetailResult>(ex.Message);
        }
        catch
        {
            return ToolResultHelper.Error<AssistedGradingItemDetailResult>("Nao foi possivel atualizar o rascunho de correcao neste momento.");
        }

        var response = new ToolResponse<AssistedGradingItemDetailResult>(
            "ok",
            data,
            [],
            AuditId: null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildUpdateDraftNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private async Task<CallToolResult> CreateLaunchPreviewCoreAsync(
        Guid batchJobId,
        IReadOnlyList<Guid> gradingItemIds,
        bool onlyReviewed,
        bool allowOverwriteExisting,
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
                new CreateGradingLaunchPreviewCommand(batchJobId, gradingItemIds, onlyReviewed, allowOverwriteExisting),
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
                new ConfirmMoodleBatchLaunchCommand(pendingActionId, confirmationText),
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
            data.FailedItems == 0 ? "ok" : "partial_failure",
            data,
            data.Failures.Select(failure => failure.Message).ToArray(),
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

    private async Task<CallToolResult> GetAuditCoreAsync(
        string auditId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(auditId))
        {
            return ToolResultHelper.Error<GradingAuditResult>("Informe um auditId valido.");
        }

        GradingAuditResult data;
        try
        {
            data = await mediator.Send(
                new GetGradingAuditQuery(auditId, page, pageSize),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ToolResultHelper.Error<GradingAuditResult>("Nao foi possivel consultar a auditoria de correcao neste momento.");
        }

        var response = new ToolResponse<GradingAuditResult>(
            "ok",
            data,
            [],
            data.AuditId,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildAuditNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private async Task<CallToolResult> GetAuditByBatchCoreAsync(
        Guid batchJobId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (batchJobId == Guid.Empty)
        {
            return ToolResultHelper.Error<GradingAuditResult>("Informe um batchJobId valido.");
        }

        GradingAuditResult data;
        try
        {
            data = await mediator.Send(
                new GetGradingBatchAuditQuery(batchJobId, page, pageSize),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ToolResultHelper.Error<GradingAuditResult>("Nao foi possivel consultar a auditoria de correcao por lote neste momento.");
        }

        var response = new ToolResponse<GradingAuditResult>(
            "ok",
            data,
            [],
            AuditId: null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = BuildAuditNarration(data) }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private static DiscoverMoodleGradingFunctionsResponse ToResponse(
        MoodleGradingCapabilitiesReport report)
    {
        return new DiscoverMoodleGradingFunctionsResponse(
            report.ServiceName,
            report.CheckedAt,
            report.Functions.Select(function => new MoodleFunctionStatus(
                function.Name,
                function.Purpose,
                function.Available)).ToArray(),
            report.CanReadSubmissions,
            report.CanReadGrades,
            report.CanReadFiles,
            report.CanWriteIndividualGrades,
            report.CanWriteBatchGrades,
            report.MissingFunctions);
    }

    private static IReadOnlyList<string> BuildWarnings(
        DiscoverMoodleGradingFunctionsResponse response)
    {
        if (response.MissingFunctions.Count == 0)
        {
            return [];
        }

        return [$"Funcoes ausentes no servico Moodle atual: {string.Join(", ", response.MissingFunctions)}."];
    }

    private static string BuildNarration(DiscoverMoodleGradingFunctionsResponse response)
    {
        var availableCount = response.Functions.Count(function => function.Available);
        var total = response.Functions.Count;
        var writeStatus = response.CanWriteIndividualGrades
            ? "envio individual de nota parece disponivel"
            : "envio individual de nota nao foi detectado";

        return $"Verifiquei {total} funcao(oes) Moodle relevantes para correcao assistida: {availableCount} disponivel(is); {writeStatus}.";
    }

    private static string BuildTechnicalDiscoveryNarration(GradingTechnicalDiscoveryReport response)
    {
        var blockerSuffix = response.BlockingIssues.Count > 0
            ? $" Bloqueios: {string.Join("; ", response.BlockingIssues)}"
            : " Sem bloqueios automaticos; falta prova em Moodle real.";
        return $"Descoberta tecnica da correcao: status {response.OverallStatus}, token {response.WriteToken.Mode}.{blockerSuffix}";
    }

    private static string BuildCreateBatchNarration(CreateAssistedGradingBatchResult response)
    {
        if (response.BatchJobId == Guid.Empty)
        {
            return "Nenhuma entrega aguardando correcao foi encontrada; nenhum lote foi criado.";
        }

        return $"Lote de correcao assistida criado com {response.AcceptedItems} item(ns) aceito(s). BatchJobId: {response.BatchJobId}.";
    }

    private static string BuildStartPendingGradingRunNarration(StartPendingGradingRunResult response)
    {
        if (response.Batches.Count == 0)
        {
            return $"Nenhuma entrega pendente elegivel foi encontrada em {response.CoursesScanned} curso(s).";
        }

        return $"Fluxo de correcoes pendentes iniciado: {response.TotalItems} entrega(s) em {response.Batches.Count} curso(s). " +
               "Prossiga pelos batchJobIds retornados, sem interromper o fluxo pelos cursos ou itens bloqueados.";
    }

    private static string BuildListarEntregasCorrigiveisNarration(ListarEntregasCorrigiveisResponse response)
    {
        var suffix = response.HasMore ? " Ha mais entregas para consultar." : string.Empty;
        return $"Entregas corrigiveis: {response.Items.Count} item(ns) nesta pagina de {response.TotalItems} total(is).{suffix}";
    }

    private static string BuildBatchStatusNarration(AssistedGradingBatchStatusResult response)
    {
        var suffix = response.HasMore ? " Ha mais itens para consultar." : string.Empty;
        var metrics = response.ProcessingMetrics;
        var canLaunchNote = metrics.CanLaunch ? " Pronto para lancamento." : string.Empty;
        var awaitingAiCount = response.Items.Count(item => item.Status == "AwaitingAiAnalysis");
        var awaitingAiNote = awaitingAiCount > 0
            ? $" {awaitingAiCount} item(ns) aguardam analise da IA. Use preparar_lote_correcao_ia para gerar nota e feedback."
            : string.Empty;
        return $"Lote {response.BatchJobId}: status {response.Status}, {response.Items.Count} item(ns) nesta pagina de {response.TotalItems} total(is). Prontos: {response.ReadyItems}, bloqueados: {response.BlockedItems}, falhos: {response.FailedItems}, progresso: {metrics.ProgressPercent}%.{awaitingAiNote}{canLaunchNote}{suffix}";
    }

    private static string BuildCoordinationReportNarration(AssistedGradingCoordinationReportResult response)
    {
        var awaitingAi = response.StatusCounts.TryGetValue("AwaitingAiAnalysis", out var count) ? count : 0;
        var awaitingNote = awaitingAi > 0
            ? $" {awaitingAi} item(ns) aguardam analise da IA."
            : string.Empty;
        return $"Relatorio consolidado do lote {response.BatchJobId}: {response.TotalItems} item(ns), {response.ReviewedItems} revisado(s), {response.PendingReviewItems} com revisao pendente, {response.AttentionItems.Count} item(ns) exigem atencao.{awaitingNote}";
    }

    private static string BuildPendingGradingRunReportNarration(PendingGradingRunReportResult response)
    {
        return $"Relatorio final de correcoes pendentes: {response.CorrectedCount} entrega(s) corrigida(s) e lancada(s) no Moodle; " +
               $"{response.NotCorrectedCount} nao corrigida(s) com motivo para ajuste manual.";
    }

    private static string BuildCancelBatchNarration(CancelAssistedGradingBatchResult response)
    {
        return $"Lote {response.BatchJobId}: {response.Message}";
    }

    private static string BuildGradingItemNarration(AssistedGradingItemDetailResult response)
    {
        return $"Item {response.GradingItemId}: estudante {response.StudentId}, status {response.Status}, revisao {response.ReviewStatus}.";
    }

    private static string BuildUpdateDraftNarration(AssistedGradingItemDetailResult response)
    {
        return $"Rascunho do item {response.GradingItemId} atualizado para revisao {response.ReviewStatus}.";
    }

    private static string BuildLaunchPreviewNarration(CreateGradingLaunchPreviewResult response)
    {
        if (response.PendingActionId == Guid.Empty)
        {
            return "Nenhum item pronto para lancamento foi encontrado.";
        }

        return $"Previa de lancamento criada com {response.ReadyItems} item(ns). Para confirmar, envie exatamente: {response.ConfirmationText}";
    }

    private static string BuildConfirmLaunchNarration(ConfirmMoodleBatchLaunchResult response)
    {
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

        if (reasons.Length == 0)
        {
            return summary;
        }

        var suffix = reasons.Length > 2 ? " Ha outros motivos no resultado estruturado." : string.Empty;
        return $"{summary} Motivo(s): {string.Join("; ", reasons.Take(2))}.{suffix}";
    }

    private static string BuildAuditNarration(GradingAuditResult response)
    {
        var scope = response.AuditId is not null
            ? $"Auditoria {response.AuditId}"
            : $"Auditoria do lote {response.BatchJobId}";
        var suffix = response.HasMore ? " Ha mais eventos para consultar." : string.Empty;
        return $"{scope}: {response.Events.Count} evento(s) nesta pagina de {response.TotalEvents} total(is).{suffix}";
    }

    private static string BuildPrepareAiBatchNarration(AiGradingBatchPackageResult data)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Pacote IA — Lote {data.BatchJobId}");
        sb.AppendLine();
        sb.AppendLine($"**{data.TotalItems} aluno(s)** com dados extraidos para correcao.");
        sb.AppendLine();

        foreach (var item in data.Items)
        {
            var textInfo = string.IsNullOrWhiteSpace(item.ExtractedText)
                ? "sem texto extraido"
                : item.TextTruncated
                    ? $"texto truncado ({item.ExtractedText.Length} chars)"
                    : $"texto completo ({item.ExtractedText.Length} chars)";
            var gradeInfo = item.MaxGrade > 0
                ? $"nota maxima: {item.MaxGrade}"
                : "nota maxima: nao confirmada (sugestao numerica bloqueada)";
            sb.AppendLine($"- **Aluno {item.StudentId}** (item {item.GradingItemId}): {textInfo}, {gradeInfo}");
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
        return sb.ToString();
    }



    public sealed record DiscoverMoodleGradingFunctionsResponse(
        [property: JsonPropertyName("serviceName")] string ServiceName,
        [property: JsonPropertyName("checkedAt")] DateTimeOffset CheckedAt,
        [property: JsonPropertyName("functions")] IReadOnlyList<MoodleFunctionStatus> Functions,
        [property: JsonPropertyName("canReadSubmissions")] bool CanReadSubmissions,
        [property: JsonPropertyName("canReadGrades")] bool CanReadGrades,
        [property: JsonPropertyName("canReadFiles")] bool CanReadFiles,
        [property: JsonPropertyName("canWriteIndividualGrades")] bool CanWriteIndividualGrades,
        [property: JsonPropertyName("canWriteBatchGrades")] bool CanWriteBatchGrades,
        [property: JsonPropertyName("missingFunctions")] IReadOnlyList<string> MissingFunctions);

    public sealed record MoodleFunctionStatus(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("purpose")] string Purpose,
        [property: JsonPropertyName("available")] bool Available);

    public sealed record ListarEntregasCorrigiveisResponse(
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("perPage")] int PerPage,
        [property: JsonPropertyName("totalItems")] int TotalItems,
        [property: JsonPropertyName("hasMore")] bool HasMore,
        [property: JsonPropertyName("counters")] EntregaCorrigivelContadores Counters,
        [property: JsonPropertyName("permissions")] EntregaCorrigivelPermissoes Permissions,
        [property: JsonPropertyName("failedAssignments")] IReadOnlyList<EntregaCorrigivelFalha> FailedAssignments,
        [property: JsonPropertyName("items")] IReadOnlyList<EntregaCorrigivelItem> Items);

    public sealed record EntregaCorrigivelContadores(
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("awaitingGrading")] int AwaitingGrading,
        [property: JsonPropertyName("submitted")] int Submitted,
        [property: JsonPropertyName("notSubmitted")] int NotSubmitted,
        [property: JsonPropertyName("late")] int Late);

    public sealed record EntregaCorrigivelPermissoes(
        [property: JsonPropertyName("canCreateBatch")] bool CanCreateBatch,
        [property: JsonPropertyName("canCommitToMoodle")] bool CanCommitToMoodle,
        [property: JsonPropertyName("commitStatus")] string CommitStatus,
        [property: JsonPropertyName("commitReason")] string CommitReason);

    public sealed record EntregaCorrigivelFalha(
        [property: JsonPropertyName("assignmentId")] string AssignmentId,
        [property: JsonPropertyName("errorCode")] string ErrorCode,
        [property: JsonPropertyName("message")] string Message);

    public sealed record EntregaCorrigivelItem(
        [property: JsonPropertyName("courseId")] string CourseId,
        [property: JsonPropertyName("assignmentId")] string AssignmentId,
        [property: JsonPropertyName("submissionId")] string? SubmissionId,
        [property: JsonPropertyName("studentId")] string StudentId,
        [property: JsonPropertyName("studentName")] string? StudentName,
        [property: JsonPropertyName("submissionStatus")] string SubmissionStatus,
        [property: JsonPropertyName("gradingStatus")] string? GradingStatus,
        [property: JsonPropertyName("submitted")] bool Submitted,
        [property: JsonPropertyName("needsGrading")] bool NeedsGrading,
        [property: JsonPropertyName("late")] bool Late,
        [property: JsonPropertyName("attemptNumber")] int? AttemptNumber,
        [property: JsonPropertyName("submittedAt")] DateTimeOffset? SubmittedAt,
        [property: JsonPropertyName("modifiedAt")] DateTimeOffset? ModifiedAt,
        [property: JsonPropertyName("fileCount")] int FileCount,
        [property: JsonPropertyName("hasOnlineText")] bool HasOnlineText);

    private static bool TryParseSubmissionFilter(string? value, out AssignmentSubmissionFilter filter)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            filter = AssignmentSubmissionFilter.All;
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "all":
            case "todos":
                filter = AssignmentSubmissionFilter.All;
                return true;
            case "submitted":
            case "entregues":
                filter = AssignmentSubmissionFilter.Submitted;
                return true;
            case "pending":
            case "pendentes":
                filter = AssignmentSubmissionFilter.NotSubmitted;
                return true;
            case "late":
            case "atrasadas":
                filter = AssignmentSubmissionFilter.Late;
                return true;
            case "awaiting_grading":
            case "aguardando_correcao":
                filter = AssignmentSubmissionFilter.NeedsGrading;
                return true;
            default:
                filter = AssignmentSubmissionFilter.All;
                return false;
        }
    }

    // ============================================================
    // Tool: preparar_correcao_entrega
    // ============================================================

    [McpServerTool(
        Name = "prepare_submission_grading",
        Title = "Prepare Submission Grading",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GradingContextForChatResult>))]
    [Description("Retorna o contexto completo de uma entrega para correcao: enunciado da atividade, texto da entrega do aluno, nota maxima e instrucoes. Use este contexto para gerar feedback e nota sugerida diretamente no chat. Nao escreve no Moodle.")]
    public async Task<CallToolResult> PrepararCorrecaoEntregaAsync(
        [Description("Identificador do item retornado pelo status do lote.")]
        Guid gradingItemId,
        [Description("Identificador opcional do lote para validar vinculo.")]
        Guid? batchJobId = null,
        CancellationToken cancellationToken = default)
    {
        if (gradingItemId == Guid.Empty)
        {
            return ToolResultHelper.Error<GradingContextForChatResult>("Informe um identificador de item valido.");
        }

        GradingContextForChatResult data;
        try
        {
            data = await mediator.Send(
                new PrepareGradingContextForChatQuery(gradingItemId, batchJobId),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            return ToolResultHelper.Error<GradingContextForChatResult>(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ToolResultHelper.Error<GradingContextForChatResult>(ex.Message);
        }
        catch
        {
            return ToolResultHelper.Error<GradingContextForChatResult>("Nao foi possivel preparar o contexto de correcao neste momento.");
        }

        var narration = BuildPrepareGradingNarration(data);
        var response = new ToolResponse<GradingContextForChatResult>(
            "ok",
            data,
            data.Warnings,
            AuditId: null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = narration }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = false
        };
    }

    private static string BuildPrepareGradingNarration(GradingContextForChatResult data)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Contexto para Correcao — {data.AssignmentName ?? $"Tarefa {data.AssignmentId}"}");
        sb.AppendLine();
        sb.AppendLine(data.MaxGrade > 0
            ? $"**Nota maxima:** {data.MaxGrade} pontos"
            : "**Nota maxima:** nao confirmada — sugestao numerica bloqueada");
        sb.AppendLine($"**Aluno (ID):** {data.StudentId}");
        sb.AppendLine($"**Item ID:** {data.GradingItemId}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(data.AssignmentStatement))
        {
            var preview = data.AssignmentStatement.Length > 500
                ? data.AssignmentStatement[..500] + "..."
                : data.AssignmentStatement;
            sb.AppendLine("### Enunciado da Atividade (resumo)");
            sb.AppendLine(preview);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(data.StudentSubmission))
        {
            var preview = data.StudentSubmission.Length > 500
                ? data.StudentSubmission[..500] + "..."
                : data.StudentSubmission;
            sb.AppendLine("### Entrega do Aluno (resumo)");
            sb.AppendLine(preview);
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("### Entrega do Aluno");
            sb.AppendLine("*Texto nao disponivel — verificar anexos.*");
            sb.AppendLine();
        }

        if (data.SuggestedGrade != null)
        {
            sb.AppendLine($"**Nota sugerida pelo motor heuristico:** {data.SuggestedGrade} (confianca: {data.Confidence:P0})");
        }

        sb.AppendLine();
        sb.AppendLine(data.Instructions);

        return sb.ToString();
    }
}

public sealed record ReviewedGradingDraftInput(
    [property: JsonPropertyName("gradingItemId")] Guid GradingItemId,
    [property: JsonPropertyName("finalGrade")] decimal? FinalGrade,
    [property: JsonPropertyName("finalFeedback")] string FinalFeedback,
    [property: JsonPropertyName("teacherDecision")] string TeacherDecision,
    [property: JsonPropertyName("reviewNotes")] string? ReviewNotes = null,
    [property: JsonPropertyName("expectedReviewStatus")] string ExpectedReviewStatus = "NotReviewed",
    [property: JsonPropertyName("expectedDraftVersionHash")] string? ExpectedDraftVersionHash = null);

public sealed record BatchDraftUpdateFailure(
    [property: JsonPropertyName("gradingItemId")] Guid GradingItemId,
    [property: JsonPropertyName("message")] string Message);

public sealed record BatchDraftUpdateResult(
    [property: JsonPropertyName("savedItems")] int SavedItems,
    [property: JsonPropertyName("failedItems")] int FailedItems,
    [property: JsonPropertyName("savedItemIds")] IReadOnlyList<Guid> SavedItemIds,
    [property: JsonPropertyName("failures")] IReadOnlyList<BatchDraftUpdateFailure> Failures);
