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

    public async Task<bool> CanAccessCatalogRoutePathAsync(string path, int userId, bool isAdmin, CancellationToken ct = default)
    {
        var normalized = NormalizeCatalogPath(path);
        if (normalized.Length == 0)
            return true;

        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var candidates = await db.Pages.AsNoTracking()
            .Where(p => p.IsActive && p.Route != null && p.Route != "")
            .Select(p => new { p.Id, p.DeskApplicationId, p.Route })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var page = candidates.FirstOrDefault(p =>
            string.Equals(p.Route.Trim(), normalized, StringComparison.Ordinal));
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
