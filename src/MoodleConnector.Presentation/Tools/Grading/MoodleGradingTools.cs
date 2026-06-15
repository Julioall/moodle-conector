using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Grading;
using MoodleConnector.Application.Submissions;
using MoodleConnector.Application.Tools;
using MoodleConnector.Domain;

namespace MoodleConnector.Presentation.Tools.Grading;

[McpServerToolType]
public sealed class MoodleGradingTools(
    IMediator mediator,
    IMoodleConnectionSelection moodleSelection,
    IMoodleUserResolver moodleUserResolver)
{
    [McpServerTool(
        Name = "descobrir_funcoes_moodle_correcao",
        Title = "Descobrir Funcoes Moodle Correcao",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<DiscoverMoodleGradingFunctionsResponse>))]
    [Description("Verifica quais funcoes Moodle necessarias para correcao assistida estao habilitadas no servico atual. Nao baixa entregas nem executa escrita.")]
    public Task<CallToolResult> DescobrirFuncoesMoodleCorrecaoAsync(
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrao do usuario.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return DiscoverCoreAsync(moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "discover_moodle_grading_functions",
        Title = "Discover Moodle Grading Functions",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<DiscoverMoodleGradingFunctionsResponse>))]
    [Description("Checks which Moodle web service functions required for assisted grading are enabled in the current service. Does not download submissions or write grades.")]
    public Task<CallToolResult> DiscoverMoodleGradingFunctionsAsync(
        [Description("Moodle connection alias to query. When omitted, uses the user's default Moodle connection.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return DiscoverCoreAsync(moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "executar_descoberta_tecnica_correcao",
        Title = "Executar Descoberta Tecnica Correcao",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GradingTechnicalDiscoveryReport>))]
    [Description("Consolida a descoberta tecnica da correcao assistida: funcoes Moodle, anexos, mod_assign_save_grade, permissao de escrita, rubricas/escalas e modo de token. Nao baixa arquivos nem escreve no Moodle.")]
    public Task<CallToolResult> ExecutarDescobertaTecnicaCorrecaoAsync(
        [Description("Alias do Moodle a consultar. Quando omitido, usa o Moodle padrao do usuario.")]
        string? moodleAlias = null,
        CancellationToken cancellationToken = default)
    {
        return TechnicalDiscoveryCoreAsync(moodleAlias, cancellationToken);
    }

    [McpServerTool(
        Name = "listar_entregas_corrigiveis",
        Title = "Listar Entregas Corrigiveis",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<ListarEntregasCorrigiveisResponse>))]
    [Description("Lista entregas corrigiveis de uma ou mais tarefas, com contadores e paginação agregada para preparo de lote.")]
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
        Name = "criar_lote_correcao_assistida",
        Title = "Criar Lote Correcao Assistida",
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
        [Description("Identificadores das tarefas Moodle. Devem ser numericos nesta versao inicial.")]
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
            cancellationToken);
    }

    [McpServerTool(
        Name = "consultar_status_lote_correcao",
        Title = "Consultar Status Lote Correcao",
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
        Name = "exportar_relatorio_correcao_coordenacao",
        Title = "Exportar Relatorio Correcao Coordenacao",
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
        Name = "cancelar_lote_correcao_assistida",
        Title = "Cancelar Lote Correcao Assistida",
        ReadOnly = false,
        Destructive = false,
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
        Name = "consultar_item_correcao_assistida",
        Title = "Consultar Item Correcao Assistida",
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
        Name = "atualizar_rascunho_correcao",
        Title = "Atualizar Rascunho Correcao",
        ReadOnly = false,
        Destructive = false,
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
        CancellationToken cancellationToken = default)
    {
        return UpdateDraftCoreAsync(
            gradingItemId,
            finalGrade,
            finalFeedback,
            teacherDecision,
            reviewNotes,
            expectedReviewStatus,
            cancellationToken);
    }

    [McpServerTool(
        Name = "criar_previa_lancamento_lote",
        Title = "Criar Previa Lancamento Lote",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<CreateGradingLaunchPreviewResult>))]
    [Description("Cria uma acao pendente com previa revisavel para lancar nota e feedback no Moodle. Nao executa escrita oficial.")]
    public Task<CallToolResult> CriarPreviaLancamentoLoteAsync(
        [Description("Identificador do lote de correcao assistida.")]
        Guid batchJobId,
        [Description("Itens especificos a incluir. Quando vazio, inclui todos os itens prontos do lote.")]
        Guid[]? gradingItemIds = null,
        [Description("Quando true, inclui apenas itens revisados.")]
        bool onlyReviewed = true,
        CancellationToken cancellationToken = default)
    {
        return CreateLaunchPreviewCoreAsync(
            batchJobId,
            gradingItemIds ?? [],
            onlyReviewed,
            cancellationToken);
    }

    [McpServerTool(
        Name = "confirmar_lancamento_lote_moodle",
        Title = "Confirmar Lancamento Lote Moodle",
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
        Name = "consultar_auditoria_correcao",
        Title = "Consultar Auditoria Correcao",
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
        Name = "consultar_auditoria_correcao_lote",
        Title = "Consultar Auditoria Correcao Lote",
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

    private async Task<CallToolResult> DiscoverCoreAsync(
        string? moodleAlias,
        CancellationToken cancellationToken)
    {
        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return Error<DiscoverMoodleGradingFunctionsResponse>("Usuario nao autenticado para descobrir funcoes de correcao.");
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
            return Error<DiscoverMoodleGradingFunctionsResponse>("Nao foi possivel consultar as funcoes Moodle de correcao neste momento.");
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
            return Error<GradingTechnicalDiscoveryReport>("Usuario nao autenticado para executar descoberta tecnica de correcao.");
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
            return Error<GradingTechnicalDiscoveryReport>("Nao foi possivel executar a descoberta tecnica de correcao neste momento.");
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
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return Error<CreateAssistedGradingBatchResult>("Informe um identificador de curso.");
        }

        if (assignmentIds.Count == 0 || assignmentIds.All(string.IsNullOrWhiteSpace))
        {
            return Error<CreateAssistedGradingBatchResult>("Informe pelo menos uma tarefa.");
        }

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return Error<CreateAssistedGradingBatchResult>("Usuario nao autenticado para criar lote de correcao.");
        }

        CreateAssistedGradingBatchResult data;
        try
        {
            data = await mediator.Send(
                new CreateAssistedGradingBatchCommand(
                    moodleUserId.Value.ToString(),
                    courseId,
                    assignmentIds,
                    submissionIds,
                    maxItems,
                    onlyAwaitingGrading,
                    includeRubric,
                    includeSubmissionFiles,
                    includeCourseMaterials,
                    teacherInstructions,
                    priority),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Error<CreateAssistedGradingBatchResult>("Nao foi possivel criar o lote de correcao assistida neste momento.");
        }

        var response = new ToolResponse<CreateAssistedGradingBatchResult>(
            "ok",
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
            return Error<ListarEntregasCorrigiveisResponse>("Informe um identificador de curso.");
        }

        var normalizedAssignmentIds = assignmentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedAssignmentIds.Length == 0)
        {
            return Error<ListarEntregasCorrigiveisResponse>("Informe pelo menos uma tarefa para listar entregas corrigiveis.");
        }

        if (!TryParseSubmissionFilter(status, out var parsedFilter))
        {
            return Error<ListarEntregasCorrigiveisResponse>("Filtro de status invalido. Use all, submitted, pending, late ou awaiting_grading.");
        }

        var filter = onlyAwaitingGrading ? AssignmentSubmissionFilter.NeedsGrading : parsedFilter;
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(perPage, 1, 100);

        moodleSelection.Alias = moodleAlias;
        var moodleUserId = await moodleUserResolver.ResolveMoodleUserIdAsync(cancellationToken);
        if (moodleUserId is null)
        {
            return Error<ListarEntregasCorrigiveisResponse>("Usuario nao autenticado para listar entregas corrigiveis.");
        }

        var items = new List<EntregaCorrigivelItem>();
        var warnings = new List<string>();

        foreach (var assignmentId in normalizedAssignmentIds)
        {
            var requestPage = 1;
            while (true)
            {
                AssignmentSubmissionsPage? submissions;
                try
                {
                    submissions = await mediator.Send(
                        new ListAssignmentSubmissionsQuery(
                            moodleUserId.Value.ToString(),
                            courseId,
                            assignmentId,
                            filter,
                            requestPage,
                            100,
                            Since: null,
                            Before: null,
                            IncludeLate: includeLate,
                            IncludeUngraded: true),
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    warnings.Add($"Nao foi possivel listar entregas da tarefa {assignmentId} neste momento.");
                    break;
                }

                if (submissions is null)
                {
                    warnings.Add($"Tarefa {assignmentId} nao encontrada para o usuario atual.");
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
                CanCommitToMoodle: false),
            pagedItems);

        var response = new ToolResponse<ListarEntregasCorrigiveisResponse>(
            "ok",
            data,
            warnings,
            AuditId: null,
            DateTimeOffset.UtcNow);

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
            return Error<AssistedGradingBatchStatusResult>("Informe um identificador de lote valido.");
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
            return Error<AssistedGradingBatchStatusResult>("Nao foi possivel consultar o lote de correcao assistida neste momento.");
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
            return Error<AssistedGradingCoordinationReportResult>("Informe um identificador de lote valido.");
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
            return Error<AssistedGradingCoordinationReportResult>(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Error<AssistedGradingCoordinationReportResult>(ex.Message);
        }
        catch
        {
            return Error<AssistedGradingCoordinationReportResult>("Nao foi possivel exportar o relatorio consolidado de correcao neste momento.");
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
            return Error<CancelAssistedGradingBatchResult>("Informe um identificador de lote valido.");
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
            return Error<CancelAssistedGradingBatchResult>(ex.Message);
        }
        catch
        {
            return Error<CancelAssistedGradingBatchResult>("Nao foi possivel cancelar o lote de correcao assistida neste momento.");
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
            return Error<AssistedGradingItemDetailResult>("Informe um identificador de item valido.");
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
            return Error<AssistedGradingItemDetailResult>(ex.Message);
        }
        catch
        {
            return Error<AssistedGradingItemDetailResult>("Nao foi possivel consultar o item de correcao assistida neste momento.");
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
        CancellationToken cancellationToken)
    {
        if (gradingItemId == Guid.Empty)
        {
            return Error<AssistedGradingItemDetailResult>("Informe um identificador de item valido.");
        }

        if (string.IsNullOrWhiteSpace(finalFeedback))
        {
            return Error<AssistedGradingItemDetailResult>("Informe o feedback final revisado.");
        }

        if (string.IsNullOrWhiteSpace(teacherDecision))
        {
            return Error<AssistedGradingItemDetailResult>("Informe a decisao do professor/tutor.");
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
                    expectedReviewStatus),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            return Error<AssistedGradingItemDetailResult>(ex.Message);
        }
        catch
        {
            return Error<AssistedGradingItemDetailResult>("Nao foi possivel atualizar o rascunho de correcao neste momento.");
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
        CancellationToken cancellationToken)
    {
        if (batchJobId == Guid.Empty)
        {
            return Error<CreateGradingLaunchPreviewResult>("Informe um identificador de lote valido.");
        }

        CreateGradingLaunchPreviewResult data;
        try
        {
            data = await mediator.Send(
                new CreateGradingLaunchPreviewCommand(batchJobId, gradingItemIds, onlyReviewed),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Error<CreateGradingLaunchPreviewResult>("Nao foi possivel criar a previa de lancamento neste momento.");
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
            IsError = data.PendingActionId == Guid.Empty
        };
    }

    private async Task<CallToolResult> ConfirmLaunchCoreAsync(
        Guid pendingActionId,
        string confirmationText,
        CancellationToken cancellationToken)
    {
        if (pendingActionId == Guid.Empty)
        {
            return Error<ConfirmMoodleBatchLaunchResult>("Informe uma acao pendente valida.");
        }

        if (string.IsNullOrWhiteSpace(confirmationText))
        {
            return Error<ConfirmMoodleBatchLaunchResult>("Informe o texto literal de confirmacao.");
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
            return Error<ConfirmMoodleBatchLaunchResult>(ex.Message);
        }
        catch
        {
            return Error<ConfirmMoodleBatchLaunchResult>("Nao foi possivel confirmar o lancamento no Moodle neste momento.");
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
            IsError = data.SentItems == 0 && data.FailedItems > 0
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
            return Error<GradingAuditResult>("Informe um auditId valido.");
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
            return Error<GradingAuditResult>("Nao foi possivel consultar a auditoria de correcao neste momento.");
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
            return Error<GradingAuditResult>("Informe um batchJobId valido.");
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
            return Error<GradingAuditResult>("Nao foi possivel consultar a auditoria de correcao por lote neste momento.");
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
        return $"Lote de correcao assistida criado com {response.AcceptedItems} item(ns) aceito(s). BatchJobId: {response.BatchJobId}.";
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
        return $"Lote {response.BatchJobId}: status {response.Status}, {response.Items.Count} item(ns) nesta pagina de {response.TotalItems} total(is). Prontos: {response.ReadyItems}, bloqueados: {response.BlockedItems}, falhos: {response.FailedItems}, progresso: {metrics.ProgressPercent}%.{canLaunchNote}{suffix}";
    }

    private static string BuildCoordinationReportNarration(AssistedGradingCoordinationReportResult response)
    {
        return $"Relatorio consolidado do lote {response.BatchJobId}: {response.TotalItems} item(ns), {response.ReviewedItems} revisado(s), {response.PendingReviewItems} com revisao pendente, {response.AttentionItems.Count} item(ns) exigem atencao.";
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
        return $"Lancamento Moodle confirmado: {response.SentItems} enviado(s), {response.FailedItems} falha(s).";
    }

    private static string BuildAuditNarration(GradingAuditResult response)
    {
        var scope = response.AuditId is not null
            ? $"Auditoria {response.AuditId}"
            : $"Auditoria do lote {response.BatchJobId}";
        var suffix = response.HasMore ? " Ha mais eventos para consultar." : string.Empty;
        return $"{scope}: {response.Events.Count} evento(s) nesta pagina de {response.TotalEvents} total(is).{suffix}";
    }

    private static CallToolResult Error<T>(string message)
    {
        var response = new ToolResponse<T>(
            "error",
            Data: default,
            Warnings: [message],
            AuditId: null,
            DateTimeOffset.UtcNow);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = message }],
            StructuredContent = JsonSerializer.SerializeToElement(response),
            IsError = true
        };
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
        [property: JsonPropertyName("items")] IReadOnlyList<EntregaCorrigivelItem> Items);

    public sealed record EntregaCorrigivelContadores(
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("awaitingGrading")] int AwaitingGrading,
        [property: JsonPropertyName("submitted")] int Submitted,
        [property: JsonPropertyName("notSubmitted")] int NotSubmitted,
        [property: JsonPropertyName("late")] int Late);

    public sealed record EntregaCorrigivelPermissoes(
        [property: JsonPropertyName("canCreateBatch")] bool CanCreateBatch,
        [property: JsonPropertyName("canCommitToMoodle")] bool CanCommitToMoodle);

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
}
