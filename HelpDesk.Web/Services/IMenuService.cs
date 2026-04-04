using HelpDesk.Models;

namespace HelpDesk.Web.Services;

public interface IMenuService
{
    Task<List<DeskApplication>> GetAccessibleApplicationsAsync(int userId, bool isAdmin, CancellationToken ct = default);

    Task<List<DeskPage>> GetAccessiblePagesAsync(int appId, int userId, bool isAdmin, CancellationToken ct = default);

    /// <summary>Returns all accessible pages grouped by application ID in a single query.</summary>
    Task<Dictionary<int, List<DeskPage>>> GetAccessiblePagesByAppsAsync(IEnumerable<int> appIds, int userId, bool isAdmin, CancellationToken ct = default);

    /// <summary>
    /// If <paramref name="path"/> matches an active catalog <see cref="DeskPage.Route"/>, returns whether the user may open it
    /// (same rules as the navigation menu). Paths that are not catalog routes return true.
    /// </summary>
    Task<bool> CanAccessCatalogRoutePathAsync(string path, int userId, bool isAdmin, CancellationToken ct = default);
}
