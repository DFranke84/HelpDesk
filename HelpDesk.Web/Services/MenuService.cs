using HelpDesk.Models;
using HelpDesk.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Web.Services;

/// <summary>
/// Menu data is loaded via <see cref="IDbContextFactory{TContext}"/> so the sidebar can refresh on navigation
/// without sharing the scoped <see cref="HelpDeskDbContext"/> with the active page (avoids concurrent DbContext use).
/// </summary>
public class MenuService : IMenuService
{
    private readonly IDbContextFactory<HelpDeskDbContext> _factory;

    public MenuService(IDbContextFactory<HelpDeskDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<List<DeskApplication>> GetAccessibleApplicationsAsync(int userId, bool isAdmin, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        if (isAdmin)
        {
            return await db.Applications.AsNoTracking()
                .Where(a => a.IsActive)
                .OrderBy(a => a.Name)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }

        var groupIds = await db.UserGroups.AsNoTracking()
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.GroupId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return await db.Applications.AsNoTracking()
            .Where(a => a.IsActive && a.ApplicationGroups.Any(ag => groupIds.Contains(ag.DeskGroupId)))
            .OrderBy(a => a.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<DeskPage>> GetAccessiblePagesAsync(int appId, int userId, bool isAdmin, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        if (isAdmin)
        {
            return await db.Pages.AsNoTracking()
                .Where(p => p.DeskApplicationId == appId && p.IsActive)
                .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }

        var groupIds = await db.UserGroups.AsNoTracking()
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.GroupId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var canAccessApp = await db.DeskApplicationGroups.AsNoTracking()
            .AnyAsync(ag => ag.DeskApplicationId == appId && groupIds.Contains(ag.DeskGroupId), ct)
            .ConfigureAwait(false);

        if (!canAccessApp)
            return new List<DeskPage>();

        return await db.Pages.AsNoTracking()
            .Where(p => p.DeskApplicationId == appId && p.IsActive)
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Dictionary<int, List<DeskPage>>> GetAccessiblePagesByAppsAsync(IEnumerable<int> appIds, int userId, bool isAdmin, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var ids = appIds.ToList();

        IQueryable<DeskPage> query;
        if (isAdmin)
        {
            query = db.Pages.AsNoTracking()
                .Where(p => ids.Contains(p.DeskApplicationId) && p.IsActive);
        }
        else
        {
            var groupIds = await db.UserGroups.AsNoTracking()
                .Where(ug => ug.UserId == userId)
                .Select(ug => ug.GroupId)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var accessibleAppIds = await db.DeskApplicationGroups.AsNoTracking()
                .Where(ag => ids.Contains(ag.DeskApplicationId) && groupIds.Contains(ag.DeskGroupId))
                .Select(ag => ag.DeskApplicationId)
                .Distinct()
                .ToListAsync(ct)
                .ConfigureAwait(false);

            query = db.Pages.AsNoTracking()
                .Where(p => accessibleAppIds.Contains(p.DeskApplicationId) && p.IsActive);
        }

        var pages = await query
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return pages.GroupBy(p => p.DeskApplicationId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    public async Task<bool> CanAccessCatalogRoutePathAsync(string path, int userId, bool isAdmin, CancellationToken ct = default)
    {
        var normalized = NormalizeCatalogPath(path);
        if (normalized.Length == 0)
            return true;

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var page = await db.Pages.AsNoTracking()
            .Where(p => p.IsActive && p.Route == normalized)
            .Select(p => new { p.Id, p.DeskApplicationId })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (page is null)
            return true;

        var allowed = await GetAccessiblePagesAsync(page.DeskApplicationId, userId, isAdmin, ct).ConfigureAwait(false);
        return allowed.Exists(p => p.Id == page.Id);
    }

    internal static string NormalizeCatalogPath(string relativeOrAbsolutePath)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath))
            return "";

        var p = relativeOrAbsolutePath.Trim();
        if (p.Length == 0)
            return "";

        var q = p.IndexOf('?');
        if (q >= 0)
            p = p[..q];

        if (!p.StartsWith('/'))
            p = "/" + p;

        if (p.Length > 1)
            p = p.TrimEnd('/');

        return p;
    }
}
