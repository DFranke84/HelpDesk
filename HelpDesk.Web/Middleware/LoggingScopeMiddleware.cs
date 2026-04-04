using System.Security.Claims;
using HelpDesk.Logging;
using HelpDesk.Web.Services;

namespace HelpDesk.Web.Middleware;

/// <summary>Adds correlation/login scope properties for NLog.</summary>
public sealed class LoggingScopeMiddleware
{
    private readonly RequestDelegate _next;

    public LoggingScopeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlation, EventLogManager log)
    {
        var corr = correlation.BeginNew();
        var loginId = context.User.FindFirstValue(AuthClaims.LoginId);

        using var _ = log.BeginContext(correlationId: corr, loginId: loginId);
        await _next(context).ConfigureAwait(false);
    }
}

