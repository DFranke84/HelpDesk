namespace HelpDesk.Web.Services;

/// <summary>
/// Service for managing page-level CRUD permissions.
/// </summary>
public interface IPagePermissionService
{
    /// <summary>
    /// Get CRUD permissions for a specific user and page.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="pageId">Page ID</param>
    /// <returns>Page permissions for the user</returns>
    Task<PagePermissions> GetUserPagePermissionsAsync(int userId, int pageId);
    
    /// <summary>
    /// Get CRUD permissions for a specific user across multiple pages.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="pageIds">Collection of page IDs</param>
    /// <returns>Dictionary mapping page ID to permissions</returns>
    Task<Dictionary<int, PagePermissions>> GetUserPagePermissionsAsync(int userId, IEnumerable<int> pageIds);
    
    /// <summary>
    /// Set CRUD permissions for a specific user and page.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="pageId">Page ID</param>
    /// <param name="permissions">Permissions to set</param>
    Task SetUserPagePermissionsAsync(int userId, int pageId, PagePermissions permissions);
    
    /// <summary>
    /// Check if a user can perform a specific action on a page.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="pageId">Page ID</param>
    /// <param name="action">CRUD action to check</param>
    /// <returns>True if user can perform the action</returns>
    Task<bool> CanUserPerformActionAsync(int userId, int pageId, CrudAction action);
    
    /// <summary>
    /// Remove all permissions for a user on a specific page.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="pageId">Page ID</param>
    Task RemoveUserPagePermissionsAsync(int userId, int pageId);
    
    /// <summary>
    /// Set read permission for a user on a specific page.
    /// Enforces permission hierarchy rules.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="pageId">Page ID</param>
    /// <param name="canRead">Whether user can read</param>
    Task SetUserReadPermissionAsync(int userId, int pageId, bool canRead);
}

/// <summary>
/// Represents CRUD permissions for a page.
/// </summary>
public class PagePermissions
{
    public bool CanCreate { get; set; }
    public bool CanRead { get; set; }
    public bool CanUpdate { get; set; }
    public bool CanDelete { get; set; }
    
    /// <summary>
    /// Apply permission hierarchy rules:
    /// - Removing Read access removes all other permissions first
    /// - Then, Create/Update/Delete operations automatically grant Read access
    /// </summary>
    public void ApplyPermissionHierarchy()
    {
        // FIRST: If Read is removed, all dependent permissions are removed
        if (!CanRead)
        {
            CanCreate = false;
            CanUpdate = false;
            CanDelete = false;
        }
        
        // SECOND: If any CRUD operation is granted, Read must be granted
        // (This handles the case where someone tries to grant Create/Update/Delete without Read)
        if (CanCreate || CanUpdate || CanDelete)
            CanRead = true;
    }
}

/// <summary>
/// CRUD actions that can be performed on a page.
/// </summary>
public enum CrudAction
{
    Create,
    Read,
    Update,
    Delete
}