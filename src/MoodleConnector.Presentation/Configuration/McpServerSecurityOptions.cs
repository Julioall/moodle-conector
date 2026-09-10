namespace MoodleConnector.Presentation.Configuration;

public sealed class McpServerSecurityOptions
{
    public const string SectionName = "McpServerSecurity";

    public bool RequireJwt { get; init; } = false;

    public bool RequireApiKey { get; init; } = true;

    public string ApiKeyHeader { get; init; } = "X-Mcp-Api-Key";

    /// <summary>
    /// Legacy compatibility switch for technical API keys that are not linked
    /// to a local UserAccount. Production keeps this disabled so an unlinked
    /// key cannot inherit all Moodle tool permissions.
    /// </summary>
    public bool AllowUnlinkedApiKey { get; init; } = false;
}
