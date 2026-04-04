using HelpDesk.Web.Services;

namespace HelpDesk.Web.Middleware;

/// <summary>Forces users with <see cref="AuthClaims.MustChangePassword"/> to the first-login password page until they set a new password.</summary>
public sealed class MustChangePasswordMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Create must-change-password middleware.
    /// </summary>
    /// <param name="next">Next middleware <see cref="RequestDelegate"/>.</param>
    public MustChangePasswordMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Redirect authenticated users with must-change-password claim to the first-login page.
    /// </summary>
    /// <param name="context">Current HTTP context <see cref="HttpContext"/>.</param>
    /// <returns>A task that completes when the response is sent or the next delegate runs.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return _next(context);

        if (!context.User.HasClaim(AuthClaims.MustChangePassword, "true"))
            return _next(context);

        var path = context.Request.Path.Value ?? "";
        if (IsExempt(path))
            return _next(context);

        var returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
        context.Response.Redirect("/account/first-login-password?returnUrl=" + returnUrl);
        return Task.CompletedTask;
    }

    private static bool IsExempt(string path)
    {
        if (path.StartsWith("/account/first-login-password", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWith("/api/account/set-password", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWith("/api/account/logout", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWith("/api/account/session/ping", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }
}
