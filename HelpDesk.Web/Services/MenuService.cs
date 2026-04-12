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
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Create menu service with a database factory.
    /// </summary>
    /// <param name="factory">Context factory <see cref="IDbContextFactory{TContext}"/> for <see cref="HelpDeskDbContext"/>.</param>
    /// <param name="httpContextAccessor">Used to read Windows-mapped HelpDesk group ids from claims (must match <see cref="PagePermissionService"/>).</param>
    public MenuService(IDbContextFactory<HelpDeskDbContext> factory, IHttpContextAccessor httpContextAccessor)
    {
        _factory = factory;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Load active applications visible to the user.
    /// </summary>
    /// <param name="userId">Current user id.</param>
    /// <param name="isAdmin">Whether the user is an administrator.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Applications ordered by display order, then name.</returns>
    public async Task<List<DeskApplication>> GetAccessibleApplicationsAsync(int userId, bool isAdmin, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        if (isAdmin)
        {
            return await db.Applications.AsNoTracking()
                .Where(a => a.IsActive)
                .OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }

        var groupIds = await GetEffectiveDeskGroupIdsAsync(db, userId, ct).ConfigureAwait(false);

        return await db.Applications.AsNoTracking()
            .Where(a => a.IsActive && a.ApplicationGroups.Any(ag => groupIds.Contains(ag.DeskGroupId)))
            .OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Load active pages within an application for the user.
    /// </summary>
    /// <param name="appId">Desk application id.</param>
    /// <param name="userId">Current user id.</param>
    /// <param name="isAdmin">Whether the user is an administrator.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Pages ordered for display.</returns>
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

        var groupIds = await GetEffectiveDeskGroupIdsAsync(db, userId, ct).ConfigureAwait(false);

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

    /// <summary>
    /// Load accessible pages for multiple applications in one round trip.
    /// </summary>
    /// <param name="appIds">Application ids to include.</param>
    /// <param name="userId">Current user id.</param>
    /// <param name="isAdmin">Whether the user is an administrator.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Pages grouped by application id.</returns>
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
            var groupIds = await GetEffectiveDeskGroupIdsAsync(db, userId, ct).ConfigureAwait(false);

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

    /// <summary>
    /// Determine whether the user may navigate to a catalog route path.
    /// </summary>
    /// <param name="path">Relative or absolute catalog path.</param>
    /// <param name="userId">Current user id.</param>
    /// <param name="isAdmin">Whether the user is an administrator.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when access is allowed or the path is not a catalog route.</returns>
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

    /// <summary>
    /// Normalize a catalog path for comparison with stored routes.
    /// </summary>
    /// <param name="relativeOrAbsolutePath">Path from the browser or configuration.</param>
    /// <returns>Leading-slash path without query string or trailing slash.</returns>
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

    /// <summary>
    /// Desk catalog group ids: <c>UserGroups</c> for the person plus <c>UserGroups</c> for each Windows-mapped
    /// group user id in claims (see <see cref="DeskGroupAccessResolver"/>).
    /// </summary>
    private async Task<List<int>> GetEffectiveDeskGroupIdsAsync(HelpDeskDbContext db, int userId, CancellationToken ct)
    {
        var windowsGroupUserIds = DeskGroupAccessResolver.GetWindowsMappedGroupUserIdsFromClaims(
            _httpContextAccessor.HttpContext?.User);
        return await DeskGroupAccessResolver.GetEffectiveDeskGroupIdsAsync(db, userId, windowsGroupUserIds, ct)
            .ConfigureAwait(false);
    }
}
