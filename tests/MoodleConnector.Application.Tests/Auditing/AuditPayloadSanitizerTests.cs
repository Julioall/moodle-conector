using System.Text.Json;
using MoodleConnector.Application.Auditing;

namespace MoodleConnector.Application.Tests.Auditing;

public sealed class AuditPayloadSanitizerTests
{
    [Fact]
    public void SerializeSanitized_RedigeCamposSensiveisEUrlsComToken()
    {
        var json = AuditPayloadSanitizer.SerializeSanitized(new
        {
            user = "teacher",
            password = "senha-real",
            moodleToken = "token-real",
            links = new[]
            {
                "https://moodle.tests/pluginfile.php/1/mod_resource/content/1/file.pdf?forcedownload=1&wstoken=abc&sesskey=xyz"
            },
            nested = new
            {
                apiKey = "api-key-real",
                title = "Aula 1"
            }
        });

        Assert.DoesNotContain("senha-real", json, StringComparison.Ordinal);
        Assert.DoesNotContain("token-real", json, StringComparison.Ordinal);
        Assert.DoesNotContain("api-key-real", json, StringComparison.Ordinal);
        Assert.DoesNotContain("wstoken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sesskey", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[REDACTED]", json, StringComparison.Ordinal);
        Assert.Contains("forcedownload=1", json, StringComparison.Ordinal);
        Assert.Contains("Aula 1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ToSanitizedElement_RetornaJsonElementSemSegredo()
    {
        var element = AuditPayloadSanitizer.ToSanitizedElement(new
        {
            accessToken = "access-token-real",
            url = "https://moodle.tests/course/view.php?id=10&token=abc"
        });

        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        Assert.Equal("[REDACTED]", element.GetProperty("accessToken").GetString());
        Assert.Equal(
            "https://moodle.tests/course/view.php?id=10",
            element.GetProperty("url").GetString());
    }
}
