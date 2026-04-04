using System.Security.Claims;
using HelpDesk.Models;
using HelpDesk.Models.Data;
using HelpDesk.Logging;
using HelpDesk.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace HelpDesk.Web.Middleware;

public sealed class WindowsAutoLoginMiddleware
{
    private readonly RequestDelegate _next;

    public WindowsAutoLoginMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        HelpDeskDbContext db,
        IObservabilityService observability,
        IConfiguration configuration)
    {
        if (context.User.Identity?.AuthenticationType == CookieAuthenticationDefaults.AuthenticationScheme
            && context.User.Identity.IsAuthenticated)
        {
            await _next(context);
            return;
        }

        // After an explicit logout the client receives this cookie; skip Negotiate + Windows sign-in until it expires or forms login clears it.
        if (context.Request.Cookies.ContainsKey(AuthCookieNames.SuppressWindowsAutoLogin))
        {
            await _next(context);
            return;
        }

        var win = await context.AuthenticateAsync(NegotiateDefaults.AuthenticationScheme).ConfigureAwait(false);
        if (!win.Succeeded || win.Principal?.Identity?.Name is not { } winName)
        {
            // Integrated Windows auth is only sent by browsers after a 401 Negotiate challenge.
            // If we don't challenge, the app will always fall back to cookie auth and redirect to /login.
            var challengeEnabled = configuration.GetValue("App:WindowsAutoLoginChallenge", true);
            if (challengeEnabled && ShouldNegotiateChallenge(context))
            {
                await context.ChallengeAsync(NegotiateDefaults.AuthenticationScheme).ConfigureAwait(false);
                return;
            }

            await _next(context);
            return;
        }

        var normalized = NormalizeWindowsName(winName);
        var sam = normalized.sam;
        var upn = normalized.upn;
        var user = await db.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .FirstOrDefaultAsync(u =>
                (u.DomainSamAccountName != null
                 && u.DomainSamAccountName == sam)
                || (u.DomainUserPrincipalName != null
                    && upn != null
                    && u.DomainUserPrincipalName == upn))
            .ConfigureAwait(false);

        if (user is null)
        {
            await _next(context);
            return;
        }

        var claims = CookieAuthClaims.BuildForWindowsUser(user);
        claims.Add(new Claim(AuthClaims.WindowsName, winName));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(60),
                AllowRefresh = true
            }).ConfigureAwait(false);

        using (ScopeContext.PushProperty("domainLogin", winName))
            await observability.AddAuditAsync("Login", user.LoginId, "Windows integrated login", winName, context.RequestAborted).ConfigureAwait(false);

        var redirect = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
        context.Response.Redirect(redirect);
    }

    private static bool ShouldNegotiateChallenge(HttpContext context)
    {
        // Only for normal page navigations (avoid breaking API calls / assets / Blazor endpoints).
        if (!HttpMethods.IsGet(context.Request.Method))
            return false;

        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
            return false;
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            return false;
        if (path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase))
            return false;
        if (path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase))
            return false;
        if (path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase))
            return false;

        // If the browser already sent credentials and they failed, don't loop on challenge.
        if (context.Request.Headers.ContainsKey("Authorization"))
            return false;

        // Only when asking for HTML-ish content.
        var accept = context.Request.Headers.Accept.ToString();
        if (!string.IsNullOrWhiteSpace(accept)
            && !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase)
            && !accept.Contains("*/*", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static (string sam, string? upn) NormalizeWindowsName(string name)
    {
        if (name.Contains('@', StringComparison.Ordinal))
            return (name, name);
        return (name, null);
    }
}
