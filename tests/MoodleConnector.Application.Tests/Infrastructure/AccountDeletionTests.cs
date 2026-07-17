using Microsoft.EntityFrameworkCore;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Infrastructure;

namespace MoodleConnector.Application.Tests.Infrastructure;

public sealed class AccountDeletionTests
{
    [Fact]
    public async Task DeleteAccountAsync_RemovesAccountAndConnections()
    {
        var options = new DbContextOptionsBuilder<ConnectorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new ConnectorDbContext(options);
        var userId = Guid.NewGuid();
        var clientId = userId.ToString();
        dbContext.UserAccounts.Add(new UserAccountEntity
        {
            Id = userId,
            Name = "Usuario Teste",
            Email = "usuario@example.com",
            PasswordHash = PasswordHasher.Hash("senha-segura-123"),
            ConnectorClientId = clientId
        });
        dbContext.ConnectorClients.Add(new ConnectorClientCredentialEntity
        {
            Id = $"{clientId}:default",
            ClientId = clientId,
            MoodleAlias = "default",
            MoodleBaseUrl = "https://moodle.example.com",
            MoodleUsernameEncrypted = "user",
            MoodlePasswordEncrypted = "password",
            MoodleTarget = "default",
            IsDefault = true,
            IsActive = true
        });
        await dbContext.SaveChangesAsync();

        var service = new AccountService(dbContext, null!, null!, null!);
        await service.DeleteAccountAsync(
            new DeleteAccountRequest(userId, "senha-segura-123", "EXCLUIR MINHA CONTA"),
            CancellationToken.None);

        Assert.Empty(await dbContext.UserAccounts.ToListAsync());
        Assert.Empty(await dbContext.ConnectorClients.ToListAsync());
    }

    [Theory]
    [InlineData("senha-incorreta", "EXCLUIR MINHA CONTA")]
    [InlineData("senha-segura-123", "excluir minha conta")]
    public async Task DeleteAccountAsync_RejectsInvalidConfirmation(string password, string confirmation)
    {
        var options = new DbContextOptionsBuilder<ConnectorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new ConnectorDbContext(options);
        var userId = Guid.NewGuid();
        dbContext.UserAccounts.Add(new UserAccountEntity
        {
            Id = userId,
            Name = "Usuario Teste",
            Email = "usuario@example.com",
            PasswordHash = PasswordHasher.Hash("senha-segura-123")
        });
        await dbContext.SaveChangesAsync();
        var service = new AccountService(dbContext, null!, null!, null!);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAccountAsync(
            new DeleteAccountRequest(userId, password, confirmation), CancellationToken.None));

        Assert.NotNull(await dbContext.UserAccounts.FindAsync(userId));
    }
}
