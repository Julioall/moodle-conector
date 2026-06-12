using Microsoft.AspNetCore.Http;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure;

public sealed class MoodleUserResolver(
    IHttpContextAccessor httpContextAccessor,
    IMoodleCurrentUserIdGateway currentUserIdGateway) : IMoodleUserResolver
{
    private static readonly string[] MoodleUserIdClaimTypes =
    [
        "moodle_user_id",
        "moodle_userid",
        "moodle_user",
        "userid"
    ];

    public async Task<long?> ResolveMoodleUserIdAsync(CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        foreach (var claimType in MoodleUserIdClaimTypes)
        {
            var value = principal.FindFirst(claimType)?.Value;
            if (long.TryParse(value, out var moodleUserId))
            {
                return moodleUserId;
            }
        }

        return await currentUserIdGateway.GetCurrentUserIdAsync(cancellationToken);
    }
}
