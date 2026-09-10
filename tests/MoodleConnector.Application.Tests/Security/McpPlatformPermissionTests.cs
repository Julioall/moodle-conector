using System.Security.Claims;
using MoodleConnector.Presentation.Configuration;
using MoodleConnector.Presentation.Endpoints;
using MoodleConnector.Presentation.Security;

namespace MoodleConnector.Application.Tests.Security;

public sealed class McpPlatformPermissionTests
{
    [Fact]
    public void HasPlatformToolPermission_RequiresGrant()
    {
        var principal = Principal("tool.courses.view");

        Assert.True(PortalEndpointAuthorization.HasPlatformToolPermission(principal, "tool.courses.view"));
        Assert.False(PortalEndpointAuthorization.HasPlatformToolPermission(principal, "tool.assignments.view"));
    }

    [Fact]
    public void HasPlatformToolPermission_DenyOverridesGrant()
    {
        var principal = Principal("tool.courses.view", "tool.courses.view");

        Assert.False(PortalEndpointAuthorization.HasPlatformToolPermission(principal, "tool.courses.view"));
    }

    [Fact]
    public void FilesTools_HaveDedicatedPermissionAndScopeMapping()
    {
        var metadata = new MoodleToolMetadataAttribute
        {
            Family = "files",
            Kind = "controlled-write"
        };

        Assert.Equal("tool.files.write", ToolAuthorizationMapping.PermissionFor("moodle_confirm_upload", metadata));
        Assert.Contains(
            MoodleScopePolicies.WriteAny,
            ToolAuthorizationMapping.OAuthScopesFor("moodle_confirm_upload", metadata));
    }

    private static ClaimsPrincipal Principal(string grant, string? deny = null)
    {
        var claims = new List<Claim> { new("platform_permission", grant) };
        if (!string.IsNullOrWhiteSpace(deny))
        {
            claims.Add(new Claim("platform_permission_deny", deny));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
