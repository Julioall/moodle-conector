using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure;

internal sealed class PlatformPermissionService(ConnectorDbContext dbContext) : IPlatformPermissionService
{
    // Temporariamente, todos os usuários recebem o catálogo completo para manter
    // os fluxos do conector operacionais enquanto o RBAC ainda está sendo validado.
    private static readonly string[] DefaultPermissions = PlatformPermissionCatalog.All;

    public async Task EnsureDefaultPermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var existingMembership = await dbContext.PermissionGroupMemberships
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (existingMembership is not null)
        {
            var existingPermissions = await dbContext.PermissionGroupPermissions
                .Where(item => item.GroupId == existingMembership.GroupId)
                .Select(item => item.Permission)
                .ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken);
            var missingPermissions = DefaultPermissions.Where(permission => !existingPermissions.Contains(permission));
            dbContext.PermissionGroupPermissions.AddRange(missingPermissions.Select(permission => new PermissionGroupPermissionEntity
            {
                Id = Guid.NewGuid(), GroupId = existingMembership.GroupId, Permission = permission
            }));
            await dbContext.SaveChangesAsync(cancellationToken);
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
        dbContext.PermissionGroupPermissions.AddRange(DefaultPermissions.Select(permission => new PermissionGroupPermissionEntity
        {
            Id = Guid.NewGuid(), GroupId = group.Id, Permission = permission
        }));
        dbContext.PermissionGroupMemberships.Add(new PermissionGroupMembershipEntity
        {
            Id = Guid.NewGuid(), GroupId = group.Id, UserId = userId
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PermissionGroupDto> CreateGroupAsync(CreatePermissionGroupRequest request, CancellationToken cancellationToken)
    {
        await EnsureAdministratorAsync(request.ActorUserId, cancellationToken);
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
        await EnsureAdministratorAsync(request.ActorUserId, cancellationToken);
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
        await EnsureAdministratorAsync(request.ActorUserId, cancellationToken);
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

    private async Task EnsureAdministratorAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!await dbContext.TeamMemberships.AnyAsync(item => item.UserId == userId && item.IsActive && item.Role == "administrator", cancellationToken))
            throw new InvalidOperationException("Apenas administradores podem alterar grupos e permissões.");
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
    public static readonly string[] AllRead =
    [
        "tool.assignments.view", "tool.messages.view", "tool.reports.view", "tool.courses.view",
        "tool.students.view", "tool.classroom.view", "tool.followup.view", "tool.forums.view",
        "tool.connections.manage", "tool.memory.manage", "tool.pedagogy.view"
    ];

    public static readonly string[] AllWrite =
    ["tool.assignments.grade", "tool.messages.send"];

    public static readonly string[] All =
    [
        "tool.assignments.view", "tool.assignments.grade", "tool.messages.view",
        "tool.messages.send", "tool.reports.view", "tool.courses.view", "tool.students.view",
        "tool.classroom.view", "tool.followup.view", "tool.forums.view", "tool.connections.manage",
        "tool.memory.manage", "tool.pedagogy.view"
    ];
}
