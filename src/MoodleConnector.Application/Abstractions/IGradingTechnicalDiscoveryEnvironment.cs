namespace MoodleConnector.Application.Abstractions;

public interface IGradingTechnicalDiscoveryEnvironment
{
    bool AssignmentGradeWriteEnabled { get; }

    bool AssignmentFeedbackWriteEnabled { get; }

    bool HasWriteServiceToken { get; }

    bool AllowServiceTokenForReadOnlyQueries { get; }
}
