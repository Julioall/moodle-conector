using MoodleConnector.Domain.Registry;

namespace MoodleConnector.Application.Registry;

public interface IOperationRegistry
{
    MoodleOperation? GetOperation(string operationName);

    // Version-aware overload is additive so existing registry test doubles and
    // specialized callers remain source-compatible.
    MoodleOperation? GetOperation(string operationName, string? moodleRelease) => GetOperation(operationName);

    MoodleOperation? GetOperation(
        string operationName,
        string? moodleRelease,
        string? externalFunctionVersion) => GetOperation(operationName, moodleRelease);

    IReadOnlyList<MoodleOperation> GetAllOperations();
}
