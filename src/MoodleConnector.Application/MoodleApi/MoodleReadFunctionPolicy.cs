namespace MoodleConnector.Application.MoodleApi;

public static class MoodleReadFunctionPolicy
{
    private static readonly HashSet<string> ReadFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "core_webservice_get_site_info",
        "core_course_get_courses",
        "core_course_get_courses_by_field",
        "core_course_search_courses",
        "core_course_get_categories",
        "core_course_get_enrolled_courses_by_timeline_classification",
        "core_enrol_get_users_courses",
        "core_enrol_get_enrolled_users",
        "core_user_get_users",
        "core_user_get_users_by_field",
        "core_group_get_course_groups",
        "core_group_get_group_members",
        "core_course_get_contents",
        "core_completion_get_activities_completion_status",
        "core_completion_get_course_completion_status",
        "core_files_get_files",
        "core_grades_get_grades",
        "gradereport_user_get_grade_items",
        "mod_assign_get_assignments",
        "mod_assign_get_submissions",
        "mod_assign_get_submission_status",
        "mod_assign_get_grades",
        "mod_assign_get_participant",
        "mod_forum_get_forum_discussions_paginated",
        "mod_forum_get_forum_discussion_posts",
        "mod_quiz_get_quizzes_by_courses",
        "mod_scorm_get_scorms_by_courses"
    };

    private static readonly string[] DestructiveTokens = ["delete", "unenrol", "purge", "reset", "remove"];

    private static readonly HashSet<string> ControlledWriteFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mod_assign_save_grade",
        "mod_assign_save_grades",
        "core_user_update_users",
        "core_course_update_courses",
        "core_message_send_instant_messages",
        "mod_forum_add_discussion",
        "mod_forum_add_discussion_post",
        "core_enrol_enrol_users"
    };

    public static MoodleFunctionRisk Classify(string functionName)
    {
        if (ReadFunctions.Contains(functionName))
        {
            return MoodleFunctionRisk.Read;
        }

        if (ControlledWriteFunctions.Contains(functionName))
        {
            return MoodleFunctionRisk.ControlledWrite;
        }

        return DestructiveTokens.Any(token => functionName.Contains(token, StringComparison.OrdinalIgnoreCase))
            ? MoodleFunctionRisk.Destructive
            : MoodleFunctionRisk.Unknown;
    }
}
