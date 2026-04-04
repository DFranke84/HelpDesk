using HelpDesk.Models;

namespace HelpDesk.Web.Services;

/// <summary>Provides menu data (applications and pages) filtered by the current user's group memberships.</summary>
public interface IMenuService
{
    /// <summary>Returns the active applications the user may see (all for admins; group-linked for regular users).</summary>
    Task<List<DeskApplication>> GetAccessibleApplicationsAsync(int userId, bool isAdmin, CancellationToken ct = default);

    /// <summary>Returns the active pages within <paramref name="appId"/> the user may access.</summary>
    Task<List<DeskPage>> GetAccessiblePagesAsync(int appId, int userId, bool isAdmin, CancellationToken ct = default);

    /// <summary>Returns all accessible pages grouped by application ID in a single query.</summary>
    Task<Dictionary<int, List<DeskPage>>> GetAccessiblePagesByAppsAsync(IEnumerable<int> appIds, int userId, bool isAdmin, CancellationToken ct = default);

    /// <summary>
    /// If <paramref name="path"/> matches an active catalog <see cref="DeskPage.Route"/>, returns whether the user may open it
    /// (same rules as the navigation menu). Paths that are not catalog routes return true.
    /// </summary>
    Task<bool> CanAccessCatalogRoutePathAsync(string path, int userId, bool isAdmin, CancellationToken ct = default);
}
