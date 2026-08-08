using MoodleConnector.Domain.Registry;

namespace MoodleConnector.Application.Registry;

public interface IOperationRegistry
{
    MoodleOperation? GetOperation(string operationName);
    IReadOnlyList<MoodleOperation> GetAllOperations();
}
