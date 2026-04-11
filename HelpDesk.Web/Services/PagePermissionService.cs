using System.Security.Claims;
using System.Text.Json;
using HelpDesk.Models;
using HelpDesk.Models.Data;
using HelpDesk.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HelpDesk.Web.Services;

/// <summary>
/// Service for managing page-level CRUD permissions with admin bypass and group access integration.
/// </summary>
public class PagePermissionService : IPagePermissionService
{
    private readonly IDbContextFactory<HelpDeskDbContext> _factory;
    private readonly IMemoryCache _cache;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(10);

    public PagePermissionService(
        IDbContextFactory<HelpDeskDbContext> factory, 
        IMemoryCache cache,
        IHttpContextAccessor httpContextAccessor)
    {
        _factory = factory;
        _cache = cache;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<PagePermissions> GetUserPagePermissionsAsync(int userId, int pageId)
    {
        var cacheKey = $"page_perms_{userId}_{pageId}";
        if (_cache.TryGetValue(cacheKey, out PagePermissions? cachedPermissions) && cachedPermissions != null)
        {
            return cachedPermissions;
        }

        await using var db = await _factory.CreateDbContextAsync().ConfigureAwait(false);

        // Check effective role (user + groups) - Admin gets full access
        var effectiveRole = await GetEffectiveUserRoleAsync(db, userId);
        if (effectiveRole == UserRole.Admin)
        {
            var adminPermissions = new PagePermissions
            {
                CanCreate = true,
                CanRead = true,
                CanUpdate = true,
                CanDelete = true
            };
            _cache.Set(cacheKey, adminPermissions, _cacheExpiry);
            return adminPermissions;
        }

        // Check if user OR any of their groups has access to this page
        var hasGroupAccess = await HasGroupAccessToPageAsync(db, userId, pageId).ConfigureAwait(false);
        if (!hasGroupAccess)
        {
            var noAccessPermissions = new PagePermissions();
            _cache.Set(cacheKey, noAccessPermissions, _cacheExpiry);
            return noAccessPermissions;
        }

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
        var groupIds = GetUserHelpdeskGroupIds();
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
        var effectivePermissions = UnionPermissions(allPermissions.ToArray());

        _cache.Set(cacheKey, effectivePermissions, _cacheExpiry);
        return effectivePermissions;
    }

    public async Task<Dictionary<int, PagePermissions>> GetUserPagePermissionsAsync(int userId, IEnumerable<int> pageIds)
    {
        var result = new Dictionary<int, PagePermissions>();
        var pageIdList = pageIds.ToList();

        await using var db = await _factory.CreateDbContextAsync().ConfigureAwait(false);

        // Check effective role (user + groups) - Admin gets full access
        var effectiveRole = await GetEffectiveUserRoleAsync(db, userId);
        if (effectiveRole == UserRole.Admin)
        {
            var adminPermissions = new PagePermissions
            {
                CanCreate = true,
                CanRead = true,
                CanUpdate = true,
                CanDelete = true
            };
            
            foreach (var pageId in pageIdList)
            {
                result[pageId] = adminPermissions;
                var cacheKey = $"page_perms_{userId}_{pageId}";
                _cache.Set(cacheKey, adminPermissions, _cacheExpiry);
            }
            return result;
        }

        // Get user's traditional group IDs and Windows group IDs
        var userGroupIds = await db.UserGroups.AsNoTracking()
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.GroupId)
            .ToListAsync()
            .ConfigureAwait(false);

        var windowsGroupIds = GetUserHelpdeskGroupIds();
        var allGroupIds = userGroupIds.Union(windowsGroupIds).ToList();

        // Get pages user has access to (via any group)
        var accessiblePages = await db.Pages.AsNoTracking()
            .Where(p => pageIdList.Contains(p.Id) && p.IsActive)
            .Where(p => p.DeskApplication.ApplicationGroups.Any(ag => allGroupIds.Contains(ag.DeskGroupId)))
            .Select(p => p.Id)
            .ToListAsync()
            .ConfigureAwait(false);

        // Get user's direct permissions
        var userPermissions = await db.UserPagePermissions.AsNoTracking()
            .Where(upp => upp.UserId == userId && pageIdList.Contains(upp.PageId))
            .ToListAsync()
            .ConfigureAwait(false);

        // Get group permissions (both traditional and Windows groups)
        var allEntityIds = new List<int> { userId };
        allEntityIds.AddRange(windowsGroupIds);

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
            
            // Cache individual permissions
            var cacheKey = $"page_perms_{userId}_{pageId}";
            _cache.Set(cacheKey, permissions, _cacheExpiry);
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

        // Invalidate cache
        var cacheKey = $"page_perms_{userId}_{pageId}";
        _cache.Remove(cacheKey);
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

        // Invalidate cache
        var cacheKey = $"page_perms_{userId}_{pageId}";
        _cache.Remove(cacheKey);
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

        // Invalidate cache
        var cacheKey = $"page_perms_{userId}_{pageId}";
        _cache.Remove(cacheKey);
    }

    /// <summary>
    /// Get user's HelpDesk group IDs from claims (Windows groups that have been mapped to HelpDesk User entities).
    /// </summary>
    private List<int> GetUserHelpdeskGroupIds()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) return new List<int>();

        var groupsClaim = user.FindFirst(AuthClaims.HelpdeskGroups)?.Value;
        if (string.IsNullOrEmpty(groupsClaim)) return new List<int>();

        try
        {
            var groupData = JsonSerializer.Deserialize<List<UserGroupClaim>>(groupsClaim);
            return groupData?.Select(g => g.Id).ToList() ?? new List<int>();
        }
        catch
        {
            return new List<int>();
        }
    }

    /// <summary>
    /// Calculate union of permissions (most permissive wins).
    /// </summary>
    private PagePermissions UnionPermissions(params PagePermissions[] permissions)
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
    private async Task<UserRole> GetEffectiveUserRoleAsync(HelpDeskDbContext db, int userId)
    {
        // Get user's role
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Role })
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        var effectiveRole = user?.Role ?? UserRole.User;

        // Check if user is in any groups with higher roles
        var groupIds = GetUserHelpdeskGroupIds();
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

    private async Task<bool> HasGroupAccessToPageAsync(HelpDeskDbContext db, int userId, int pageId)
    {
        // Get traditional UserGroup relationships
        var userGroupIds = await db.UserGroups.AsNoTracking()
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.GroupId)
            .ToListAsync()
            .ConfigureAwait(false);

        // Get Windows group IDs from claims
        var windowsGroupIds = GetUserHelpdeskGroupIds();
        
        // Combine all group IDs
        var allGroupIds = userGroupIds.Union(windowsGroupIds).ToList();

        return await db.Pages.AsNoTracking()
            .Where(p => p.Id == pageId && p.IsActive)
            .Where(p => p.DeskApplication.ApplicationGroups.Any(ag => allGroupIds.Contains(ag.DeskGroupId)))
            .AnyAsync()
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Helper class for deserializing group claims.
    /// </summary>
    private class UserGroupClaim
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int AccessLevel { get; set; }
    }
}