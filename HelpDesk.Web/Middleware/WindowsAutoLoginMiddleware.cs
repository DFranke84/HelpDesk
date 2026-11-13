using System.Security.Claims;
using System.Security.Principal;
using HelpDesk.Models;
using HelpDesk.Models.Data;
using HelpDesk.Logging;
using HelpDesk.Web.Models;
using HelpDesk.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace HelpDesk.Web.Middleware;

/// <summary>
/// Sign in Help Desk users automatically when integrated Windows authentication succeeds.
/// </summary>
public sealed class WindowsAutoLoginMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Create Windows auto-login middleware.
    /// </summary>
    /// <param name="next">Next middleware <see cref="RequestDelegate"/>.</param>
    public WindowsAutoLoginMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Attempt Windows integrated sign-in or forward the request unchanged.
    /// </summary>
    /// <param name="context">Current HTTP context <see cref="HttpContext"/>.</param>
    /// <param name="db">Help Desk database <see cref="HelpDeskDbContext"/>.</param>
    /// <param name="log">Event log manager <see cref="EventLogManager"/>.</param>
    /// <param name="configuration">Application configuration <see cref="IConfiguration"/>.</param>
    /// <param name="windowsGroupService">Windows group resolution service <see cref="IWindowsGroupService"/>.</param>
    /// <returns>A task that completes when the pipeline finishes.</returns>
    public async Task InvokeAsync(
        HttpContext context,
        HelpDeskDbContext db,
        EventLogManager log,
        IConfiguration configuration,
        IWindowsGroupService windowsGroupService)
    {
        if (context.User.Identity?.AuthenticationType == CookieAuthenticationDefaults.AuthenticationScheme
            && context.User.Identity.IsAuthenticated)
        {
            await _next(context);
            return;
        }

        // Under IIS a Windows-authenticated connection arrives with HttpContext.User already set to the Windows
        // account. Only the Help Desk cookie means "signed in": without it the request is anonymous until the
        // sign-in below succeeds, otherwise any domain user would pass [Authorize] without a Help Desk account.
        if (context.User.Identity?.IsAuthenticated == true)
            context.User = new ClaimsPrincipal(new ClaimsIdentity());

        // Automatic sign-in ends in a redirect back to the same address, which only makes sense for page loads.
        // A form post (for example the sign-in form itself) must be handled as posted.
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
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
        // Match person accounts by Domain SAM / UPN (case-insensitive).
        var samUpper = sam.ToUpperInvariant();
        var upnUpper = upn?.ToUpperInvariant();
        var ct = context.RequestAborted;

        User? user = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.UserType == UserType.User)
            .FirstOrDefaultAsync(u =>
                (u.DomainSamAccountName != null
                 && u.DomainSamAccountName.ToUpper() == samUpper)
                || (u.DomainUserPrincipalName != null
                    && upnUpper != null
                    && u.DomainUserPrincipalName.ToUpper() == upnUpper), ct)
            .ConfigureAwait(false);

        var enableGroupIntegration = configuration.GetValue("App:EnableWindowsGroupIntegration", true);

        List<WindowsGroupInfo>? cachedWindowsGroups = null;
        List<User>? cachedHelpdeskGroupEntities = null;

        // No person row: sign in as the HelpDesk group User row(s) already defined (UserType.Group) — use their
        // User page permissions, catalog UserGroups links, and roles. No new Users row is created.
        if (user is null && enableGroupIntegration
            && win.Principal?.Identity is System.Security.Principal.WindowsIdentity wiForGroups)
        {
            cachedWindowsGroups = await windowsGroupService.GetUserWindowsGroupsMatchingHelpdeskAsync(wiForGroups).ConfigureAwait(false);
            cachedHelpdeskGroupEntities = await windowsGroupService.GetHelpdeskGroupEntitiesAsync(cachedWindowsGroups)
                .ConfigureAwait(false);
            if (cachedHelpdeskGroupEntities.Count > 0)
            {
                var ranked = new List<(User Entity, int Access)>();
                foreach (var ge in cachedHelpdeskGroupEntities)
                {
                    var accessLevel = await windowsGroupService.CalculateGroupAccessLevelAsync(ge.Id).ConfigureAwait(false);
                    ranked.Add((ge, accessLevel));
                }

                // Role claim comes from the primary row: prefer Admin over User, then highest access level.
                var primary = ranked
                    .OrderByDescending(x => x.Entity.Role)
                    .ThenByDescending(x => x.Access)
                    .First().Entity;
                user = primary;
                log.LogInformation(
                    "Windows login for {Win} bound to HelpDesk group {GroupLoginId} (no person row); {TotalGroups} group(s) matched.",
                    winName, primary.LoginId, cachedHelpdeskGroupEntities.Count);
            }
        }

        if (user is null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
            await _next(context).ConfigureAwait(false);
            return;
        }

        List<Claim> claims;

        if (enableGroupIntegration && win.Principal?.Identity is System.Security.Principal.WindowsIdentity windowsIdentity)
        {
            try
            {
                var windowsGroups = cachedWindowsGroups
                    ?? await windowsGroupService.GetUserWindowsGroupsMatchingHelpdeskAsync(windowsIdentity).ConfigureAwait(false);
                log.LogDebug("Found {Count} HelpDesk-matched Windows groups for user {User}", windowsGroups.Count, winName);

                var helpdeskGroupEntities = cachedHelpdeskGroupEntities
                    ?? await windowsGroupService.GetHelpdeskGroupEntitiesAsync(windowsGroups).ConfigureAwait(false);

                var userGroups = new List<UserGroupInfo>();
                foreach (var groupEntity in helpdeskGroupEntities)
                {
                    var accessLevel = await windowsGroupService.CalculateGroupAccessLevelAsync(groupEntity.Id)
                        .ConfigureAwait(false);
                    userGroups.Add(new UserGroupInfo
                    {
                        Id = groupEntity.Id,
                        Name = groupEntity.LoginId,
                        AccessLevel = accessLevel,
                        Role = groupEntity.Role
                    });
                }

                claims = CookieAuthClaims.BuildForWindowsUser(user, userGroups);
                log.LogInformation("Assigned {Count} HelpDesk groups to session for {LoginId}", userGroups.Count, user.LoginId);
            }
            catch (Exception ex)
            {
                log.LogWarning("Failed to resolve Windows groups for user {User}: {Error}", winName, ex.Message);
                claims = CookieAuthClaims.BuildForWindowsUser(user);
            }
        }
        else
        {
            claims = CookieAuthClaims.BuildForWindowsUser(user);
        }

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
        using (log.BeginContext(loginId: user.LoginId, activityCategory: "Login"))
            log.LogInformation("{ActionType} {Message} {Details}", "Login", "Windows integrated login", winName);

        // A path such as "//other-site/x" would be read by the browser as another site: collapse leading slashes.
        var path = (context.Request.PathBase + context.Request.Path).ToString();
        var redirect = "/" + path.TrimStart('/', '\\') + context.Request.QueryString;
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
