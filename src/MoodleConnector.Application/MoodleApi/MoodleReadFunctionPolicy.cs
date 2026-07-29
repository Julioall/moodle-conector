namespace MoodleConnector.Application.MoodleApi;

public static class MoodleReadFunctionPolicy
{
    private static readonly HashSet<string> ReadFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Site e conexão
        "core_webservice_get_site_info",

        // Cursos
        "core_course_get_courses",
        "core_course_get_courses_by_field",
        "core_course_search_courses",
        "core_course_get_categories",
        "core_course_get_contents",
        "core_course_get_course_module",
        "core_course_get_course_module_by_instance",
        "core_course_get_enrolled_courses_by_timeline_classification",
        "core_course_get_enrolled_courses_with_action_events_by_timeline_classification",
        "core_course_get_recent_courses",
        "core_course_check_updates",
        "core_course_get_updates_since",

        // Matrículas e usuários
        "core_enrol_get_course_enrolment_methods",
        "core_enrol_get_users_courses",
        "core_enrol_get_enrolled_users",
        "core_enrol_search_users",
        "core_user_get_users_by_field",
        "core_user_get_course_user_profiles",

        // Grupos
        "core_group_get_course_groups",
        "core_group_get_course_groupings",
        "core_group_get_course_user_groups",
        "core_group_get_activity_allowed_groups",
        "core_group_get_activity_groupmode",

        // Calendário e prazos
        "core_calendar_get_action_events_by_course",
        "core_calendar_get_action_events_by_courses",
        "core_calendar_get_action_events_by_timesort",
        "core_calendar_get_calendar_events",
        "core_calendar_get_calendar_event_by_id",
        "core_calendar_get_calendar_upcoming_view",
        "core_calendar_get_calendar_day_view",
        "core_calendar_get_calendar_monthly_view",
        "core_calendar_get_calendar_access_information",
        "core_calendar_get_allowed_event_types",

        // Arquivos
        "core_files_get_files",

        // Conclusão
        "core_completion_get_activities_completion_status",
        "core_completion_get_course_completion_status",

        // Notas e boletim
        "core_grades_get_gradable_users",
        "core_grades_get_gradeitems",
        "core_grades_get_enrolled_users_for_selector",
        "core_grades_get_groups_for_selector",
        "core_grades_get_enrolled_users_for_search_widget",
        "core_grades_get_groups_for_search_widget",
        "gradereport_user_get_grade_items",
        "gradereport_user_get_grades_table",
        "gradereport_user_get_access_information",
        "gradereport_overview_get_course_grades",
        "gradereport_grader_get_users_in_report",

        // Tarefas
        "mod_assign_get_assignments",
        "mod_assign_get_submissions",
        "mod_assign_get_submission_status",
        "mod_assign_get_grades",
        "mod_assign_get_participant",
        "mod_assign_get_user_flags",
        "mod_assign_get_user_mappings",
        "mod_assign_list_participants",

        // Fóruns
        "mod_forum_get_forums_by_courses",
        "mod_forum_get_forum_access_information",
        "mod_forum_get_forum_discussions",
        "mod_forum_get_discussion_posts",
        "mod_forum_get_discussion_post",
        "mod_forum_can_add_discussion",

        // Questionários
        "mod_quiz_get_quizzes_by_courses",
        "mod_quiz_get_quiz_access_information",
        "mod_quiz_get_attempt_access_information",
        "mod_quiz_get_user_attempts",
        "mod_quiz_get_user_quiz_attempts",
        "mod_quiz_get_user_best_grade",
        "mod_quiz_get_attempt_summary",
        "mod_quiz_get_attempt_review",
        "mod_quiz_get_combined_review_options",
        "mod_quiz_get_quiz_feedback_for_grade",

        // SCORM
        "mod_scorm_get_scorms_by_courses",
        "mod_scorm_get_scorm_access_information",
        "mod_scorm_get_scorm_attempt_count",
        "mod_scorm_get_scorm_scoes",
        "mod_scorm_get_scorm_sco_tracks",
        "mod_scorm_get_scorm_user_data",

        // Recursos do curso
        "mod_page_get_pages_by_courses",
        "mod_book_get_books_by_courses",
        "mod_resource_get_resources_by_courses",
        "mod_folder_get_folders_by_courses",
        "mod_url_get_urls_by_courses",
        "mod_label_get_labels_by_courses",

