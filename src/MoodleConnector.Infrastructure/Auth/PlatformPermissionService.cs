using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure;

internal sealed class PlatformPermissionService(ConnectorDbContext dbContext) : IPlatformPermissionService
{
    public async Task EnsureDefaultPermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var existingMembership = await dbContext.PermissionGroupMemberships
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (existingMembership is not null)
        {
            // Existing groups are user-managed authorization state. Never
            // expand them on login when the catalog changes.
            return;
        }

        var group = new PermissionGroupEntity
        {
            Id = Guid.NewGuid(),
            Name = "Acesso inicial",
            Description = "Permissões básicas de leitura e gerenciamento da própria conexão.",
            CreatedByUserId = userId
        };
        dbContext.PermissionGroups.Add(group);
        // A new account receives only the ability to configure its own
        // permission groups. Tool access must be granted explicitly by the
        // user through groups or direct overrides.
        dbContext.PermissionGroupPermissions.Add(new PermissionGroupPermissionEntity
        {
            Id = Guid.NewGuid(), GroupId = group.Id, Permission = PlatformPermissionCatalog.PermissionGroupsManage
        });
        dbContext.PermissionGroupMemberships.Add(new PermissionGroupMembershipEntity
        {
            Id = Guid.NewGuid(), GroupId = group.Id, UserId = userId
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PermissionGroupDto> CreateGroupAsync(CreatePermissionGroupRequest request, CancellationToken cancellationToken)
    {
        await EnsureCanManageGroupsAsync(request.ActorUserId, cancellationToken);
        var permissions = NormalizePermissions(request.Permissions);
        var group = new PermissionGroupEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            CreatedByUserId = request.ActorUserId
        };
        dbContext.PermissionGroups.Add(group);
        dbContext.PermissionGroupPermissions.AddRange(permissions.Select(permission => new PermissionGroupPermissionEntity
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            Permission = permission
        }));
        await dbContext.SaveChangesAsync(cancellationToken);
        return new PermissionGroupDto(group.Id, group.Name, group.Description, permissions);
    }

    public async Task AddMemberAsync(AddPermissionGroupMemberRequest request, CancellationToken cancellationToken)
    {
        await EnsureCanManageGroupsAsync(request.ActorUserId, cancellationToken);
        if (!await dbContext.PermissionGroups.AnyAsync(item => item.Id == request.GroupId, cancellationToken))
            throw new InvalidOperationException("Grupo de permissões não encontrado.");
        if (!await dbContext.PermissionGroupMemberships.AnyAsync(item => item.GroupId == request.GroupId && item.UserId == request.UserId, cancellationToken))
        {
            dbContext.PermissionGroupMemberships.Add(new PermissionGroupMembershipEntity
            {
                Id = Guid.NewGuid(), GroupId = request.GroupId, UserId = request.UserId
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task SetUserPermissionAsync(SetUserPermissionRequest request, CancellationToken cancellationToken)
    {
        await EnsureCanManageGroupsAsync(request.ActorUserId, cancellationToken);
        var permission = NormalizePermission(request.Permission);
        var existing = await dbContext.UserPermissionOverrides.FirstOrDefaultAsync(item => item.UserId == request.UserId && item.Permission == permission, cancellationToken);
        if (existing is null)
        {
            dbContext.UserPermissionOverrides.Add(new UserPermissionOverrideEntity
            {
                Id = Guid.NewGuid(), UserId = request.UserId, Permission = permission,
                IsAllowed = request.IsAllowed, ChangedByUserId = request.ActorUserId
            });
        }
        else
        {
            existing.IsAllowed = request.IsAllowed;
            existing.ChangedByUserId = request.ActorUserId;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var groupPermissions = await (from membership in dbContext.PermissionGroupMemberships.AsNoTracking()
                                      join permission in dbContext.PermissionGroupPermissions.AsNoTracking() on membership.GroupId equals permission.GroupId
                                      where membership.UserId == userId
                                      select permission.Permission).ToArrayAsync(cancellationToken);
        var overrides = await dbContext.UserPermissionOverrides.AsNoTracking().Where(item => item.UserId == userId).ToArrayAsync(cancellationToken);
        var permissions = groupPermissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var grant in overrides.Where(item => item.IsAllowed)) permissions.Add(grant.Permission);
        foreach (var deny in overrides.Where(item => !item.IsAllowed)) permissions.Remove(deny.Permission);
        return permissions.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<IReadOnlyList<PermissionGroupDto>> GetGroupsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await (from membership in dbContext.PermissionGroupMemberships.AsNoTracking()
                          join teamGroup in dbContext.PermissionGroups.AsNoTracking() on membership.GroupId equals teamGroup.Id
                          join permission in dbContext.PermissionGroupPermissions.AsNoTracking() on teamGroup.Id equals permission.GroupId into permissions
                          where membership.UserId == userId
                          select new { teamGroup, permissions }).ToArrayAsync(cancellationToken);
        return rows.Select(row => new PermissionGroupDto(row.teamGroup.Id, row.teamGroup.Name, row.teamGroup.Description, row.permissions.Select(item => item.Permission).ToArray())).ToArray();
    }

    private async Task EnsureCanManageGroupsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var effective = await GetEffectivePermissionsAsync(userId, cancellationToken);
        if (!effective.Contains(PlatformPermissionCatalog.PermissionGroupsManage, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("O usuário não possui permissão para gerenciar grupos de permissões.");
    }

    private static IReadOnlyCollection<string> NormalizePermissions(IEnumerable<string> permissions) =>
        permissions.Select(NormalizePermission).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string NormalizePermission(string permission)
    {
        var value = permission.Trim().ToLowerInvariant();
        if (!PlatformPermissionCatalog.All.Contains(value, StringComparer.Ordinal))
            throw new ArgumentException("Permissão de plataforma inválida.", nameof(permission));
        return value;
    }
}

public static class PlatformPermissionCatalog
{
    public const string PermissionGroupsManage = "tool.permission_groups.manage";
    public const string PendingActionsManage = "tool.pending_actions.manage";
    public const string TeamsManage = "tool.teams.manage";
    public static readonly string[] PortalPermissions =
    [
        "dashboard.view", "courses.view", "schools.view", "students.view", "students.followup.write",
        "tasks.manage", "agenda.manage", "messages.prepare", "automations.view", "automations.manage", "grading.view", "grading.manage", "reports.view",
        "connections.manage", "settings.view", "admin.view"
    ];
    public static readonly string[] AllRead =
    [
        "tool.assignments.view", "tool.messages.view", "tool.reports.view", "tool.courses.view",
        "tool.students.view", "tool.classroom.view", "tool.followup.view", "tool.forums.view",
        "tool.connections.manage", "tool.memory.manage", "tool.pedagogy.view"
    ];

    public static readonly string[] AllWrite =
    ["tool.assignments.grade", "tool.messages.send", "tool.forums.write"];

    public static readonly string[] All =
    [
        "tool.assignments.view", "tool.assignments.grade", "tool.messages.view",
        "tool.messages.send", "tool.reports.view", "tool.courses.view", "tool.students.view",
        "tool.classroom.view", "tool.followup.view", "tool.forums.view", "tool.forums.write", "tool.connections.manage",
        "tool.memory.manage", "tool.pedagogy.view", PermissionGroupsManage, PendingActionsManage, TeamsManage,
        ..PortalPermissions
    ];
}
