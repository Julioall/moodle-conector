using System.Text.Json.Nodes;
using MoodleConnector.Application.Benchmarking;
using MoodleConnector.Domain.Registry;

namespace MoodleConnector.Application.Tests.ShadowComparison;

public sealed class ParticipantsShadowTests
{
    [Fact]
    public async Task ShadowComparison_GetEnrolledUsers_ShouldIgnoreExtraFieldsAndPreserveSemantics()
    {
        var profile = new ParticipantComparisonProfile();
        var runner = new ShadowComparisonRunner([profile]);
        var legacyJson = """
        [
          { "id": 100, "fullname": "Aluno", "suspended": false, "firstaccess": 10, "lastaccess": 20, "lastcourseaccess": 30 }
        ]
        """;
        var registryJson = """
        {
          "users": [
            { "id": 100, "fullname": "Aluno", "suspended": false, "firstaccess": 10, "lastaccess": 20, "lastcourseaccess": 30, "roles": [], "groups": [] }
          ]
        }
        """;

        var connection = new ConnectionInfo(Guid.NewGuid(), "test_alias", "https://moodle.example");
        var result = await runner.RunComparisonAsync(
            "core_enrol_get_enrolled_users",
            connection,
            "test",
            "course-participants",
            () => Task.FromResult<JsonNode?>(JsonNode.Parse(legacyJson)),
            () => Task.FromResult<(JsonNode?, string)>((JsonNode.Parse(registryJson), "Allow")));

        Assert.Equal(100.0, result.Comparison.SemanticParityPercent);
        Assert.Empty(result.Comparison.MissingItems);
        Assert.Empty(result.Comparison.FieldDifferences);
    }
}
