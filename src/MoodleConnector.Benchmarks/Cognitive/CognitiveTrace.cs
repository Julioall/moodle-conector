using System;
using System.Collections.Generic;

namespace MoodleConnector.Benchmarks.Cognitive;

public sealed record ToolInvocationTrace(
    string ToolName,
    string ArgumentsJson,
    string ToolResult,
    long LatencyMs,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens
);

public sealed record RoutingTrace(
    string SelectedSkill,
    string SelectedIntent,
    string SelectedOperation,
    string? SelectedConnection,
    IReadOnlyDictionary<string, object> Arguments,
    IReadOnlyList<ToolInvocationTrace> ToolInvocations
);

public sealed record ExecutionTrace(
    Guid ConnectionId,
    string RegistryOperation,
    string PolicyDecision,
    int MoodleCalls,
    long LatencyMs,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    int ToolSchemaTokens
);

public sealed record ScoringTrace(
    bool IntentAccuracy,
    bool RoutingAccuracy,
    bool ConnectionAccuracy,
    bool ParameterAccuracy,
    bool ResultAccuracy,
    bool PaginationAwareness,
    bool OverallSuccess,
    FailureTaxonomy FailureReason
);

public sealed record CognitiveTrace(
    string TaskId,
    BenchmarkProfile Profile,
    string Prompt,
    string Model,
    RoutingTrace Routing,
    ExecutionTrace Execution,
    string ResultContent,
    ScoringTrace Scoring
);
