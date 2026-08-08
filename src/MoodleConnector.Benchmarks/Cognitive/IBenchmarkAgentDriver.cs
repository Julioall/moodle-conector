using System.Threading;
using System.Threading.Tasks;

namespace MoodleConnector.Benchmarks.Cognitive;

public interface IBenchmarkAgentDriver
{
    Task<CognitiveTrace> RunAsync(
        BenchmarkTask task,
        BenchmarkProfile profile,
        CancellationToken cancellationToken);
}
