using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure;

public sealed class CurrentUserContext(IHttpContextAccessor httpContextAccessor) : ICurrentUserContext
{
    public string Subject =>
        Principal?.FindFirst("sub")?.Value ??
        Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
        string.Empty;

    public string? Email =>
        Principal?.FindFirst("email")?.Value ??
        Principal?.FindFirst(ClaimTypes.Email)?.Value;

    public IReadOnlyCollection<string> Scopes =>
        Principal?.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

    public bool HasScope(string scope)
    {
        return Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase);
    }

    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;
}