        // H5P
        "mod_h5pactivity_get_h5pactivities_by_courses",
        "mod_h5pactivity_get_h5pactivity_access_information",
        "mod_h5pactivity_get_attempts",
        "mod_h5pactivity_get_user_attempts",
        "mod_h5pactivity_get_results",

        // Pesquisa de opinião
        "mod_feedback_get_feedbacks_by_courses",
        "mod_feedback_get_feedback_access_information",
        "mod_feedback_get_items",
        "mod_feedback_get_page_items",
        "mod_feedback_get_analysis",
        "mod_feedback_get_responses_analysis",
        "mod_feedback_get_finished_responses",
        "mod_feedback_get_unfinished_responses",
        "mod_feedback_get_non_respondents",
        "mod_feedback_get_last_completed",

        // Escolha
        "mod_choice_get_choices_by_courses",
        "mod_choice_get_choice_options",
        "mod_choice_get_choice_results",

        // Chat nativo
        "mod_chat_get_chats_by_courses",
        "mod_chat_get_chat_latest_messages",
        "mod_chat_get_chat_users",
        "mod_chat_get_sessions",
        "mod_chat_get_session_messages",

        // Pesquisa nativa
        "mod_survey_get_questions",
        "mod_survey_get_surveys_by_courses",

        // Mensagens e conversas
        "core_message_get_unread_conversations_count",
        "core_message_get_unread_conversation_counts",
        "core_message_get_unread_notification_count",
        "core_message_get_conversation_counts",
        "core_message_get_conversations",
        "core_message_get_conversation",
        "core_message_get_conversation_between_users",
        "core_message_get_self_conversation",
        "core_message_get_conversation_members",
        "core_message_get_conversation_messages",
        "core_message_get_member_info",
        "core_message_get_messages",
        "core_message_get_user_contacts",
        "core_message_get_blocked_users",
        "core_message_get_contact_requests",
        "core_message_get_received_contact_requests_count",
        "core_message_get_user_message_preferences",
        "core_message_get_user_notification_preferences",
        "core_message_data_for_messagearea_search_messages",
        "core_message_message_search_users",
        "core_message_search_contacts",

        // Notificações
        "message_popup_get_popup_notifications",
        "message_popup_get_unread_popup_notification_count",

        // Comentários, anotações e avaliações
        "core_comment_get_comments",
        "core_notes_get_course_notes",
        "core_rating_get_item_ratings"
    };

    private static readonly string[] DestructiveTokens = ["delete", "unenrol", "purge", "reset", "remove"];

    private static readonly HashSet<string> ControlledWriteFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Notas
        "mod_assign_save_grade",
        "mod_assign_save_grades",

        // Gerenciamento de entregas
        "mod_assign_save_user_extensions",
        "mod_assign_set_user_flags",
        "mod_assign_lock_submissions",
        "mod_assign_unlock_submissions",
        "mod_assign_revert_submissions_to_draft",

        // Mensagens
        "core_message_send_instant_messages",
        "core_message_send_messages_to_conversation",

        // Estado de leitura
        "core_message_mark_message_read",
        "core_message_mark_notification_read",
        "core_message_mark_all_notifications_as_read",
        "core_message_mark_all_conversation_messages_as_read",

        // Preferências de conversas
        "core_message_mute_conversations",
        "core_message_unmute_conversations",
        "core_message_set_favourite_conversations",
        "core_message_unset_favourite_conversations",

        // Contatos
        "core_message_block_user",
        "core_message_unblock_user",
        "core_message_create_contact_request",
        "core_message_confirm_contact_request",
        "core_message_decline_contact_request",

        // Fóruns
        "mod_forum_add_discussion",
        "mod_forum_add_discussion_post",
        "mod_forum_update_discussion_post",
        "mod_forum_set_lock_state",
        "mod_forum_set_pin_state",
        "mod_forum_set_subscription_state",
        "mod_forum_set_forum_subscription",
        "mod_forum_set_forum_tracking",
        "mod_forum_toggle_favourite_state",

        // Calendário
        "core_calendar_create_calendar_events",
        "core_calendar_submit_create_update_form",
        "core_calendar_update_event_start_day",

        // Comentários e anotações
        "core_comment_add_comments",
        "core_notes_create_notes",
        "core_rating_add_rating",

        // Conclusão manual
        "core_completion_update_activity_completion_status_manually",
        "core_completion_mark_course_self_completed",

        // Preferências pessoais
        "core_course_set_favourite_courses",
        "core_user_set_user_preferences",
        "core_user_update_user_preferences"
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