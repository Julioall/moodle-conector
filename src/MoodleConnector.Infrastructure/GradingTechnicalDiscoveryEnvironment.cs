using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;

namespace MoodleConnector.Infrastructure;

internal sealed class GradingTechnicalDiscoveryEnvironment(
    IOptions<MoodleApiOptions> moodleApiOptions,
    IOptions<AssignmentWriteFeatureOptions> featureOptions) : IGradingTechnicalDiscoveryEnvironment
{
    private readonly MoodleApiOptions _moodleApiOptions = moodleApiOptions.Value;
    private readonly AssignmentWriteFeatureOptions _featureOptions = featureOptions.Value;

    public bool AssignmentGradeWriteEnabled => _featureOptions.AssignmentGradeWriteEnabled;

    public bool AssignmentFeedbackWriteEnabled => _featureOptions.AssignmentFeedbackWriteEnabled;

    public bool AllowServiceTokenForReadOnlyQueries => _moodleApiOptions.AllowServiceTokenForReadOnlyQueries;
}
