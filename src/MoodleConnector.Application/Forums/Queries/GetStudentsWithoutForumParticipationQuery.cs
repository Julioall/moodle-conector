using MediatR;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Forums.Queries;

/// <summary>
/// Identifica estudantes ativos que ainda não postaram em um fórum específico.
///
/// LIMITAÇÃO: A API Moodle não expõe diretamente "quem não postou".
/// Esta query coleta os autores de todos os posts do fórum e subtrai da lista de estudantes ativos.
/// Pode ser lenta para fóruns com muitas discussões/posts.
/// </summary>
public sealed record StudentForumParticipationStatus(
    string StudentId,
    string FullName,
    bool Participated,
    DateTimeOffset? LastCourseAccessAt,
    string ParticipationStatus = "known"); // "known" | "partial" | "unknown"

public sealed record GetStudentsWithoutForumParticipationResult(
    string CourseId,
    string ForumId,
    int TotalStudentsAnalyzed,
    IReadOnlyList<StudentForumParticipationStatus> StudentsWithoutParticipation,
    IReadOnlyList<string> SuggestedRecipientIds,
    string Limitation,
    string? Warning)
{
    public string ParticipationStatus { get; init; } = "known"; // "known" | "partial" | "unknown"
    public bool IsComplete { get; init; } = true;
}

public sealed record GetStudentsWithoutForumParticipationQuery(
    string CourseId,
    string ForumId,
    int MaxStudentsToAnalyze = 100,
    int MaxDiscussionsToScan = 20) : IRequest<GetStudentsWithoutForumParticipationResult>;

public sealed class GetStudentsWithoutForumParticipationQueryHandler(
    IMoodleParticipantsGateway participantsGateway,
    IMoodleForumGateway forumGateway,
    IMoodleCurrentUserIdGateway currentUserIdGateway)
    : IRequestHandler<GetStudentsWithoutForumParticipationQuery, GetStudentsWithoutForumParticipationResult>
{
    private const string LimitationMessage =
        "A API Moodle não expõe 'não-participantes' diretamente. Esta query coleta autores de posts e subtrai da lista de estudantes. " +
        "Pode não capturar participação via votos ou curtidas. Revise os resultados com atenção.";

    public async Task<GetStudentsWithoutForumParticipationResult> Handle(
        GetStudentsWithoutForumParticipationQuery request,
        CancellationToken cancellationToken)
    {
        var currentUserExternalId = (await currentUserIdGateway.GetCurrentUserIdAsync(cancellationToken)).ToString();

        // 1. Fetch active students
        var participantsPage = await participantsGateway.GetCourseParticipantsAsync(
            userExternalId: currentUserExternalId,
            courseId: request.CourseId,
            statusFilter: ParticipantStatusFilter.Active,
            page: 1,
            pageSize: request.MaxStudentsToAnalyze > 0 ? request.MaxStudentsToAnalyze : 100,
            studentsOnly: true,
            includeEmail: false,
            groupId: null,
            cancellationToken: cancellationToken);

        // 2. Collect user IDs that have posted in this forum
        var participatedUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? warning = null;
        var postErrors = 0;
        var discussionReadFailed = false;
        var discussionsTruncated = false;

        try
        {
            var discussions = await forumGateway.GetForumDiscussionsPaginatedAsync(
                userExternalId: currentUserExternalId,
                forumId: request.ForumId,
                sortBy: "created",
                sortDirection: "DESC",
                page: 1,
                pageSize: request.MaxDiscussionsToScan > 0 ? request.MaxDiscussionsToScan : 20,
                cancellationToken: cancellationToken);

            foreach (var discussion in discussions)
            {
                // Add the discussion author
                if (!string.IsNullOrWhiteSpace(discussion.AuthorUserId))
                {
                    participatedUserIds.Add(discussion.AuthorUserId);
                }

                // Fetch posts for this discussion
                try
                {
                    var posts = await forumGateway.GetDiscussionPostsAsync(
                        userExternalId: currentUserExternalId,
                        discussionId: discussion.DiscussionId,
                        sortBy: "created",
                        sortDirection: "ASC",
                        cancellationToken: cancellationToken);

                    foreach (var post in posts)
                    {
                        if (!string.IsNullOrWhiteSpace(post.UserId))
                        {
                            participatedUserIds.Add(post.UserId);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    postErrors++;
                }
            }

            var discussionLimit = request.MaxDiscussionsToScan > 0 ? request.MaxDiscussionsToScan : 20;
            if (discussions.Count >= discussionLimit)
            {
                discussionsTruncated = true;
                warning = AppendWarning(warning, $"O fórum pode ter mais discussões além das {discussionLimit} analisadas. " +
                          "Considere aumentar MaxDiscussionsToScan para uma análise mais completa.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            discussionReadFailed = true;
            warning = $"Não foi possível carregar as discussões do fórum {request.ForumId}. " +
                      "Verifique se o ID do fórum está correto e se o usuário tem acesso.";
        }

        if (postErrors > 0)
        {
            warning = AppendWarning(warning, $"Não foi possível carregar as mensagens de {postErrors} discussão(ões); a análise de participação é parcial.");
        }

        warning = participantsPage.HasMore
            ? AppendWarning(warning, "A lista de estudantes foi limitada pelo maximo solicitado; a analise de participacao e parcial.")
            : warning;

        // A failed forum read cannot prove that a student did not participate.
        // Return an explicit unavailable/partial state instead of classifying
        // every active student as a non-participant.
        var participationStatus = discussionReadFailed
            ? "unknown"
            : postErrors > 0 || participantsPage.HasMore || discussionsTruncated
                ? "partial"
                : "known";

        if (discussionReadFailed || postErrors > 0)
        {
            return new GetStudentsWithoutForumParticipationResult(
                CourseId: request.CourseId,
                ForumId: request.ForumId,
                TotalStudentsAnalyzed: participantsPage.Participants.Count,
                StudentsWithoutParticipation: [],
                SuggestedRecipientIds: [],
                Limitation: LimitationMessage,
                Warning: warning)
            {
                ParticipationStatus = participationStatus,
                IsComplete = false
            };
        }

        // 3. Find students who have NOT posted
        var studentsWithoutParticipation = participantsPage.Participants
            .Select(student => new StudentForumParticipationStatus(
                StudentId: student.UserId,
                FullName: student.FullName,
                Participated: participatedUserIds.Contains(student.UserId),
                LastCourseAccessAt: student.LastCourseAccessAt,
                ParticipationStatus: participationStatus))
            .Where(s => !s.Participated)
            .ToList();

        var suggestedRecipients = studentsWithoutParticipation.Select(s => s.StudentId).ToList();

        return new GetStudentsWithoutForumParticipationResult(
            CourseId: request.CourseId,
            ForumId: request.ForumId,
            TotalStudentsAnalyzed: participantsPage.Participants.Count,
            StudentsWithoutParticipation: studentsWithoutParticipation,
            SuggestedRecipientIds: suggestedRecipients,
            Limitation: LimitationMessage,
            Warning: warning)
        {
            ParticipationStatus = participationStatus,
            IsComplete = participationStatus == "known"
        };
    }

    private static string AppendWarning(string? current, string additional) =>
        string.IsNullOrWhiteSpace(current) ? additional : $"{current} {additional}";
}
