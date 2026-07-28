using Microsoft.EntityFrameworkCore;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class AccountMoodleUpdateTests
{
    [Fact]
    public async Task UpdateMoodleAsync_RejectsCanonicalAliasCollisionBeforeSaving()
    {
        var options = new DbContextOptionsBuilder<ConnectorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new ConnectorDbContext(options);
        var userId = Guid.NewGuid();
        var clientId = userId.ToString();
        dbContext.UserAccounts.Add(new UserAccountEntity
        {
            Id = userId,
            Name = "Teacher",
            Email = "teacher@example.com",
            PasswordHash = "hash",
            ConnectorClientId = clientId
        });
        dbContext.ConnectorClients.AddRange(
            Connection("connection-a", clientId, "nacional", isDefault: true),
            Connection("connection-b", clientId, "Goiás", isDefault: false));
        await dbContext.SaveChangesAsync();
        var sut = new AccountService(
            dbContext,
            new PassThroughProtector(),
            null!,
            new AlwaysValidCredentialValidator());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.UpdateMoodleAsync(
                new UpdateMoodleAccountRequest(
                    userId,
                    "connection-a",
                    "  GOIÁS ",
                    "https://ead.senai.br",
                    MoodleUsername: null,
                    MoodlePassword: null,
                    IsDefault: true,
                    CanWrite: false),
                CancellationToken.None));

        Assert.Contains("alias 'goias'", exception.Message, StringComparison.Ordinal);
        Assert.Equal("nacional", (await dbContext.ConnectorClients.FindAsync("connection-a"))!.MoodleAlias);
    }

    private static ConnectorClientCredentialEntity Connection(
        string id,
        string clientId,
        string alias,
        bool isDefault) => new()
    {
        Id = id,
        ClientId = clientId,
        MoodleAlias = alias,
        MoodleTarget = alias,
        MoodleBaseUrl = "https://moodle.example.com",
        MoodleUsernameEncrypted = "user",
        MoodlePasswordEncrypted = "password",
        IsDefault = isDefault,
        IsActive = true
    };

    private sealed class PassThroughProtector : IConnectorSecretProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string ciphertext) => ciphertext;
    }

    private sealed class AlwaysValidCredentialValidator : IMoodleCredentialValidator
    {
        public Task<bool> ValidateAsync(
            string moodleBaseUrl,
            string username,
            string password,
            CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
