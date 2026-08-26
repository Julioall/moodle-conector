using System.Net.Http;

namespace MoodleConnector.Infrastructure;

/// <summary>
/// Per-request controls shared by the Moodle REST client and its resilience
/// policies. A write must opt out of automatic retries because a lost response
/// can mean that Moodle already applied the mutation.
/// </summary>
internal static class MoodleHttpRequestOptions
{
    public static readonly HttpRequestOptionsKey<bool> DisableAutomaticRetry =
        new("MoodleConnector.DisableAutomaticRetry");
}
