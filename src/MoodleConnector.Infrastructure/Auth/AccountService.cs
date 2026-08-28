using System.Net.Mail;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Infrastructure.Configuration;

namespace MoodleConnector.Infrastructure;

internal sealed class AccountService(
    ConnectorDbContext dbContext,
    IConnectorSecretProtector secretProtector,
    IConnectorClientRegistrationService registrationService,
    IMoodleCredentialValidator moodleValidator,
    ITeamAccessService? teamAccessService = null,
    IPlatformPermissionService? platformPermissionService = null,
    IOptions<PasswordRecoveryOptions>? passwordRecoveryOptions = null) : IAccountService
{
    private const int MinimumPasswordLength = 8;
    private const int MaximumPasswordLength = 256;

    public async Task<AccountDto> RegisterAsync(RegisterAccountRequest request, CancellationToken cancellationToken)
    {
        var name = NormalizeName(request.Name);
        var email = NormalizeEmailOrThrow(request.Email);
        ValidatePassword(request.Password);

        var exists = await dbContext.UserAccounts
            .AnyAsync(u => u.Email == email, cancellationToken);

        if (exists)
            throw new InvalidOperationException("Este e-mail já está cadastrado.");

        var entity = new UserAccountEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            Email = email,
            PasswordHash = PasswordHasher.Hash(request.Password),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.UserAccounts.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (teamAccessService is not null)
        {
            await teamAccessService.CreatePersonalTeamAsync(entity.Id, entity.Name, cancellationToken);
        }

        if (platformPermissionService is not null)
        {
            await platformPermissionService.EnsureDefaultPermissionsAsync(entity.Id, cancellationToken);
        }

        return ToDto(entity);
    }

    public async Task<AccountDto?> ValidateLoginAsync(LoginAccountRequest request, CancellationToken cancellationToken)
    {
        if (!TryNormalizeEmail(request.Email, out var email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return null;
        }

        var entity = await dbContext.UserAccounts
            .SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (entity is null) return null;
        if (!PasswordHasher.Verify(request.Password, entity.PasswordHash)) return null;

        return ToDto(entity);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var account = await dbContext.UserAccounts.FindAsync([request.UserId], cancellationToken)
            ?? throw new InvalidOperationException("Usuário não encontrado.");
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || !PasswordHasher.Verify(request.CurrentPassword, account.PasswordHash))
            throw new InvalidOperationException("Senha atual inválida.");

        ValidatePassword(request.NewPassword);
        account.PasswordHash = PasswordHasher.Hash(request.NewPassword);
        account.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PortalAccountListItemDto>> ListAccountsAsync(CancellationToken cancellationToken)
        => await dbContext.UserAccounts.AsNoTracking()
            .OrderBy(account => account.Name)
            .ThenBy(account => account.Email)
            .Select(account => new PortalAccountListItemDto(account.Id, account.Name, account.Email, account.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);

    public async Task ResetPasswordToDefaultAsync(Guid userId, CancellationToken cancellationToken)
    {
        var defaultPassword = passwordRecoveryOptions?.Value.DefaultPassword;
        if (string.IsNullOrWhiteSpace(defaultPassword))
            throw new InvalidOperationException("Configure PasswordRecovery:DefaultPassword para habilitar a redefinição administrativa de senha.");
        ValidatePassword(defaultPassword);

        var account = await dbContext.UserAccounts.FindAsync([userId], cancellationToken)
            ?? throw new InvalidOperationException("Usuário não encontrado.");
        account.PasswordHash = PasswordHasher.Hash(defaultPassword);
        account.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<AccountProfileDto?> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.UserAccounts
            .FindAsync([userId], cancellationToken);

        if (entity is null) return null;

        string? apiKey = null;
        if (!string.IsNullOrWhiteSpace(entity.ApiKeyEncrypted))
        {
            try { apiKey = secretProtector.Unprotect(entity.ApiKeyEncrypted); }
            catch { /* chave de criptografia pode ter mudado */ }
        }

        var clientId = entity.ConnectorClientId ?? entity.Id.ToString();
        var connections = await dbContext.ConnectorClients
            .AsNoTracking()
            .Where(client => client.ClientId == clientId && client.IsActive)
            .OrderByDescending(client => client.IsDefault)
            .ThenBy(client => client.MoodleAlias)
            .Select(client => new MoodleConnectionDto(
                client.Id,
                client.MoodleAlias,
                client.MoodleBaseUrl,
                client.IsDefault,
                client.CanWrite,
                client.ValidationStatus,
                client.LastValidatedAtUtc))
            .ToArrayAsync(cancellationToken);

        return new AccountProfileDto(entity.Id, entity.Name, entity.Email, connections.Length > 0, apiKey, connections);
    }

    public async Task<string> ConnectMoodleAsync(ConnectMoodleAccountRequest request, CancellationToken cancellationToken)
    {
        var moodleBaseUrl = NormalizeMoodleBaseUrl(request.MoodleBaseUrl);
        var moodleUsername = request.MoodleUsername.Trim();
        if (string.IsNullOrWhiteSpace(moodleUsername) ||
            string.IsNullOrWhiteSpace(request.MoodlePassword))
        {
            throw new InvalidOperationException("Usuario e senha do Moodle sao obrigatorios.");
        }

        var isValid = await moodleValidator.ValidateAsync(
            moodleBaseUrl,
            moodleUsername,
            request.MoodlePassword,
            cancellationToken);

        if (!isValid)
            throw new InvalidOperationException("Credenciais do Moodle inválidas. Verifique seu usuário e senha.");

        var entity = await dbContext.UserAccounts
            .FindAsync([request.UserId], cancellationToken)
            ?? throw new InvalidOperationException("Usuário não encontrado.");

        var clientId = request.UserId.ToString();

        var result = await registrationService.RegisterOrRotateAsync(
            new RegisterConnectorClientRequest(
                clientId,
                request.MoodleAlias,
                moodleBaseUrl,
                moodleUsername,
                request.MoodlePassword,
                "default",
                request.IsDefault,
                request.CanWrite),
            cancellationToken);

        var connection = await dbContext.ConnectorClients
            .SingleAsync(client => client.Id == result.ConnectionId && client.ClientId == clientId, cancellationToken);
        connection.ValidationStatus = "active";
        connection.LastValidatedAtUtc = DateTimeOffset.UtcNow;

        entity.ConnectorClientId = clientId;
        if (!string.IsNullOrWhiteSpace(result.ApiKey))
        {
            entity.ApiKeyEncrypted = secretProtector.Protect(result.ApiKey);
        }
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return !string.IsNullOrWhiteSpace(result.ApiKey)
            ? result.ApiKey
            : entity.ApiKeyEncrypted is null
                ? string.Empty
                : secretProtector.Unprotect(entity.ApiKeyEncrypted);
    }

    public async Task<MoodleConnectionValidationDto> ValidateMoodleAsync(Guid userId, string moodleId, CancellationToken cancellationToken)
    {
        var account = await dbContext.UserAccounts
            .FindAsync([userId], cancellationToken)
            ?? throw new InvalidOperationException("Usuário não encontrado.");

        var clientId = account.ConnectorClientId ?? account.Id.ToString();
        var connection = await dbContext.ConnectorClients
            .FirstOrDefaultAsync(client => client.Id == moodleId && client.ClientId == clientId && client.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("Moodle não encontrado ou acesso negado.");

        var status = "inactive";
        var validatedAt = DateTimeOffset.UtcNow;
        try
        {
            var username = secretProtector.Unprotect(connection.MoodleUsernameEncrypted);
            var password = secretProtector.Unprotect(connection.MoodlePasswordEncrypted);
            if (await moodleValidator.ValidateAsync(connection.MoodleBaseUrl, username, password, cancellationToken))
                status = "active";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            status = "inactive";
        }

        connection.ValidationStatus = status;
        connection.LastValidatedAtUtc = validatedAt;
        connection.UpdatedAtUtc = validatedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new MoodleConnectionValidationDto(status, validatedAt);
    }

    public async Task<MoodleConnectionDataSummaryDto> GetMoodleDataSummaryAsync(Guid userId, string moodleId, CancellationToken cancellationToken)
    {
        var account = await dbContext.UserAccounts.FindAsync([userId], cancellationToken)
            ?? throw new InvalidOperationException("Usuário não encontrado.");
        var clientId = account.ConnectorClientId ?? account.Id.ToString();
        var connection = await dbContext.ConnectorClients.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == moodleId && item.ClientId == clientId, cancellationToken)
            ?? throw new InvalidOperationException("Moodle não encontrado ou acesso negado.");
        var alias = connection.MoodleAlias;
        var memories = await dbContext.UserMemories.CountAsync(item => item.OwnerSubject == clientId && item.MoodleAlias == alias, cancellationToken);
        var documents = await dbContext.UserMemoryDocuments.CountAsync(item => item.OwnerSubject == clientId && item.MoodleAlias == alias, cancellationToken);
        var links = await dbContext.MoodleUserLinks.CountAsync(item => item.Subject == clientId && item.MoodleAlias == alias, cancellationToken);
        var audits = await dbContext.MoodleAuditLogs.CountAsync(item => item.ActorSubject == clientId && (item.MoodleConnectionId == moodleId || item.MoodleConnectionAlias == alias), cancellationToken);
        return new MoodleConnectionDataSummaryDto(memories, documents, links, audits);
    }

    public async Task UpdateMoodleAsync(UpdateMoodleAccountRequest request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.UserAccounts
            .FindAsync([request.UserId], cancellationToken)
            ?? throw new InvalidOperationException("Usuário não encontrado.");

        var clientId = entity.ConnectorClientId ?? entity.Id.ToString();

        var clientEntity = await dbContext.ConnectorClients
            .FirstOrDefaultAsync(c => c.Id == request.MoodleId && c.ClientId == clientId, cancellationToken)
            ?? throw new InvalidOperationException("Moodle não encontrado ou acesso negado.");

        var moodleBaseUrl = NormalizeMoodleBaseUrl(request.MoodleBaseUrl);
        var urlAlreadyUsed = await dbContext.ConnectorClients
            .AsNoTracking()
            .AnyAsync(c => c.ClientId == clientId && c.Id != clientEntity.Id && c.IsActive && c.MoodleBaseUrl == moodleBaseUrl, cancellationToken);
        if (urlAlreadyUsed)
        {
            throw new InvalidOperationException("Já existe uma conexão Moodle com esta URL nesta conta.");
        }

        var urlChanged = !string.Equals(clientEntity.MoodleBaseUrl, moodleBaseUrl, StringComparison.OrdinalIgnoreCase);
        var credentialsUpdated = !string.IsNullOrWhiteSpace(request.MoodleUsername) && !string.IsNullOrWhiteSpace(request.MoodlePassword);

        if (credentialsUpdated)
        {
            var username = request.MoodleUsername!.Trim();
            var password = request.MoodlePassword!;
            var isValid = await moodleValidator.ValidateAsync(
                moodleBaseUrl,
                username,
                password,
                cancellationToken);

            if (!isValid)
                throw new InvalidOperationException("Credenciais do Moodle inválidas. Verifique seu usuário e senha.");
            
            clientEntity.MoodleUsernameEncrypted = secretProtector.Protect(username);
            clientEntity.MoodlePasswordEncrypted = secretProtector.Protect(password);
            clientEntity.ValidationStatus = "active";
            clientEntity.LastValidatedAtUtc = DateTimeOffset.UtcNow;
        }

        var normalizedAlias = MoodleConnectionAlias.NormalizeOrDefault(request.MoodleAlias);
        var otherAliases = await dbContext.ConnectorClients
            .AsNoTracking()
            .Where(c => c.ClientId == clientId && c.Id != clientEntity.Id)
            .Select(c => c.MoodleAlias)
            .ToArrayAsync(cancellationToken);
        if (otherAliases.Any(alias =>
                string.Equals(
                    MoodleConnectionAlias.Normalize(alias),
                    normalizedAlias,
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Ja existe uma conexao Moodle com o alias '{normalizedAlias}' nesta conta.");
        }

        clientEntity.MoodleAlias = normalizedAlias;
        clientEntity.MoodleBaseUrl = moodleBaseUrl;
        clientEntity.CanWrite = request.CanWrite;
        clientEntity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (urlChanged && !credentialsUpdated)
        {
            clientEntity.ValidationStatus = "unknown";
            clientEntity.LastValidatedAtUtc = null;
        }

        if (request.IsDefault)
        {
            var existingDefaults = await dbContext.ConnectorClients
                .Where(c => c.ClientId == clientId && c.IsDefault && c.Id != clientEntity.Id)
                .ToListAsync(cancellationToken);
            
            foreach (var existingDefault in existingDefaults)
            {
                existingDefault.IsDefault = false;
                existingDefault.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
            clientEntity.IsDefault = true;
        }
        else
        {
            clientEntity.IsDefault = false;
            var anyDefault = await dbContext.ConnectorClients
                .AnyAsync(c => c.ClientId == clientId && c.IsDefault && c.Id != clientEntity.Id && c.IsActive, cancellationToken);
            if (!anyDefault)
            {
                clientEntity.IsDefault = true; // Force at least one default if it's the only one
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> RotateApiKeyAsync(Guid userId, CancellationToken cancellationToken)
    {
        var account = await dbContext.UserAccounts
            .FindAsync([userId], cancellationToken)
            ?? throw new InvalidOperationException("Usuário não encontrado.");

        var clientId = account.ConnectorClientId ?? account.Id.ToString();
        var connections = await dbContext.ConnectorClients
            .Where(client => client.ClientId == clientId && client.IsActive)
            .OrderByDescending(client => client.IsDefault)
            .ThenBy(client => client.MoodleAlias)
            .ToListAsync(cancellationToken);

        if (connections.Count == 0)
            throw new InvalidOperationException("Adicione uma conexão Moodle antes de gerar a API key.");

        var apiKey = GenerateApiKey();
        var apiKeyHash = ApiKeyHasher.Hash(apiKey);
        var now = DateTimeOffset.UtcNow;

        // Uma chave identifica o cliente e pode acessar suas conexões ativas.
        // O hash fica somente na conexão principal; qualquer chave anterior é invalidada.
        foreach (var connection in connections)
        {
            connection.ApiKeyHash = null;
            connection.UpdatedAtUtc = now;
        }
        connections[0].ApiKeyHash = apiKeyHash;

        account.ConnectorClientId = clientId;
        account.ApiKeyEncrypted = secretProtector.Protect(apiKey);
        account.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return apiKey;
    }

    public async Task DeleteMoodleAsync(Guid userId, string moodleId, CancellationToken cancellationToken)
        => await DeleteMoodleAsync(userId, moodleId, false, null, cancellationToken);

    public async Task DeleteMoodleAsync(Guid userId, string moodleId, bool deleteLinkedData, string? confirmationText, CancellationToken cancellationToken)
    {
        var entity = await dbContext.UserAccounts
            .FindAsync([userId], cancellationToken)
            ?? throw new InvalidOperationException("Usuário não encontrado.");

        var clientId = entity.ConnectorClientId ?? entity.Id.ToString();

        var clientEntity = await dbContext.ConnectorClients
            .FirstOrDefaultAsync(c => c.Id == moodleId && c.ClientId == clientId, cancellationToken)
            ?? throw new InvalidOperationException("Moodle não encontrado ou acesso negado.");

        if (deleteLinkedData && !string.Equals(confirmationText?.Trim(), "EXCLUIR CONEXÃO E DADOS", StringComparison.Ordinal))
            throw new InvalidOperationException("Digite EXCLUIR CONEXÃO E DADOS para confirmar a exclusão dos dados associados.");

        if (deleteLinkedData)
        {
            await dbContext.UserMemories.Where(item => item.OwnerSubject == clientId && item.MoodleAlias == clientEntity.MoodleAlias).ExecuteDeleteAsync(cancellationToken);
            await dbContext.UserMemoryDocuments.Where(item => item.OwnerSubject == clientId && item.MoodleAlias == clientEntity.MoodleAlias).ExecuteDeleteAsync(cancellationToken);
            await dbContext.MoodleUserLinks.Where(item => item.Subject == clientId && item.MoodleAlias == clientEntity.MoodleAlias).ExecuteDeleteAsync(cancellationToken);
            // Audit logs are retained for accountability and are never deleted here.
        }

        // Snapshots and durable refresh states are derived from this Moodle
        // connection. Keeping them after disconnect would both waste storage
        // and allow a later reconnect to observe stale data.
        dbContext.MoodleSnapshots.RemoveRange(await dbContext.MoodleSnapshots
            .Where(item => item.OwnerId == userId && item.ConnectionAlias == clientEntity.MoodleAlias)
            .ToListAsync(cancellationToken));
        dbContext.MoodleSyncStates.RemoveRange(await dbContext.MoodleSyncStates
            .Where(item => item.OwnerId == userId && item.ConnectionAlias == clientEntity.MoodleAlias)
            .ToListAsync(cancellationToken));

        dbContext.ConnectorClients.Remove(clientEntity);

        // Verify if it was the last default, and pick another one to be default if exists
        if (clientEntity.IsDefault)
        {
            var nextDefault = await dbContext.ConnectorClients
                .Where(c => c.ClientId == clientId && c.Id != moodleId && c.IsActive)
                .FirstOrDefaultAsync(cancellationToken);
            
            if (nextDefault != null)
            {
                nextDefault.IsDefault = true;
                nextDefault.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAccountAsync(DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.ConfirmationText?.Trim(), "EXCLUIR MINHA CONTA", StringComparison.Ordinal))
            throw new InvalidOperationException("Digite EXCLUIR MINHA CONTA para confirmar a exclusão definitiva.");

        var account = await dbContext.UserAccounts
            .SingleOrDefaultAsync(user => user.Id == request.UserId, cancellationToken)
            ?? throw new InvalidOperationException("Usuário não encontrado.");
        if (string.IsNullOrWhiteSpace(request.Password) || !PasswordHasher.Verify(request.Password, account.PasswordHash))
            throw new InvalidOperationException("Senha atual inválida.");

        var clientId = account.ConnectorClientId ?? account.Id.ToString();
        var subjects = new[] { account.Id.ToString(), clientId }.Distinct().ToArray();
        dbContext.MoodleSnapshots.RemoveRange(await dbContext.MoodleSnapshots
            .Where(item => item.OwnerId == request.UserId)
            .ToListAsync(cancellationToken));
        dbContext.MoodleSyncStates.RemoveRange(await dbContext.MoodleSyncStates
            .Where(item => item.OwnerId == request.UserId)
            .ToListAsync(cancellationToken));
        var pendingActions = await dbContext.PendingMoodleActions
            .Where(action => subjects.Contains(action.CreatedBySubject))
            .ToListAsync(cancellationToken);
        var pendingActionIds = pendingActions.Select(action => action.Id).ToArray();

        dbContext.ConfirmedMoodleActions.RemoveRange(await dbContext.ConfirmedMoodleActions
            .Where(action => subjects.Contains(action.ConfirmedBySubject) || pendingActionIds.Contains(action.PendingActionId))
            .ToListAsync(cancellationToken));
        dbContext.PendingMoodleActions.RemoveRange(pendingActions);
        dbContext.MoodleAuditLogs.RemoveRange(await dbContext.MoodleAuditLogs
            .Where(log => subjects.Contains(log.ActorSubject))
            .ToListAsync(cancellationToken));
        dbContext.MoodleUserLinks.RemoveRange(await dbContext.MoodleUserLinks
            .Where(link => subjects.Contains(link.Subject))
            .ToListAsync(cancellationToken));
        dbContext.UserMemories.RemoveRange(await dbContext.UserMemories
            .Where(memory => subjects.Contains(memory.OwnerSubject))
            .ToListAsync(cancellationToken));
        dbContext.UserMemoryDocuments.RemoveRange(await dbContext.UserMemoryDocuments
            .Where(document => subjects.Contains(document.OwnerSubject))
            .ToListAsync(cancellationToken));
        dbContext.GradingBatches.RemoveRange(await dbContext.GradingBatches
            .Where(batch => subjects.Contains(batch.CreatedBySubject))
            .ToListAsync(cancellationToken));
        dbContext.ConnectorClients.RemoveRange(await dbContext.ConnectorClients
            .Where(connection => connection.ClientId == clientId)
            .ToListAsync(cancellationToken));
        var oauthAuthorizations = await dbContext.OAuthAuthorizations
            .Where(authorization => authorization.Subject != null && subjects.Contains(authorization.Subject))
            .ToListAsync(cancellationToken);
        var oauthAuthorizationIds = oauthAuthorizations.Select(authorization => authorization.Id).ToArray();
        dbContext.OAuthTokens.RemoveRange(await dbContext.OAuthTokens
            .Where(token =>
                EF.Property<string?>(token, "AuthorizationId") != null &&
                oauthAuthorizationIds.Contains(EF.Property<string>(token, "AuthorizationId")))
            .ToListAsync(cancellationToken));
        dbContext.OAuthAuthorizations.RemoveRange(oauthAuthorizations);
        dbContext.UserAccounts.Remove(account);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizeName(string name)
    {
        var normalized = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        if (normalized.Length is < 2 or > 120)
        {
            throw new ArgumentException("Nome deve ter entre 2 e 120 caracteres.");
        }

        return normalized;
    }

    private static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) ||
            password.Length < MinimumPasswordLength)
        {
            throw new ArgumentException($"A senha deve ter pelo menos {MinimumPasswordLength} caracteres.");
        }

        if (password.Length > MaximumPasswordLength)
        {
            throw new ArgumentException($"A senha deve ter no máximo {MaximumPasswordLength} caracteres.");
        }
    }

    private static bool TryNormalizeEmail(string email, out string normalized)
    {
        try
        {
            normalized = NormalizeEmailOrThrow(email);
            return true;
        }
        catch (ArgumentException)
        {
            normalized = string.Empty;
            return false;
        }
    }

    private static string NormalizeEmailOrThrow(string email)
    {
        var trimmed = string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim();
        if (trimmed.Length is 0 or > 320)
        {
            throw new ArgumentException("E-mail inválido.");
        }

        try
        {
            var address = new MailAddress(trimmed);
            if (!string.Equals(address.Address, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("E-mail inválido.");
            }

            return address.Address.ToLowerInvariant();
        }
        catch (FormatException)
        {
            throw new ArgumentException("E-mail inválido.");
        }
    }

    private static string NormalizeMoodleBaseUrl(string moodleBaseUrl)
    {
        var trimmed = string.IsNullOrWhiteSpace(moodleBaseUrl) ? string.Empty : moodleBaseUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("URL do Moodle deve ser HTTPS, absoluta e sem credenciais na URL.");
        }

        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    private static AccountDto ToDto(UserAccountEntity e) =>
        new(e.Id, e.Name, e.Email, e.ConnectorClientId is not null, e.ConnectorClientId);
}
