using MoodleConnector.Application.MoodleApi;

namespace MoodleConnector.Application.Tests.MoodleApi;

public sealed class MoodleFunctionClassifierTests
{
    [Theory]
    [InlineData("mod_forum_get_forum_discussions_paginated")]
    [InlineData("local_attendance_get_sessions")]
    public void Classify_ReturnsReadForDiscoveredQueryVerbs(string functionName)
    {
        Assert.Equal(MoodleFunctionRisk.Read, MoodleFunctionClassifier.Classify(functionName));
    }

    [Theory]
    [InlineData("core_course_delete_courses")]
    [InlineData("local_plugin_unknown_function")]
    public void Classify_RoutesNonQueriesThroughConfirmedWrite(string functionName)
    {
        Assert.Equal(MoodleFunctionRisk.ControlledWrite, MoodleFunctionClassifier.Classify(functionName));
    }
}
