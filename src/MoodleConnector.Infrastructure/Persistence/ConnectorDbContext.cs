using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;
using MoodleConnector.Domain;

namespace MoodleConnector.Infrastructure;

public sealed class ConnectorDbContext(DbContextOptions<ConnectorDbContext> options) : DbContext(options)
{
    public DbSet<ConnectorClientCredentialEntity> ConnectorClients => Set<ConnectorClientCredentialEntity>();
    public DbSet<UserAccountEntity> UserAccounts => Set<UserAccountEntity>();
    public DbSet<PendingMoodleAction> PendingMoodleActions => Set<PendingMoodleAction>();
    public DbSet<ConfirmedMoodleAction> ConfirmedMoodleActions => Set<ConfirmedMoodleAction>();
    public DbSet<MoodleAuditLog> MoodleAuditLogs => Set<MoodleAuditLog>();
    public DbSet<MoodleUserLink> MoodleUserLinks => Set<MoodleUserLink>();
    public DbSet<OpenIddictEntityFrameworkCoreApplication> OAuthApplications => Set<OpenIddictEntityFrameworkCoreApplication>();
    public DbSet<OpenIddictEntityFrameworkCoreAuthorization> OAuthAuthorizations => Set<OpenIddictEntityFrameworkCoreAuthorization>();
    public DbSet<OpenIddictEntityFrameworkCoreScope> OAuthScopes => Set<OpenIddictEntityFrameworkCoreScope>();
    public DbSet<OpenIddictEntityFrameworkCoreToken> OAuthTokens => Set<OpenIddictEntityFrameworkCoreToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ConnectorClientCredentialEntity>();
        entity.ToTable("connector_clients");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id).HasMaxLength(64);
        entity.Property(x => x.ClientId).HasMaxLength(64).IsRequired();
        entity.Property(x => x.ApiKeyHash).HasMaxLength(128);
        entity.Property(x => x.MoodleAlias).HasMaxLength(64).IsRequired();
        entity.Property(x => x.MoodleBaseUrl).HasMaxLength(512).IsRequired();
        entity.Property(x => x.MoodleUsernameEncrypted).IsRequired();
        entity.Property(x => x.MoodlePasswordEncrypted).IsRequired();
        entity.Property(x => x.MoodleTarget).HasMaxLength(32).IsRequired();
        entity.Property(x => x.IsDefault).IsRequired();
        entity.Property(x => x.IsActive).IsRequired();
        entity.Property(x => x.CanWrite).IsRequired();
        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.Property(x => x.UpdatedAtUtc).IsRequired();

        entity.HasIndex(x => x.ApiKeyHash).IsUnique();
        entity.HasIndex(x => new { x.ClientId, x.MoodleAlias }).IsUnique();
        entity.HasIndex(x => new { x.ClientId, x.IsDefault });

        var userEntity = modelBuilder.Entity<UserAccountEntity>();
        userEntity.ToTable("user_accounts");
        userEntity.HasKey(x => x.Id);
        userEntity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        userEntity.Property(x => x.Email).HasMaxLength(320).IsRequired();
        userEntity.Property(x => x.PasswordHash).IsRequired();
        userEntity.Property(x => x.ConnectorClientId).HasMaxLength(64);
        userEntity.Property(x => x.CreatedAtUtc).IsRequired();
        userEntity.Property(x => x.UpdatedAtUtc).IsRequired();
        userEntity.HasIndex(x => x.Email).IsUnique();

        var pendingAction = modelBuilder.Entity<PendingMoodleAction>();
        pendingAction.ToTable("moodle_pending_actions");
        pendingAction.HasKey(x => x.Id);
        pendingAction.Property(x => x.ToolName).HasMaxLength(120).IsRequired();
        pendingAction.Property(x => x.RiskLevel).HasConversion<int>().IsRequired();
        pendingAction.Property(x => x.CreatedBySubject).HasMaxLength(200).IsRequired();
        pendingAction.Property(x => x.CreatedByEmail).HasMaxLength(320);
        pendingAction.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        pendingAction.Property(x => x.PreviewJson).HasColumnType("jsonb").IsRequired();
        pendingAction.Property(x => x.ConfirmationText).HasMaxLength(500).IsRequired();
        pendingAction.Property(x => x.Status).HasConversion<int>().IsRequired();
        pendingAction.Property(x => x.IdempotencyKey).HasMaxLength(64).IsRequired();
        pendingAction.Property(x => x.CorrelationId).HasMaxLength(64).IsRequired();
        pendingAction.HasIndex(x => x.CorrelationId);
        pendingAction.HasIndex(x => new { x.CreatedBySubject, x.Status });

        var confirmedAction = modelBuilder.Entity<ConfirmedMoodleAction>();
        confirmedAction.ToTable("moodle_confirmed_actions");
        confirmedAction.HasKey(x => x.Id);
        confirmedAction.Property(x => x.ToolName).HasMaxLength(120).IsRequired();
        confirmedAction.Property(x => x.ConfirmedBySubject).HasMaxLength(200).IsRequired();
        confirmedAction.Property(x => x.CorrelationId).HasMaxLength(64).IsRequired();
        confirmedAction.HasIndex(x => x.PendingActionId).IsUnique();

        var auditLog = modelBuilder.Entity<MoodleAuditLog>();
        auditLog.ToTable("moodle_audit_logs");
        auditLog.HasKey(x => x.Id);
        auditLog.Property(x => x.CorrelationId).HasMaxLength(64).IsRequired();
        auditLog.Property(x => x.ToolName).HasMaxLength(120).IsRequired();
        auditLog.Property(x => x.RiskLevel).HasConversion<int>().IsRequired();
        auditLog.Property(x => x.ActorSubject).HasMaxLength(200).IsRequired();
        auditLog.Property(x => x.ActorEmail).HasMaxLength(320);
        auditLog.Property(x => x.MoodleFunction).HasMaxLength(120);
        auditLog.Property(x => x.RequestSanitizedJson).HasColumnType("jsonb").IsRequired();
        auditLog.Property(x => x.ResponseSummaryJson).HasColumnType("jsonb").IsRequired();
        auditLog.Property(x => x.Status).HasMaxLength(80).IsRequired();
        auditLog.Property(x => x.ErrorCode).HasMaxLength(120);
        auditLog.HasIndex(x => x.CorrelationId);
        auditLog.HasIndex(x => new { x.ActorSubject, x.CreatedAt });

        var moodleUserLink = modelBuilder.Entity<MoodleUserLink>();
        moodleUserLink.ToTable("moodle_user_links");
        moodleUserLink.HasKey(x => x.Id);
        moodleUserLink.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        moodleUserLink.Property(x => x.Email).HasMaxLength(320);
        moodleUserLink.Property(x => x.MoodleAlias).HasMaxLength(64).IsRequired();
        moodleUserLink.HasIndex(x => new { x.Subject, x.MoodleAlias }).IsUnique();
    }
}
