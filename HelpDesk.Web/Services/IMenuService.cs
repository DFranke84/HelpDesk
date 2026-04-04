using HelpDesk.Models;

namespace HelpDesk.Web.Services;

/// <summary>Provides menu data (applications and pages) filtered by the current user's group memberships.</summary>
public interface IMenuService
{
    /// <summary>
    /// Return active applications the user may see (all for admins; group-linked for others).
    /// </summary>
    /// <param name="userId">Current user id.</param>
    /// <param name="isAdmin">Whether the user is an administrator.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Applications ordered by name.</returns>
    Task<List<DeskApplication>> GetAccessibleApplicationsAsync(int userId, bool isAdmin, CancellationToken ct = default);

    /// <summary>
    /// Return active pages within an application that the user may access.
    /// </summary>
    /// <param name="appId">Desk application id.</param>
    /// <param name="userId">Current user id.</param>
    /// <param name="isAdmin">Whether the user is an administrator.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Pages ordered for display.</returns>
    Task<List<DeskPage>> GetAccessiblePagesAsync(int appId, int userId, bool isAdmin, CancellationToken ct = default);

    /// <summary>
    /// Return accessible pages grouped by application id in one query.
    /// </summary>
    /// <param name="appIds">Application ids to include.</param>
    /// <param name="userId">Current user id.</param>
    /// <param name="isAdmin">Whether the user is an administrator.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Pages grouped by application id.</returns>
    Task<Dictionary<int, List<DeskPage>>> GetAccessiblePagesByAppsAsync(IEnumerable<int> appIds, int userId, bool isAdmin, CancellationToken ct = default);

    /// <summary>
    /// Return whether the user may open a catalog route path (non-catalog paths return true).
    /// </summary>
    /// <param name="path">Relative or absolute catalog path.</param>
    /// <param name="userId">Current user id.</param>
    /// <param name="isAdmin">Whether the user is an administrator.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when access is allowed or the path is not a catalog route.</returns>
    Task<bool> CanAccessCatalogRoutePathAsync(string path, int userId, bool isAdmin, CancellationToken ct = default);
}
