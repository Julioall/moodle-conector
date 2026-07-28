using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;

namespace MoodleConnector.Infrastructure;

internal sealed class HttpContextMoodleConnectorCredentialsProvider(
    IHttpContextAccessor httpContextAccessor,
    ConnectorDbContext dbContext,
    IConnectorSecretProtector secretProtector,
    IMoodleConnectionSelection selection,
    ILogger<HttpContextMoodleConnectorCredentialsProvider> logger) : IMoodleConnectorCredentialsProvider
{
    public async Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var clientId = await ResolveClientIdAsync(principal, cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw LogFailure(
                MoodleErrorContract.ConnectionNotFound,
                "Authenticated connector client context was not found.");
        }

        var requestedAlias = MoodleConnectionAlias.Normalize(selection.Alias);
        var connections = await dbContext.ConnectorClients
            .AsNoTracking()
            .Where(connection => connection.ClientId == clientId)
            .OrderByDescending(connection => connection.IsDefault)
            .ThenBy(connection => connection.MoodleAlias)
            .ToArrayAsync(cancellationToken);

        ConnectorClientCredentialEntity entity;
        if (requestedAlias is null)
        {
            if (connections.Length == 0)
            {
                throw LogFailure(
                    MoodleErrorContract.ConnectionNotFound,
                    "No Moodle connection belongs to the authenticated connector client.");
            }

            var defaults = connections.Where(connection => connection.IsDefault).ToArray();
            if (defaults.Length != 1)
            {
                throw LogFailure(
                    MoodleErrorContract.DefaultConnectionNotConfigured,
                    $"Expected one default Moodle connection but found {defaults.Length}.");
            }

            entity = defaults[0];
        }
        else
        {
            var aliasMatches = connections
                .Where(connection => MoodleConnectionAlias.Normalize(connection.MoodleAlias) == requestedAlias)
                .ToArray();
            var matches = aliasMatches.Length > 0
                ? aliasMatches
                : connections
                    .Where(connection => MoodleConnectionAlias.Normalize(connection.MoodleTarget) == requestedAlias)
                    .ToArray();

            if (matches.Length == 0)
            {
                throw LogFailure(
                    MoodleErrorContract.ConnectionNotFound,
                    $"Moodle connection alias '{requestedAlias}' was not found.");
            }

            if (matches.Length > 1)
            {
                throw LogFailure(
                    MoodleErrorContract.ConnectionNotFound,
                    $"Moodle connection alias '{requestedAlias}' is ambiguous.");
            }

            entity = matches[0];
        }

        var safeBaseUrl = GetSafeBaseUrl(entity.MoodleBaseUrl);
        if (!entity.IsActive)
        {
            throw LogFailure(
                MoodleErrorContract.ConnectionDisabled,
                "The selected Moodle connection is disabled.",
                entity,
                safeBaseUrl);
        }

        if (!Uri.TryCreate(entity.MoodleBaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(baseUri.Host))
        {
            throw LogFailure(
                MoodleErrorContract.NetworkError,
                "The selected Moodle connection has an invalid base URL.",
                entity,
                safeBaseUrl);
        }

        if (string.IsNullOrWhiteSpace(entity.MoodleUsernameEncrypted) ||
            string.IsNullOrWhiteSpace(entity.MoodlePasswordEncrypted))
        {
            throw LogFailure(
                MoodleErrorContract.TokenMissing,
                "The selected Moodle connection has incomplete encrypted credentials.",
                entity,
                safeBaseUrl);
        }

        string username;
        string password;
        try
        {
            username = secretProtector.Unprotect(entity.MoodleUsernameEncrypted);
            password = secretProtector.Unprotect(entity.MoodlePasswordEncrypted);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            var failure = new MoodleApiException(
                MoodleErrorContract.TokenDecryptionFailed,
                "The selected Moodle credentials could not be decrypted.",
                innerException: exception,
                connectionId: entity.Id,
                connectionAlias: MoodleConnectionAlias.Normalize(entity.MoodleAlias),
                endpoint: safeBaseUrl);
            logger.LogError(
                exception,
                "Moodle credential decryption failed. AuditId={AuditId} ConnectionId={ConnectionId} Alias={Alias} Endpoint={Endpoint}",
                failure.AuditId,
                entity.Id,
                MoodleConnectionAlias.Normalize(entity.MoodleAlias),
                safeBaseUrl);
            throw failure;
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw LogFailure(
                MoodleErrorContract.TokenMissing,
                "The decrypted Moodle credentials are incomplete.",
                entity,
                safeBaseUrl);
        }

        logger.LogDebug(
            "Moodle connection resolved. ConnectionId={ConnectionId} Alias={Alias} Endpoint={Endpoint}",
            entity.Id,
            MoodleConnectionAlias.Normalize(entity.MoodleAlias),
            safeBaseUrl);

        return new MoodleConnectorCredentials(
            entity.ClientId,
            entity.Id,
            MoodleConnectionAlias.NormalizeOrDefault(entity.MoodleAlias),
            entity.MoodleBaseUrl.Trim().TrimEnd('/'),
            username,
            password,
            MoodleConnectionAlias.NormalizeOrDefault(entity.MoodleTarget),
            entity.CanWrite);
    }

    private async Task<string?> ResolveClientIdAsync(
        ClaimsPrincipal? principal,
        CancellationToken cancellationToken)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var subject = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;
        if (Guid.TryParse(subject, out var userId))
        {
            var account = await dbContext.UserAccounts
                .AsNoTracking()
                .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(account?.ConnectorClientId))
            {
                return account.ConnectorClientId;
            }
        }

        var email = principal.FindFirst(ClaimTypes.Email)?.Value
            ?? principal.FindFirst("email")?.Value
            ?? principal.FindFirst("preferred_username")?.Value;
        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            var account = await dbContext.UserAccounts
                .AsNoTracking()
                .SingleOrDefaultAsync(user => user.Email == normalizedEmail, cancellationToken);
            if (!string.IsNullOrWhiteSpace(account?.ConnectorClientId))
            {
                return account.ConnectorClientId;
            }
        }

        return principal.FindFirst("connector_client_id")?.Value
            ?? subject;
    }

    private MoodleApiException LogFailure(
        string errorCode,
        string internalMessage,
        ConnectorClientCredentialEntity? entity = null,
        string? endpoint = null)
    {
        var failure = new MoodleApiException(
            errorCode,
            internalMessage,
            connectionId: entity?.Id,
            connectionAlias: MoodleConnectionAlias.Normalize(entity?.MoodleAlias ?? selection.Alias),
            endpoint: endpoint);
        logger.LogWarning(
            "Moodle connection resolution failed. AuditId={AuditId} ErrorCode={ErrorCode} ConnectionId={ConnectionId} Alias={Alias} Endpoint={Endpoint}",
            failure.AuditId,
            failure.ErrorCode,
            failure.ConnectionId,
            failure.ConnectionAlias,
            failure.Endpoint);
        return failure;
    }

    private static string? GetSafeBaseUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty,
            UserName = string.Empty,
            Password = string.Empty
        }.Uri.AbsoluteUri.TrimEnd('/');
    }
}
