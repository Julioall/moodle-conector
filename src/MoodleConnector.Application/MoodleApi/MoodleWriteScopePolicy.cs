namespace MoodleConnector.Application.MoodleApi;

/// <summary>
/// Canonical mapping between a controlled Moodle write family and the OAuth
/// scope that authorizes it. Keeping this mapping in one place prevents a
/// specialized handler and the universal executor from drifting apart.
/// </summary>
public static class MoodleWriteScopePolicy
{
    public static string ForFunction(string functionName)
    {
        var normalized = functionName.Trim().ToLowerInvariant();
        return normalized switch
        {
            "core_message_send_instant_messages" or
            "core_message_send_messages_to_conversation" => "moodle.write.messages",
            "mod_forum_add_discussion" or
            "mod_forum_add_discussion_post" => "moodle.write.forums",
            "core_calendar_create_calendar_events" => "moodle.write.course_content",
            "mod_assign_save_grade" or
            "mod_assign_save_grades" => "moodle.write.assignments.grade",
            _ => "moodle.write"
        };
    }
}

public static class MoodleWriteExecutionClassifier
{
    public static bool IsUnknown(Exception exception)
    {
        if (exception is HttpRequestException or TimeoutException)
        {
            return true;
        }

        return exception is MoodleApiException moodleException &&
            MoodleErrorContract.NormalizeCode(moodleException.ErrorCode) is
                MoodleErrorContract.NetworkError or MoodleErrorContract.RequestTimeout;
    }
}
