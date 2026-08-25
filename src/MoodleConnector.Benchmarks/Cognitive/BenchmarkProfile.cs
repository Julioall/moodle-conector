namespace MoodleConnector.Benchmarks.Cognitive;

using MoodleConnector.Presentation.Configuration;

public sealed record BenchmarkProfile(
    ToolExposureProfile Exposure,
    string ModelName,
    bool UsePluginSkills
);
