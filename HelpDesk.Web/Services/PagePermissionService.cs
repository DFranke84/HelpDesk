using System.Security.Claims;
using HelpDesk.Models;
using HelpDesk.Models.Data;
using HelpDesk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Web.Services;

/// <summary>
/// Service for managing page-level CRUD permissions with admin bypass and group access integration.
/// </summary>
public class PagePermissionService : IPagePermissionService
{
    private readonly IDbContextFactory<HelpDeskDbContext> _factory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    // Permissions are deliberately NOT cached: a cached answer kept access alive for minutes after an
    // administrator removed it, and a cache keyed by user id leaked rights between Windows users who
    // share one group row. Each check is a handful of small indexed queries.
    public PagePermissionService(
        IDbContextFactory<HelpDeskDbContext> factory,
        IHttpContextAccessor httpContextAccessor)
    {
        _factory = factory;
        _httpContextAccessor = httpContextAccessor;
    }

    public Task<PagePermissions> GetUserPagePermissionsAsync(int userId, int pageId) =>
        GetPermissionsCoreAsync(userId, GetUserHelpdeskGroupIds(), pageId);

    public Task<PagePermissions> GetPermissionsAsync(ClaimsPrincipal user, int pageId)
    {
        if (user?.Identity?.IsAuthenticated != true
            || !int.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            return Task.FromResult(new PagePermissions());

        return GetPermissionsCoreAsync(userId, DeskGroupAccessResolver.GetWindowsMappedGroupUserIdsFromClaims(user), pageId);
    }

    private async Task<PagePermissions> GetPermissionsCoreAsync(int userId, List<int> groupIds, int pageId)
    {
        await using var db = await _factory.CreateDbContextAsync().ConfigureAwait(false);

        // Check effective role (user + groups) - Admin gets full access
        var effectiveRole = await GetEffectiveUserRoleAsync(db, userId, groupIds).ConfigureAwait(false);
        if (effectiveRole == UserRole.Admin)
        {
            return new PagePermissions
            {
                CanCreate = true,
                CanRead = true,
                CanUpdate = true,
                CanDelete = true
            };
        }

        // Check if user OR any of their groups has access to this page
        var hasGroupAccess = await HasGroupAccessToPageAsync(db, userId, groupIds, pageId).ConfigureAwait(false);
        if (!hasGroupAccess)
            return new PagePermissions();

        // Get user's direct permissions
        var userPermission = await db.UserPagePermissions.AsNoTracking()
            .Where(upp => upp.UserId == userId && upp.PageId == pageId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        var userPerms = userPermission != null
            ? new PagePermissions
            {
                CanCreate = userPermission.CanCreate,
                CanRead = userPermission.CanRead,
                CanUpdate = userPermission.CanUpdate,
                CanDelete = userPermission.CanDelete
            }
            : new PagePermissions();

        // Get permissions from all groups user belongs to (Windows groups mapped to HelpDesk)
        var groupPermissions = new List<PagePermissions>();

        if (groupIds.Any())
        {
            var groupPerms = await db.UserPagePermissions.AsNoTracking()
                .Where(upp => groupIds.Contains(upp.UserId) && upp.PageId == pageId)
                .ToListAsync()
                .ConfigureAwait(false);

            foreach (var groupPerm in groupPerms)
            {
                groupPermissions.Add(new PagePermissions
                {
                    CanCreate = groupPerm.CanCreate,
                    CanRead = groupPerm.CanRead,
                    CanUpdate = groupPerm.CanUpdate,
                    CanDelete = groupPerm.CanDelete
                });
            }
        }

        // Calculate union: most permissive wins
        var allPermissions = new List<PagePermissions> { userPerms };
        allPermissions.AddRange(groupPermissions);
        return UnionPermissions(allPermissions.ToArray());
    }

    public async Task<Dictionary<int, PagePermissions>> GetUserPagePermissionsAsync(int userId, IEnumerable<int> pageIds)
    {
        var result = new Dictionary<int, PagePermissions>();
        var pageIdList = pageIds.ToList();

        await using var db = await _factory.CreateDbContextAsync().ConfigureAwait(false);

        var windowsGroupUserIds = GetUserHelpdeskGroupIds();

        // Check effective role (user + groups) - Admin gets full access
        var effectiveRole = await GetEffectiveUserRoleAsync(db, userId, windowsGroupUserIds).ConfigureAwait(false);
        if (effectiveRole == UserRole.Admin)
        {
            foreach (var pageId in pageIdList)
            {
                result[pageId] = new PagePermissions
                {
                    CanCreate = true,
                    CanRead = true,
                    CanUpdate = true,
                    CanDelete = true
                };
            }
            return result;
        }

        var deskGroupIds = await DeskGroupAccessResolver.GetEffectiveDeskGroupIdsAsync(db, userId, windowsGroupUserIds)
            .ConfigureAwait(false);

        // Get pages user has access to (via any desk group linked to the person or their Windows groups)
        var accessiblePages = await db.Pages.AsNoTracking()
            .Where(p => pageIdList.Contains(p.Id) && p.IsActive)
            .Where(p => p.DeskApplication.ApplicationGroups.Any(ag => deskGroupIds.Contains(ag.DeskGroupId)))
            .Select(p => p.Id)
            .ToListAsync()
            .ConfigureAwait(false);

        // Get permissions of the user and of their Windows groups
        var allEntityIds = new List<int> { userId };
        allEntityIds.AddRange(windowsGroupUserIds);

        var allPermissions = await db.UserPagePermissions.AsNoTracking()
            .Where(upp => allEntityIds.Contains(upp.UserId) && pageIdList.Contains(upp.PageId))
            .ToListAsync()
            .ConfigureAwait(false);

        // Group permissions by page
        var permissionsByPage = allPermissions
            .GroupBy(upp => upp.PageId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var pageId in pageIdList)
        {
            PagePermissions permissions;

            if (!accessiblePages.Contains(pageId))
            {
                // No group access = no permissions
                permissions = new PagePermissions();
            }
            else if (permissionsByPage.TryGetValue(pageId, out var pagePerms))
            {
                // Calculate union of all permissions for this page
                var pagePermissions = pagePerms.Select(p => new PagePermissions
                {
                    CanCreate = p.CanCreate,
                    CanRead = p.CanRead,
                    CanUpdate = p.CanUpdate,
                    CanDelete = p.CanDelete
                }).ToArray();

                permissions = UnionPermissions(pagePermissions);
            }
            else
            {
                // Has group access but no explicit permissions = no access (secure default)
                permissions = new PagePermissions();
            }

            result[pageId] = permissions;
        }

        return result;
    }

    public async Task<bool> CanUserPerformActionAsync(int userId, int pageId, CrudAction action)
    {
        var permissions = await GetUserPagePermissionsAsync(userId, pageId).ConfigureAwait(false);

        return action switch
        {
            CrudAction.Create => permissions.CanCreate,
            CrudAction.Read => permissions.CanRead,
            CrudAction.Update => permissions.CanUpdate,
            CrudAction.Delete => permissions.CanDelete,
            _ => false
        };
    }

    public async Task SetUserPagePermissionsAsync(int userId, int pageId, PagePermissions permissions)
    {
        // Apply permission hierarchy rules
        permissions.ApplyPermissionHierarchy();

        await using var db = await _factory.CreateDbContextAsync().ConfigureAwait(false);

        var existing = await db.UserPagePermissions
            .Where(upp => upp.UserId == userId && upp.PageId == pageId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (existing != null)
        {
            existing.CanCreate = permissions.CanCreate;
            existing.CanRead = permissions.CanRead;
            existing.CanUpdate = permissions.CanUpdate;
            existing.CanDelete = permissions.CanDelete;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else if (permissions.CanCreate || permissions.CanRead || permissions.CanUpdate || permissions.CanDelete)
        {
            // Only create record if user has at least one permission
            var newPermission = new UserPagePermission
            {
                UserId = userId,
                PageId = pageId,
                CanCreate = permissions.CanCreate,
                CanRead = permissions.CanRead,
                CanUpdate = permissions.CanUpdate,
                CanDelete = permissions.CanDelete,
                CreatedAt = DateTime.UtcNow
            };
            db.UserPagePermissions.Add(newPermission);
        }

        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task RemoveUserPagePermissionsAsync(int userId, int pageId)
    {
        await using var db = await _factory.CreateDbContextAsync().ConfigureAwait(false);

        var existing = await db.UserPagePermissions
            .Where(upp => upp.UserId == userId && upp.PageId == pageId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (existing != null)
        {
            db.UserPagePermissions.Remove(existing);
            await db.SaveChangesAsync().ConfigureAwait(false);
        }
    }

    public async Task SetUserReadPermissionAsync(int userId, int pageId, bool canRead)
    {
        await using var db = await _factory.CreateDbContextAsync().ConfigureAwait(false);

        var existing = await db.UserPagePermissions
            .Where(upp => upp.UserId == userId && upp.PageId == pageId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (existing != null)
        {
            existing.CanRead = canRead;
            existing.UpdatedAt = DateTime.UtcNow;

            // If removing read access, remove all other permissions per hierarchy rules
            if (!canRead)
            {
                existing.CanCreate = false;
                existing.CanUpdate = false;
                existing.CanDelete = false;
            }
        }
        else if (canRead)
        {
            // Create new permission record with only Read access
            var newPermission = new UserPagePermission
            {
                UserId = userId,
                PageId = pageId,
                CanRead = true,
                CreatedAt = DateTime.UtcNow
            };
            db.UserPagePermissions.Add(newPermission);
        }

        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Windows-mapped HelpDesk <see cref="User"/> ids (<see cref="UserType.Group"/>) from the current request's claims (not <see cref="DeskGroup"/> ids).
    /// </summary>
    private List<int> GetUserHelpdeskGroupIds() =>
        DeskGroupAccessResolver.GetWindowsMappedGroupUserIdsFromClaims(_httpContextAccessor.HttpContext?.User);

    /// <summary>
    /// Calculate union of permissions (most permissive wins).
    /// </summary>
    private static PagePermissions UnionPermissions(params PagePermissions[] permissions)
    {
        var result = new PagePermissions();

        foreach (var perm in permissions)
        {
            if (perm == null) continue;

            result.CanCreate = result.CanCreate || perm.CanCreate;
            result.CanRead = result.CanRead || perm.CanRead;
            result.CanUpdate = result.CanUpdate || perm.CanUpdate;
            result.CanDelete = result.CanDelete || perm.CanDelete;
        }

        return result;
    }

    /// <summary>
    /// Get effective role considering both user role and group roles (highest wins).
    /// </summary>
    private static async Task<UserRole> GetEffectiveUserRoleAsync(HelpDeskDbContext db, int userId, List<int> groupIds)
    {
        // Get user's role
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Role })
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        var effectiveRole = user?.Role ?? UserRole.User;

        // Check if user is in any groups with higher roles
        if (groupIds.Any())
        {
            var groupRoles = await db.Users.AsNoTracking()
                .Where(u => groupIds.Contains(u.Id) && u.UserType == UserType.Group)
                .Select(u => u.Role)
                .ToListAsync()
                .ConfigureAwait(false);

            // Take the highest role (Admin > User)
            if (groupRoles.Contains(UserRole.Admin))
                effectiveRole = UserRole.Admin;
        }

        return effectiveRole;
    }

    private static async Task<bool> HasGroupAccessToPageAsync(HelpDeskDbContext db, int userId, List<int> windowsGroupUserIds, int pageId)
    {
        var deskGroupIds = await DeskGroupAccessResolver.GetEffectiveDeskGroupIdsAsync(db, userId, windowsGroupUserIds)
            .ConfigureAwait(false);

        return await db.Pages.AsNoTracking()
            .Where(p => p.Id == pageId && p.IsActive)
            .Where(p => p.DeskApplication.ApplicationGroups.Any(ag => deskGroupIds.Contains(ag.DeskGroupId)))
            .AnyAsync()
            .ConfigureAwait(false);
    }
}
