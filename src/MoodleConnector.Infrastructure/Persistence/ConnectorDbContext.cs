using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using OpenIddict.EntityFrameworkCore.Models;
using System.Text.Json;
using MoodleConnector.Domain;
using MoodleConnector.Domain.Grading;

namespace MoodleConnector.Infrastructure;

public sealed class ConnectorDbContext(DbContextOptions<ConnectorDbContext> options) : DbContext(options)
{
    public DbSet<ConnectorClientCredentialEntity> ConnectorClients => Set<ConnectorClientCredentialEntity>();
    public DbSet<UserAccountEntity> UserAccounts => Set<UserAccountEntity>();
    public DbSet<PendingMoodleAction> PendingMoodleActions => Set<PendingMoodleAction>();
    public DbSet<ConfirmedMoodleAction> ConfirmedMoodleActions => Set<ConfirmedMoodleAction>();
    public DbSet<MoodleAuditLog> MoodleAuditLogs => Set<MoodleAuditLog>();
    public DbSet<MoodleUserLink> MoodleUserLinks => Set<MoodleUserLink>();
    public DbSet<AssistedGradingBatch> GradingBatches => Set<AssistedGradingBatch>();
    public DbSet<AssistedGradingItem> GradingItems => Set<AssistedGradingItem>();
    public DbSet<GradingArtifact> GradingArtifacts => Set<GradingArtifact>();
    public DbSet<GradingEvidence> GradingEvidence => Set<GradingEvidence>();
    public DbSet<UserMemory> UserMemories => Set<UserMemory>();
    public DbSet<UserMemoryDocument> UserMemoryDocuments => Set<UserMemoryDocument>();
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
        auditLog.Property(x => x.BatchJobId);
        auditLog.Property(x => x.ToolName).HasMaxLength(120).IsRequired();
        auditLog.Property(x => x.RiskLevel).HasConversion<int>().IsRequired();
        auditLog.Property(x => x.ActorSubject).HasMaxLength(200).IsRequired();
        auditLog.Property(x => x.ActorEmail).HasMaxLength(320);
        auditLog.Property(x => x.MoodleConnectionId).HasMaxLength(128);
        auditLog.Property(x => x.MoodleConnectionAlias).HasMaxLength(128);
        auditLog.Property(x => x.MoodleFunction).HasMaxLength(120);
        auditLog.Property(x => x.PendingActionId);
        auditLog.Property(x => x.StartedAt);
        auditLog.Property(x => x.FinishedAt);
        auditLog.Property(x => x.DurationMs);
        auditLog.Property(x => x.RequestSanitizedJson).HasColumnType("jsonb").IsRequired();
        auditLog.Property(x => x.ResponseSummaryJson).HasColumnType("jsonb").IsRequired();
        auditLog.Property(x => x.Status).HasMaxLength(80).IsRequired();
        auditLog.Property(x => x.ErrorCode).HasMaxLength(120);
        auditLog.HasIndex(x => x.CorrelationId);
        auditLog.HasIndex(x => new { x.BatchJobId, x.CreatedAt });
        auditLog.HasIndex(x => new { x.ActorSubject, x.CreatedAt });
        auditLog.HasIndex(x => new { x.MoodleConnectionId, x.CreatedAt });

        var moodleUserLink = modelBuilder.Entity<MoodleUserLink>();
        moodleUserLink.ToTable("moodle_user_links");
        moodleUserLink.HasKey(x => x.Id);
        moodleUserLink.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        moodleUserLink.Property(x => x.Email).HasMaxLength(320);
        moodleUserLink.Property(x => x.MoodleAlias).HasMaxLength(64).IsRequired();
        moodleUserLink.HasIndex(x => new { x.Subject, x.MoodleAlias }).IsUnique();

