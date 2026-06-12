namespace MoodleConnector.Application.Abstractions;

public sealed record RegisterAccountRequest(string Name, string Email, string Password);
public sealed record LoginAccountRequest(string Email, string Password);
public sealed record ConnectMoodleAccountRequest(Guid UserId, string MoodleAlias, string MoodleBaseUrl, string MoodleUsername, string MoodlePassword, bool IsDefault, bool CanWrite);
public sealed record AccountDto(Guid Id, string Name, string Email, bool HasMoodleConnected, string? ConnectorClientId);
public sealed record MoodleConnectionDto(string Id, string Alias, string BaseUrl, bool IsDefault, bool CanWrite);
public sealed record AccountProfileDto(Guid Id, string Name, string Email, bool HasMoodleConnected, string? ApiKey, IReadOnlyList<MoodleConnectionDto> MoodleConnections);

public interface IAccountService
{
    Task<AccountDto> RegisterAsync(RegisterAccountRequest request, CancellationToken cancellationToken);
    Task<AccountDto?> ValidateLoginAsync(LoginAccountRequest request, CancellationToken cancellationToken);
    Task<AccountProfileDto?> GetProfileAsync(Guid userId, CancellationToken cancellationToken);
    Task<string> ConnectMoodleAsync(ConnectMoodleAccountRequest request, CancellationToken cancellationToken);
}
