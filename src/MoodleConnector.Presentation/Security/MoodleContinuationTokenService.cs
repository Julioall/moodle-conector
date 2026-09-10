using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace MoodleConnector.Presentation.Security;

public sealed record MoodleContinuationState(
    string Subject,
    string ConnectionAlias,
    string Function,
    string ParametersJson,
    string? ContractHash,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Creates opaque, encrypted continuation tokens. The token carries only the
/// next request parameters and is bound again to the caller, function, alias
/// and contract before a subsequent Moodle call is made.
/// </summary>
public sealed class MoodleContinuationTokenService(IDataProtectionProvider protectionProvider)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    private readonly IDataProtector _protector = protectionProvider.CreateProtector("moodle-connector/generic-continuation/v1");

    public string Create(
        string subject,
        string connectionAlias,
        string function,
        string parametersJson,
        string? contractHash)
    {
        var state = new MoodleContinuationState(
            subject,
            connectionAlias,
            function,
            parametersJson,
            contractHash,
            DateTimeOffset.UtcNow.Add(Lifetime));
        return _protector.Protect(JsonSerializer.Serialize(state));
    }

    public bool TryRead(string token, out MoodleContinuationState? state)
    {
        state = null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var json = _protector.Unprotect(token.Trim());
            var candidate = JsonSerializer.Deserialize<MoodleContinuationState>(json);
            if (candidate is null || candidate.ExpiresAtUtc <= DateTimeOffset.UtcNow ||
                string.IsNullOrWhiteSpace(candidate.Subject) ||
                string.IsNullOrWhiteSpace(candidate.ConnectionAlias) ||
                string.IsNullOrWhiteSpace(candidate.Function) ||
                string.IsNullOrWhiteSpace(candidate.ParametersJson))
            {
                return false;
            }

            state = candidate;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or JsonException)
        {
            return false;
        }
    }
}
