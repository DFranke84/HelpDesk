using System.Security.Claims;
using HelpDesk.Logging;
using HelpDesk.Web.Services;

namespace HelpDesk.Web.Middleware;

/// <summary>Adds correlation/login scope properties for NLog.</summary>
public sealed class LoggingScopeMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Create logging scope middleware.
    /// </summary>
    /// <param name="next">Next middleware <see cref="RequestDelegate"/>.</param>
    public LoggingScopeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Begin correlation scope and forward the request.
    /// </summary>
    /// <param name="context">Current HTTP context <see cref="HttpContext"/>.</param>
    /// <param name="correlation">Correlation scope <see cref="ICorrelationContext"/>.</param>
    /// <param name="log">NLog bridge <see cref="EventLogManager"/>.</param>
    /// <returns>A task that completes when the pipeline finishes.</returns>
    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlation, EventLogManager log)
    {
        var corr = correlation.BeginNew();
        var loginId = context.User.FindFirstValue(AuthClaims.LoginId);

        using var _ = log.BeginContext(correlationId: corr, loginId: loginId);
        await _next(context).ConfigureAwait(false);
    }
}

