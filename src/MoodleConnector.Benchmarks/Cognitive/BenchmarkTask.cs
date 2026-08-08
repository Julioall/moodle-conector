using System.Collections.Generic;

namespace MoodleConnector.Benchmarks.Cognitive;

public sealed record BenchmarkTask(
    string Id,
    string Category,
    string Prompt,
    string ExpectedIntent,
    IReadOnlyList<string> AllowedOperations,
    IReadOnlyList<string> ForbiddenOperations,
    bool RequiresCompleteDataset,
    string? ExpectedConnection = null,
    bool IsCriticalTask = false
);
