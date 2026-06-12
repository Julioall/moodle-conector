using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure;

internal sealed class DatabaseConnectorClientRegistrationService(
    ConnectorDbContext dbContext,
    IConnectorSecretProtector secretProtector) : IConnectorClientRegistrationService
{
    public async Task<RegisterConnectorClientResult> RegisterOrRotateAsync(
        RegisterConnectorClientRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            throw new ArgumentException("ClientId e obrigatorio.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.MoodleUsername) || string.IsNullOrWhiteSpace(request.MoodlePassword))
        {
            throw new ArgumentException("Credenciais Moodle sao obrigatorias.", nameof(request));
        }

        if (!Uri.TryCreate(request.MoodleBaseUrl.Trim(), UriKind.Absolute, out var moodleBaseUri) ||
            moodleBaseUri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("MoodleBaseUrl deve ser uma URL absoluta http/https.", nameof(request));
        }

        var apiKey = GenerateApiKey();
        var apiKeyHash = ApiKeyHasher.Hash(apiKey);
        var now = DateTimeOffset.UtcNow;
        var clientId = request.ClientId.Trim();
        var alias = NormalizeAlias(request.MoodleAlias);
        var connectionId = BuildConnectionId(clientId, alias);

        var entity = await dbContext.ConnectorClients
            .SingleOrDefaultAsync(client => client.Id == connectionId, cancellationToken);

        var replaced = entity is not null;
        if (entity is null)
        {
            entity = new ConnectorClientCredentialEntity
            {
                Id = connectionId,
                ClientId = clientId,
                CreatedAtUtc = now
            };
            dbContext.ConnectorClients.Add(entity);
        }

        var hasExistingApiKey = await dbContext.ConnectorClients
            .AnyAsync(client => client.ClientId == clientId && client.ApiKeyHash != null && client.Id != connectionId, cancellationToken);

        entity.ApiKeyHash = hasExistingApiKey ? entity.ApiKeyHash : apiKeyHash;
        entity.ClientId = clientId;
        entity.MoodleAlias = alias;
        entity.MoodleBaseUrl = NormalizeBaseUrl(moodleBaseUri);
        entity.MoodleUsernameEncrypted = secretProtector.Protect(request.MoodleUsername.Trim());
        entity.MoodlePasswordEncrypted = secretProtector.Protect(request.MoodlePassword);
        entity.MoodleTarget = string.IsNullOrWhiteSpace(request.MoodleTarget) ? "default" : request.MoodleTarget.Trim().ToLowerInvariant();
        entity.IsDefault = request.IsDefault || !await dbContext.ConnectorClients.AnyAsync(client => client.ClientId == clientId && client.IsActive && client.Id != connectionId, cancellationToken);
        entity.CanWrite = request.CanWrite;
        entity.IsActive = true;
        entity.UpdatedAtUtc = now;

        if (entity.IsDefault)
        {
            await dbContext.ConnectorClients
                .Where(client => client.ClientId == clientId && client.Id != connectionId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(client => client.IsDefault, false), cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new RegisterConnectorClientResult(clientId, entity.Id, alias, entity.ApiKeyHash == apiKeyHash ? apiKey : string.Empty, replaced);
    }

    private static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string NormalizeAlias(string alias)
    {
        var normalized = string.IsNullOrWhiteSpace(alias) ? "default" : alias.Trim().ToLowerInvariant();
        return normalized.Length > 64 ? normalized[..64] : normalized;
    }

    private static string NormalizeBaseUrl(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    private static string BuildConnectionId(string clientId, string alias)
    {
        var id = $"{clientId}:{alias}";
        return id.Length <= 64 ? id : $"{clientId[..Math.Min(clientId.Length, 36)]}:{alias[..Math.Min(alias.Length, 26)]}";
    }
}
