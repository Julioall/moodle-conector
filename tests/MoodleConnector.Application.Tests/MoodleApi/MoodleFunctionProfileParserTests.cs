using System.Text.Json;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;

namespace MoodleConnector.Application.Tests.MoodleApi;

public sealed class MoodleFunctionProfileParserTests
{
    [Fact]
    public void Parse_PreservesExternalFunctionVersionFromSiteInfo()
    {
        using var document = JsonDocument.Parse("""
            {
              "sitename": "Moodle",
              "release": "5.1.2 (Build: 20260209)",
              "userid": 42,
              "functions": [
                { "name": "core_example_get_items", "version": "2026012100" },
                { "name": "mod_book_view_book", "version": "2026031700" }
              ]
            }
            """);

        var profile = MoodleFunctionProfileParser.Parse(Connection(), document.RootElement);

        var read = Assert.Single(profile.Functions, function => function.Name == "core_example_get_items");
        var write = Assert.Single(profile.Functions, function => function.Name == "mod_book_view_book");
        Assert.Equal("2026012100", read.ExternalFunctionVersion);
        Assert.Equal("2026031700", write.ExternalFunctionVersion);
        Assert.Equal(MoodleFunctionRisk.Read, read.Risk);
        Assert.Equal(MoodleFunctionRisk.ControlledWrite, write.Risk);
    }

    [Fact]
    public void Parse_DeduplicatesFunctionNamesWithoutDiscardingVersion()
    {
        using var document = JsonDocument.Parse("""
            {
              "functions": [
                { "name": "core_example_get_items", "version": "2026012100" },
                { "name": "CORE_EXAMPLE_GET_ITEMS", "version": "2026012100" }
              ]
            }
            """);

        var profile = MoodleFunctionProfileParser.Parse(Connection(), document.RootElement);

        var function = Assert.Single(profile.Functions);
        Assert.Equal("2026012100", function.ExternalFunctionVersion);
    }

    private static MoodleConnectorCredentials Connection() => new(
        "client",
        "connection",
        "fieg",
        "https://moodle.example",
        "user",
        "password",
        "fieg",
        false);
}
