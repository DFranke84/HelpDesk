using System.Security.Claims;
using HelpDesk.Models;
using HelpDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HelpDesk.Web.Authorization;

/// <summary>
/// Custom authorization attribute that checks CRUD permissions for specific pages.
/// Integrates with the PagePermissionService to enforce page-level access control.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class PageCrudAuthorizeAttribute : AuthorizeAttribute, IAsyncAuthorizationFilter
{
    /// <summary>
    /// The CRUD action required for this endpoint.
    /// </summary>
    public CrudAction RequiredAction { get; set; }
    
    /// <summary>
    /// The name of the route/query parameter that contains the page ID.
    /// Defaults to "pageId".
    /// </summary>
    public string PageIdParameter { get; set; } = "pageId";

    /// <summary>
    /// Initializes a new instance of the PageCrudAuthorizeAttribute.
    /// </summary>
    /// <param name="requiredAction">The CRUD action required for this endpoint.</param>
    public PageCrudAuthorizeAttribute(CrudAction requiredAction)
    {
        RequiredAction = requiredAction;
    }

    /// <summary>
    /// Called when authorization is required.
    /// </summary>
    /// <param name="context">Authorization filter context.</param>
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // Check if user is authenticated
        var user = context.HttpContext.User;
        if (!user.Identity?.IsAuthenticated ?? true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // Check if user is admin (bypass permission checks)
        if (user.IsInRole(nameof(UserRole.Admin)))
        {
            return; // Admin has full access
        }

        // Get user ID
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim?.Value, out var userId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // Get page ID from route/query parameters - make it optional for backward compatibility
        if (!TryGetPageId(context, out var pageId))
        {
            // If no pageId provided, allow the request (for backward compatibility)
            // In a strict implementation, you could return BadRequest instead
            return;
        }

        // Get the permission service from DI
        var permissionService = context.HttpContext.RequestServices.GetService<IPagePermissionService>();
        if (permissionService == null)
        {
            context.Result = new StatusCodeResult(500);
            return;
        }

        try
        {
            var hasPermission = await permissionService.CanUserPerformActionAsync(userId, pageId, RequiredAction);
            if (!hasPermission)
            {
                context.Result = new ForbidResult();
            }
        }
        catch
        {
            context.Result = new StatusCodeResult(500);
        }
    }

    /// <summary>
    /// Attempts to extract the page ID from route or query parameters.
    /// </summary>
    /// <param name="context">Authorization filter context.</param>
    /// <param name="pageId">The extracted page ID.</param>
    /// <returns>True if page ID was successfully extracted.</returns>
    private bool TryGetPageId(AuthorizationFilterContext context, out int pageId)
    {
        pageId = 0;

        // Try route values first
        if (context.RouteData.Values.TryGetValue(PageIdParameter, out var routeValue))
        {
            if (int.TryParse(routeValue?.ToString(), out pageId))
                return true;
        }

        // Try query parameters
        if (context.HttpContext.Request.Query.TryGetValue(PageIdParameter, out var queryValue))
        {
            if (int.TryParse(queryValue.FirstOrDefault(), out pageId))
                return true;
        }

        // Try form data for POST requests
        if (context.HttpContext.Request.HasFormContentType)
        {
            if (context.HttpContext.Request.Form.TryGetValue(PageIdParameter, out var formValue))
            {
                if (int.TryParse(formValue.FirstOrDefault(), out pageId))
                    return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Convenience attributes for specific CRUD actions.
/// </summary>
public class PageCreateAuthorizeAttribute : PageCrudAuthorizeAttribute
{
    public PageCreateAuthorizeAttribute() : base(CrudAction.Create) { }
}

public class PageReadAuthorizeAttribute : PageCrudAuthorizeAttribute
{
    public PageReadAuthorizeAttribute() : base(CrudAction.Read) { }
}

public class PageUpdateAuthorizeAttribute : PageCrudAuthorizeAttribute
{
    public PageUpdateAuthorizeAttribute() : base(CrudAction.Update) { }
}

public class PageDeleteAuthorizeAttribute : PageCrudAuthorizeAttribute
{
    public PageDeleteAuthorizeAttribute() : base(CrudAction.Delete) { }
}