using System.Collections.Generic;
using MoodleConnector.Presentation.Configuration;

namespace MoodleConnector.Benchmarks.Cognitive;

/// <summary>
/// Aggregated metrics for a single benchmark profile run.
/// </summary>
public sealed record ProfileReport(
    ToolExposureProfile Profile,
    string ModelName,
    int TotalTasks,
    int SucceededTasks,
    int CriticalTasks,
    int CriticalSucceeded,

    // Accuracy metrics
    double IntentAccuracyRate,
    double RoutingAccuracyRate,
    double ConnectionAccuracyRate,
    double ParameterAccuracyRate,
    double ResultAccuracyRate,
    double PaginationAwarenessRate,

    // Token / cost metrics
    double AvgToolSchemaTokens,
    double AvgInputTokens,
    double AvgOutputTokens,
    double AvgToolCalls,
    double AvgMoodleWsCalls,

    // Latency
    long LatencyP50Ms,
    long LatencyP95Ms,
    long AvgLatencyMs,

    // Safety metrics
    int WrongConnectionExecutions,
    int UnsafeActions,
    double HallucinationRate,

    // Raw traces for gate evaluation
    IReadOnlyList<CognitiveTrace> Traces
)
{
    public double TaskSuccessRate => TotalTasks == 0 ? 0 : (double)SucceededTasks / TotalTasks * 100.0;
    public double CriticalTaskSuccessRate => CriticalTasks == 0 ? 100.0 : (double)CriticalSucceeded / CriticalTasks * 100.0;
}

/// <summary>
/// Result of applying gates against the baseline profile (A).
/// </summary>
public sealed record GateResult(
    string GateName,
    string Description,
    bool Passed,
    string BaselineValue,
    string ProfileValue,
    string Threshold
);

/// <summary>
/// Full benchmark report comparing all three profiles.
/// </summary>
public sealed record BenchmarkReport(
    string RunId,
    string BenchmarkVersion,
    string CommitSha,
    string Model,
    ProfileReport ProfileA,
    ProfileReport ProfileB,
    ProfileReport ProfileC,
    IReadOnlyList<GateResult> GatesForProfileB,
    IReadOnlyList<GateResult> GatesForProfileC,
    bool ProfileBApproved,
    bool ProfileCApproved
);
