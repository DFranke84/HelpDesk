using HelpDesk.Models;
using HelpDesk.Models.Data;
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
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(10);

    public PagePermissionService(IDbContextFactory<HelpDeskDbContext> factory, IMemoryCache cache)
    {
        _factory = factory;
        _cache = cache;
    }

    public async Task<PagePermissions> GetUserPagePermissionsAsync(int userId, int pageId)
    {
        var cacheKey = $"page_perms_{userId}_{pageId}";
        if (_cache.TryGetValue(cacheKey, out PagePermissions? cachedPermissions) && cachedPermissions != null)
        {
            return cachedPermissions;
        }

        await using var db = await _factory.CreateDbContextAsync().ConfigureAwait(false);

        // Check if user is admin first
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Role })
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (user?.Role == UserRole.Admin)
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

        // Check group access to the page first
        var hasGroupAccess = await HasGroupAccessToPageAsync(db, userId, pageId).ConfigureAwait(false);
        if (!hasGroupAccess)
        {
            var noAccessPermissions = new PagePermissions();
            _cache.Set(cacheKey, noAccessPermissions, _cacheExpiry);
            return noAccessPermissions;
        }

        // Get explicit permissions from database
        var userPermission = await db.UserPagePermissions.AsNoTracking()
            .Where(upp => upp.UserId == userId && upp.PageId == pageId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        var permissions = userPermission != null
            ? new PagePermissions
            {
                CanCreate = userPermission.CanCreate,
                CanRead = userPermission.CanRead,
                CanUpdate = userPermission.CanUpdate,
                CanDelete = userPermission.CanDelete
            }
            : new PagePermissions(); // No explicit permissions = no access (secure default)

        _cache.Set(cacheKey, permissions, _cacheExpiry);
        return permissions;
    }

    public async Task<Dictionary<int, PagePermissions>> GetUserPagePermissionsAsync(int userId, IEnumerable<int> pageIds)
    {
        var result = new Dictionary<int, PagePermissions>();
        var pageIdList = pageIds.ToList();

        await using var db = await _factory.CreateDbContextAsync().ConfigureAwait(false);

        // Check if user is admin first
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Role })
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (user?.Role == UserRole.Admin)
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

        // Get user's group IDs
        var groupIds = await db.UserGroups.AsNoTracking()
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.GroupId)
            .ToListAsync()
            .ConfigureAwait(false);

        // Get pages user has group access to
        var accessiblePages = await db.Pages.AsNoTracking()
            .Where(p => pageIdList.Contains(p.Id) && p.IsActive)
            .Where(p => p.DeskApplication.ApplicationGroups.Any(ag => groupIds.Contains(ag.DeskGroupId)))
            .Select(p => p.Id)
            .ToListAsync()
            .ConfigureAwait(false);

        // Get explicit permissions for accessible pages
        var userPermissions = await db.UserPagePermissions.AsNoTracking()
            .Where(upp => upp.UserId == userId && pageIdList.Contains(upp.PageId))
            .ToListAsync()
            .ConfigureAwait(false);

        var permissionLookup = userPermissions.ToDictionary(upp => upp.PageId);

        foreach (var pageId in pageIdList)
        {
            PagePermissions permissions;
            
            if (!accessiblePages.Contains(pageId))
            {
                // No group access = no permissions
                permissions = new PagePermissions();
            }
            else if (permissionLookup.TryGetValue(pageId, out var userPermission))
            {
                // Has explicit permissions
                permissions = new PagePermissions
                {
                    CanCreate = userPermission.CanCreate,
                    CanRead = userPermission.CanRead,
                    CanUpdate = userPermission.CanUpdate,
                    CanDelete = userPermission.CanDelete
                };
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

    private async Task<bool> HasGroupAccessToPageAsync(HelpDeskDbContext db, int userId, int pageId)
    {
        var groupIds = await db.UserGroups.AsNoTracking()
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.GroupId)
            .ToListAsync()
            .ConfigureAwait(false);

        return await db.Pages.AsNoTracking()
            .Where(p => p.Id == pageId && p.IsActive)
            .Where(p => p.DeskApplication.ApplicationGroups.Any(ag => groupIds.Contains(ag.DeskGroupId)))
            .AnyAsync()
            .ConfigureAwait(false);
    }
}