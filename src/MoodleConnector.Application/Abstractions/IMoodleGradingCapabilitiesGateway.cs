using MoodleConnector.Application.Grading;

namespace MoodleConnector.Application.Abstractions;

public interface IMoodleGradingCapabilitiesGateway
{
    Task<MoodleWebServiceFunctionCatalog> GetFunctionCatalogAsync(
        string userExternalId,
        CancellationToken cancellationToken);
}