        ConfigureGrading(modelBuilder);
        ConfigureUserMemories(modelBuilder);
    }

    private static void ConfigureUserMemories(ModelBuilder modelBuilder)
    {
        var memory = modelBuilder.Entity<UserMemory>();
        memory.ToTable("user_memories");
        memory.HasKey(x => x.Id);
        memory.Property(x => x.OwnerSubject).HasMaxLength(200).IsRequired();
        memory.Property(x => x.Category).HasMaxLength(32).IsRequired();
        memory.Property(x => x.NormalizedKey).HasMaxLength(120).IsRequired();
        memory.Property(x => x.Content).HasMaxLength(1000).IsRequired();
        memory.Property(x => x.Origin).HasMaxLength(32).IsRequired();
        memory.Property(x => x.MoodleAlias).HasMaxLength(64);
        memory.Property(x => x.CourseId).HasMaxLength(64);
        memory.Property(x => x.LinkedDocumentId);
        memory.Property(x => x.CreatedAtUtc).IsRequired();
        memory.Property(x => x.UpdatedAtUtc).IsRequired();
        memory.HasIndex(x => new { x.OwnerSubject, x.MoodleAlias, x.CourseId, x.UpdatedAtUtc })
            .IsDescending(false, false, false, true);
        memory.HasIndex(x => new { x.OwnerSubject, x.Category, x.MoodleAlias, x.CourseId, x.NormalizedKey })
            .IsUnique()
            .AreNullsDistinct(false);

        var document = modelBuilder.Entity<UserMemoryDocument>();
        document.ToTable("user_memory_documents");
        document.HasKey(x => x.Id);
        document.Property(x => x.OwnerSubject).HasMaxLength(200).IsRequired();
        document.Property(x => x.NormalizedKey).HasMaxLength(120).IsRequired();
        document.Property(x => x.Title).HasMaxLength(200).IsRequired();
        document.Property(x => x.Content).HasMaxLength(200000).IsRequired();
        document.Property(x => x.Format).HasMaxLength(32).IsRequired();
        document.Property(x => x.Origin).HasMaxLength(32).IsRequired();
        document.Property(x => x.MoodleAlias).HasMaxLength(64);
        document.Property(x => x.CourseId).HasMaxLength(64);
        document.Property(x => x.CreatedAtUtc).IsRequired();
        document.Property(x => x.UpdatedAtUtc).IsRequired();
        document.HasIndex(x => new { x.OwnerSubject, x.MoodleAlias, x.CourseId, x.UpdatedAtUtc })
            .IsDescending(false, false, false, true);
        document.HasIndex(x => new { x.OwnerSubject, x.MoodleAlias, x.CourseId, x.NormalizedKey })
            .IsUnique()
            .AreNullsDistinct(false);
    }

    private static void ConfigureGrading(ModelBuilder modelBuilder)
    {
        var batch = modelBuilder.Entity<AssistedGradingBatch>();
        batch.ToTable("grading_batch");
        batch.HasKey(x => x.Id);
        batch.Property(x => x.AssignmentIds)
            .HasColumnName("AssignmentIdsJson")
            .HasColumnType("jsonb")
            .HasConversion(
                ids => JsonSerializer.Serialize(ids, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<long[]>(json, (JsonSerializerOptions?)null) ?? Array.Empty<long>())
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<long>>(
                (left, right) => left != null && right != null && left.SequenceEqual(right),
                value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                value => value.ToArray()));
        batch.Property(x => x.CreatedBySubject).HasMaxLength(200).IsRequired();
        batch.Property(x => x.Status).HasConversion<string>().HasMaxLength(80).IsRequired();
        batch.HasIndex(x => new { x.CreatedBySubject, x.Status });
        batch.HasIndex(x => new { x.CourseId, x.Status });

        var item = modelBuilder.Entity<AssistedGradingItem>();
        item.ToTable("grading_item");
        item.HasKey(x => x.Id);
        item.Property(x => x.Status).HasConversion<string>().HasMaxLength(80).IsRequired();
        item.Property(x => x.ReviewStatus).HasConversion<string>().HasMaxLength(80).IsRequired();
        item.Property(x => x.CommitStatus).HasConversion<string>().HasMaxLength(80).IsRequired();
        item.Property(x => x.TeacherDecision).HasMaxLength(80);
        item.Property(x => x.PrivateNotesToTeacher);
        item.Property(x => x.ReviewedBySubject).HasMaxLength(200);
        item.Property(x => x.IdempotencyKey).HasMaxLength(64);
        item.HasOne<AssistedGradingBatch>()
            .WithMany()
            .HasForeignKey(x => x.BatchId)
            .OnDelete(DeleteBehavior.Cascade);
        item.HasIndex(x => new { x.BatchId, x.Status });
        item.HasIndex(x => new { x.AssignmentId, x.MoodleUserId });
        item.HasIndex(x => x.ReviewStatus);
        item.HasIndex(x => x.CommitStatus);
        item.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");

        var artifact = modelBuilder.Entity<GradingArtifact>();
        artifact.ToTable("grading_artifact");
        artifact.HasKey(x => x.Id);
        artifact.Property(x => x.ArtifactType).HasMaxLength(80).IsRequired();
        artifact.Property(x => x.Filename).HasMaxLength(512);
        artifact.Property(x => x.MimeType).HasMaxLength(160);
        artifact.Property(x => x.Sha256).HasMaxLength(64);
        artifact.Property(x => x.ExtractionStatus).HasMaxLength(80).IsRequired();
        artifact.HasOne<AssistedGradingItem>()
            .WithMany()
            .HasForeignKey(x => x.GradingItemId)
            .OnDelete(DeleteBehavior.Cascade);
        artifact.HasIndex(x => x.GradingItemId);
        artifact.HasIndex(x => x.Sha256).HasFilter("\"Sha256\" IS NOT NULL");

        var evidence = modelBuilder.Entity<GradingEvidence>();
        evidence.ToTable("grading_evidence");
        evidence.HasKey(x => x.Id);
        evidence.Property(x => x.CriterionId).HasMaxLength(120);
        evidence.HasOne<AssistedGradingItem>()
            .WithMany()
            .HasForeignKey(x => x.GradingItemId)
            .OnDelete(DeleteBehavior.Cascade);
        evidence.HasIndex(x => x.GradingItemId);
    }
}
