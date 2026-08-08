using MoodleConnector.Domain.Registry;

namespace MoodleConnector.Application.Registry;

public interface IPolicyEngine
{
    PolicyEvaluationResult Evaluate(MoodleOperation? operation);
}
