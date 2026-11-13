using System.Security.Claims;
using HelpDesk.Models;
using HelpDesk.Models.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HelpDesk.Web.Services;

/// <summary>
/// Remembers sessions that were ended by logout so a copy of the old cookie cannot be replayed.
/// Held in memory: an application restart forgets the list (cookies then live at most their remaining lifetime).
/// </summary>
public sealed class SessionRevocationStore
{
    private readonly IMemoryCache _cache;

    /// <summary>
    /// Create session revocation store.
    /// </summary>
    /// <param name="cache">Shared memory cache <see cref="IMemoryCache"/>.</param>
    public SessionRevocationStore(IMemoryCache cache) => _cache = cache;

    /// <summary>Mark a session id as ended for at least as long as its cookie could still be valid.</summary>
    public void Revoke(string sessionId, TimeSpan keepFor) => _cache.Set(Key(sessionId), true, keepFor);

    /// <summary>True when the session was ended by logout.</summary>
    public bool IsRevoked(string sessionId) => _cache.TryGetValue(Key(sessionId), out _);

    private static string Key(string sessionId) => "revoked_session_" + sessionId;
}

/// <summary>
/// Cookie authentication events that keep a session honest after sign-in:
/// every request re-checks that the account still exists, is active, and still has the role in the cookie.
/// </summary>
public static class CookieSessionGuard
{
    /// <summary>Stamp each new session with a unique id so it can be ended individually.</summary>
    public static Task OnSigningIn(CookieSigningInContext context)
    {
        if (context.Principal?.Identity is ClaimsIdentity identity)
        {
            foreach (var old in identity.FindAll(AuthClaims.SessionId).ToList())
                identity.RemoveClaim(old);
            identity.AddClaim(new Claim(AuthClaims.SessionId, Guid.NewGuid().ToString("N")));
        }

        return Task.CompletedTask;
    }

    /// <summary>On logout, remember the session id so the same cookie is refused from now on.</summary>
    public static Task OnSigningOut(CookieSigningOutContext context)
    {
        var sessionId = context.HttpContext.User.FindFirst(AuthClaims.SessionId)?.Value;
        if (!string.IsNullOrEmpty(sessionId))
        {
            var store = context.HttpContext.RequestServices.GetRequiredService<SessionRevocationStore>();
            // A cookie cannot outlive ExpireTimeSpan without being renewed, and a revoked one is never renewed.
            store.Revoke(sessionId, context.Options.ExpireTimeSpan + TimeSpan.FromMinutes(5));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reject the cookie when the session was logged out or the account was deleted / deactivated;
    /// refresh the role claim when the account's role changed.
    /// </summary>
    public static async Task OnValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var services = context.HttpContext.RequestServices;

        var sessionId = principal?.FindFirst(AuthClaims.SessionId)?.Value;
        if (principal is null
            || string.IsNullOrEmpty(sessionId)
            || services.GetRequiredService<SessionRevocationStore>().IsRevoked(sessionId)
            || !int.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            context.RejectPrincipal();
            return;
        }

        var ct = context.HttpContext.RequestAborted;
        var factory = services.GetRequiredService<IDbContextFactory<HelpDeskDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var account = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.Role })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (account is null || !account.IsActive)
        {
            context.RejectPrincipal();
            return;
        }

        // Same rule as sign-in: highest of the account's own role and its Windows-mapped group rows.
        var role = account.Role;
        if (role != UserRole.Admin)
        {
            var groupIds = DeskGroupAccessResolver.GetWindowsMappedGroupUserIdsFromClaims(principal);
            if (groupIds.Count > 0)
            {
                var adminViaGroup = await db.Users.AsNoTracking()
                    .AnyAsync(u => groupIds.Contains(u.Id) && u.UserType == UserType.Group && u.IsActive && u.Role == UserRole.Admin, ct)
                    .ConfigureAwait(false);
                if (adminViaGroup)
                    role = UserRole.Admin;
            }
        }

        var roleClaims = principal.FindAll(ClaimTypes.Role).ToList();
        if (roleClaims.Count == 1 && roleClaims[0].Value == role.ToString())
            return;

        if (principal.Identity is not ClaimsIdentity current)
        {
            context.RejectPrincipal();
            return;
        }

        var updated = current.Clone();
        foreach (var c in updated.FindAll(ClaimTypes.Role).ToList())
            updated.RemoveClaim(c);
        updated.AddClaim(new Claim(ClaimTypes.Role, role.ToString()));
        context.ReplacePrincipal(new ClaimsPrincipal(updated));
        context.ShouldRenew = true;
    }
}
