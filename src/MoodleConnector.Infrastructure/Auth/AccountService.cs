using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure;

internal sealed class AccountService(
    ConnectorDbContext dbContext,
    IConnectorSecretProtector secretProtector,
    IConnectorClientRegistrationService registrationService,
    IMoodleCredentialValidator moodleValidator) : IAccountService
{
    private const int MinimumPasswordLength = 12;
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
                client.CanWrite))
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

        if (!string.IsNullOrWhiteSpace(request.MoodleUsername) && !string.IsNullOrWhiteSpace(request.MoodlePassword))
        {
            var isValid = await moodleValidator.ValidateAsync(
                moodleBaseUrl,
                request.MoodleUsername.Trim(),
                request.MoodlePassword,
                cancellationToken);

            if (!isValid)
                throw new InvalidOperationException("Credenciais do Moodle inválidas. Verifique seu usuário e senha.");
            
            clientEntity.MoodleUsernameEncrypted = secretProtector.Protect(request.MoodleUsername.Trim());
            clientEntity.MoodlePasswordEncrypted = secretProtector.Protect(request.MoodlePassword);
        }

        clientEntity.MoodleAlias = string.IsNullOrWhiteSpace(request.MoodleAlias) ? "Moodle" : request.MoodleAlias;
        clientEntity.MoodleBaseUrl = moodleBaseUrl;
        clientEntity.CanWrite = request.CanWrite;
        clientEntity.UpdatedAtUtc = DateTimeOffset.UtcNow;

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

    public async Task DeleteMoodleAsync(Guid userId, string moodleId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.UserAccounts
            .FindAsync([userId], cancellationToken)
            ?? throw new InvalidOperationException("Usuário não encontrado.");

        var clientId = entity.ConnectorClientId ?? entity.Id.ToString();

        var clientEntity = await dbContext.ConnectorClients
            .FirstOrDefaultAsync(c => c.Id == moodleId && c.ClientId == clientId, cancellationToken)
            ?? throw new InvalidOperationException("Moodle não encontrado ou acesso negado.");

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

    private static string NormalizeName(string name)
    {
        var normalized = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        if (normalized.Length is < 2 or > 120)
        {
            throw new ArgumentException("Nome deve ter entre 2 e 120 caracteres.");
        }

        return normalized;
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
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("URL do Moodle deve ser uma URL absoluta http/https.");
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
