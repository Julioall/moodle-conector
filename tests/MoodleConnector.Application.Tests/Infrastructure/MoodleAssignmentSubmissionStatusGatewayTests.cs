using System.Text.Json;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class MoodleAssignmentSubmissionStatusGatewayTests
{
    [Fact]
    public void HasExistingFeedback_ReconheceFormatoOficialFeedbackPlugins()
    {
        using var document = JsonDocument.Parse("""
        {
          "lastattempt": { "submission": { "status": "submitted" } },
          "feedbackplugins": [
            {
              "type": "comments",
              "editorfields": [{ "name": "comment", "text": "Feedback já enviado", "format": 1 }]
            }
          ]
        }
        """);

        Assert.True(MoodleAssignmentSubmissionStatusGateway.HasExistingFeedback(document.RootElement));

        var status = MoodleAssignmentSubmissionStatusGateway.ParseStatus(document.RootElement.GetRawText(), 1095254, 123);
        Assert.NotNull(status);
        Assert.True(status.HasFeedback);
    }

    [Fact]
    public void HasExistingFeedback_ReconheceArquivoDeFeedback()
    {
        using var document = JsonDocument.Parse("""
        {
          "feedbackplugins": [
            {
              "type": "file",
              "fileareas": [{ "area": "feedback_files", "files": [{ "filename": "retorno.pdf" }] }]
            }
          ]
        }
        """);

        Assert.True(MoodleAssignmentSubmissionStatusGateway.HasExistingFeedback(document.RootElement));
    }

    [Fact]
    public void HasExistingFeedback_NaoConfundePluginDaEntregaComFeedback()
    {
        using var document = JsonDocument.Parse("""
        {
          "lastattempt": {
            "submission": {
              "status": "submitted",
              "plugins": [{ "type": "onlinetext", "editorfields": [{ "text": "Resposta do aluno" }] }]
            }
          }
        }
        """);

        Assert.False(MoodleAssignmentSubmissionStatusGateway.HasExistingFeedback(document.RootElement));
    }
}
