using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure;

internal sealed class HttpContextMoodleConnectorCredentialsProvider(
    IHttpContextAccessor httpContextAccessor,
    ConnectorDbContext dbContext,
    IConnectorSecretProtector secretProtector,
    IMoodleConnectionSelection selection) : IMoodleConnectorCredentialsProvider
{
    public async Task<MoodleConnectorCredentials> GetCurrentCredentialsAsync(CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var clientId = principal?.FindFirst("connector_client_id")?.Value
            ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("Contexto do cliente do conector nao encontrado na requisicao autenticada.");
        }

        var requestedAlias = NormalizeAlias(selection.Alias);
        var query = dbContext.ConnectorClients
            .AsNoTracking()
            .Where(client => client.ClientId == clientId && client.IsActive);

        var entity = string.IsNullOrWhiteSpace(requestedAlias)
            ? await query
                .OrderByDescending(client => client.IsDefault)
                .ThenBy(client => client.MoodleAlias)
                .FirstOrDefaultAsync(cancellationToken)
            : await query
                .Where(client => client.MoodleAlias == requestedAlias || client.MoodleTarget == requestedAlias)
                .OrderByDescending(client => client.IsDefault)
                .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(requestedAlias)
                ? "Credenciais do cliente do conector nao encontradas no banco."
                : $"Nenhuma conexao Moodle ativa encontrada para o alias '{requestedAlias}'.");
        }

        return new MoodleConnectorCredentials(
            entity.ClientId,
            entity.Id,
            entity.MoodleAlias,
            entity.MoodleBaseUrl,
            secretProtector.Unprotect(entity.MoodleUsernameEncrypted),
            secretProtector.Unprotect(entity.MoodlePasswordEncrypted),
            entity.MoodleTarget,
            entity.CanWrite);
    }

    private static string? NormalizeAlias(string? alias) =>
        string.IsNullOrWhiteSpace(alias) ? null : alias.Trim().ToLowerInvariant();
}
