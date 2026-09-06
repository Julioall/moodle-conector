namespace MoodleConnector.Application.Configuration;

public sealed class GradingLimitsOptions
{
    public const string SectionName = "GradingLimits";

    public int MaxBatchItems { get; init; } = 400;

    public int MaxFileSizeMb { get; init; } = 25;

    /// <summary>Maximum binary size returned by a single MCP resource read.</summary>
    public int MaxResourceBytes { get; init; } = 25 * 1024 * 1024;

    public int ResourceExpirationMinutes { get; init; } = 30;

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

    public int BatchLeaseMinutes { get; init; } = 15;

    public int DurableBatchPollSeconds { get; init; } = 5;

    public int DurableBatchClaimSize { get; init; } = 4;

    /// <summary>
    /// Número máximo de lotes processados em paralelo por instância. Cada
    /// lote continua protegido por lease PostgreSQL e por um limite separado
    /// por conexão Moodle.
    /// </summary>
    public int BatchWorkerConcurrency { get; init; } = 4;

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
    /// Orçamento total da chamada MCP de criação. Deve ficar abaixo do timeout
    /// do transporte para devolver um erro estruturado, não um HTTP 504.
    /// </summary>
    public int BatchCreationTimeoutSeconds { get; init; } = 75;
}
