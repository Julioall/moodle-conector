namespace MoodleConnector.Application.Configuration;

public sealed class GradingLimitsOptions
{
    public const string SectionName = "GradingLimits";

    public int MaxBatchItems { get; init; } = 400;

    public int MaxFileSizeMb { get; init; } = 25;

    /// <summary>Maximum binary size returned by a single MCP resource read.</summary>
    public int MaxResourceBytes { get; init; } = 25 * 1024 * 1024;

    /// <summary>
    /// Mantém os recursos das entregas disponíveis enquanto uma correção
    /// grande é revisada. A expiração do recurso é independente da expiração
    /// da ação de publicação.
    /// </summary>
    public int ResourceExpirationMinutes { get; init; } = 24 * 60;

    public int MaxConcurrentResourceDownloads { get; init; } = 4;

    public int MaxZipEntries { get; init; } = 100;

    public long MaxExtractedZipBytes { get; init; } = 100 * 1024 * 1024;

    public int MaxFilesPerSubmission { get; init; } = 10;

    public int MaxTextCharsPerSubmission { get; init; } = 120_000;

    public int MaxReviewItemsPerPage { get; init; } = 25;

    public int RawFileRetentionDays { get; init; } = 7;

    public int DraftRetentionDays { get; init; } = 180;

    public int MoodleMaxConcurrentRequests { get; init; } = 5;

    /// <summary>
    /// Limite legado por item. Os limites explícitos abaixo também protegem
    /// o lote e a conexão; manter os três permite uma migração de configuração
    /// sem aumentar a concorrência acidentalmente.
    /// </summary>
    public int FileDownloadWorkers { get; init; } = 4;

    public int MaxConcurrentDownloadsPerConnection { get; init; } = 4;

    public int MaxConcurrentDownloadsPerBatch { get; init; } = 4;

    public int ExtractionWorkers { get; init; } = 4;

    public int AiAnalysisWorkers { get; init; } = 3;

    /// <summary>
    /// Migração gradual: quando habilitado, o request cria somente o job e
    /// referências técnicas; downloads/extração/contexto ficam para o worker.
    /// </summary>
    public bool DeferHeavyIngestion { get; init; }

    public int BatchLeaseMinutes { get; init; } = 30;

    public int DurableBatchPollSeconds { get; init; } = 5;

    public int DurableBatchClaimSize { get; init; } = 8;

    /// <summary>
    /// Concorrência inicial do pool adaptativo. O pool pode subir ou descer
    /// entre BatchWorkerMinConcurrency e BatchWorkerMaxConcurrency.
    /// </summary>
    public int BatchWorkerConcurrency { get; init; } = 2;

    /// <summary>Ativa o ajuste automático conforme fila, CPU e memória.</summary>
    public bool AdaptiveBatchWorkerConcurrency { get; init; } = true;

    /// <summary>Menor número de slots mantidos pelo pool quando há demanda.</summary>
    public int BatchWorkerMinConcurrency { get; init; } = 1;

    /// <summary>
    /// Teto explícito do pool. Zero calcula o teto a partir de CPU e memória
    /// disponíveis no processo/container (com limite absoluto de 32).
    /// </summary>
    public int BatchWorkerMaxConcurrency { get; init; }

    /// <summary>Utilização alvo de CPU do processo, normalmente 80%.</summary>
    public int BatchWorkerCpuTargetPercent { get; init; } = 80;

    /// <summary>Utilização alvo de memória disponível, normalmente 80%.</summary>
    public int BatchWorkerMemoryTargetPercent { get; init; } = 80;

    /// <summary>Intervalo entre decisões de escala do pool.</summary>
    public int BatchWorkerScaleIntervalSeconds { get; init; } = 5;

    /// <summary>Tempo sem fila antes de reduzir slots até o mínimo.</summary>
    public int BatchWorkerIdleScaleDownSeconds { get; init; } = 30;

    /// <summary>
    /// Reserva conservadora de memória por lote para calcular o teto automático.
    /// </summary>
    public int BatchWorkerMemoryPerWorkerMb { get; init; } = 256;

    /// <summary>
    /// Evita que vários lotes do mesmo Moodle saturem a API enquanto permite
    /// que conexões independentes avancem em paralelo.
    /// </summary>
    public int BatchWorkerPerConnectionConcurrency { get; init; } = 2;

    /// <summary>
    /// Frequência do worker que retoma publicações autorizadas após queda ou
    /// timeout do request de confirmação.
    /// </summary>
    public int PublicationWorkerPollSeconds { get; init; } = 5;

    /// <summary>Publicações que uma instância pode tentar retomar em paralelo.</summary>
    public int PublicationWorkerConcurrency { get; init; } = 2;

    /// <summary>
    /// Limita escritas de publicação simultâneas contra a mesma instalação
    /// Moodle dentro de uma instância do conector.
    /// </summary>
    public int PublicationWorkerPerConnectionConcurrency { get; init; } = 1;

    /// <summary>
    /// Janela para o professor revisar uma prévia antes de confirmá-la.
    /// Nunca deve ser confundida com o timeout do request HTTP.
    /// </summary>
    public int PublicationReviewExpirationHours { get; init; } = 24;

    /// <summary>
    /// Orçamento total da chamada MCP de criação. Deve ficar abaixo do timeout
    /// do transporte para devolver um erro estruturado, não um HTTP 504.
    /// </summary>
    public int BatchCreationTimeoutSeconds { get; init; } = 75;
}
