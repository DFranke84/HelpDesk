using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace HelpDesk.Web.Middleware;

/// <summary>
/// With a <see cref="AuthorizationOptions.FallbackPolicy"/> that requires an authenticated user,
/// endpoint-routed Blazor assets under <c>/_framework</c> (and related paths) would otherwise be
/// challenged and return HTML (e.g. login redirect), which breaks script loads with a syntax error.
/// </summary>
public sealed class BlazorInfrastructureAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    /// <inheritdoc />
    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged && IsBlazorInfrastructurePath(context.Request.Path))
            return _defaultHandler.HandleAsync(next, context, policy, PolicyAuthorizationResult.Success());

        return _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }

    private static bool IsBlazorInfrastructurePath(PathString path) =>
        path.StartsWithSegments("/_framework", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/_content", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/_blazor", StringComparison.OrdinalIgnoreCase);
}

