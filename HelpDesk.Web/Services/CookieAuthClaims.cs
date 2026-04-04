using System.Security.Claims;
using HelpDesk.Models;

namespace HelpDesk.Web.Services;

public static class CookieAuthClaims
{
    public static List<Claim> BuildForFormsUser(User user) => BuildForUser(user, AuthClaims.SessionForms);

    public static List<Claim> BuildForWindowsUser(User user) => BuildForUser(user, AuthClaims.SessionWindows);

    /// <summary>Builds cookie claims; preserves <paramref name="sessionKind"/> when re-signing in after password change.</summary>
    public static List<Claim> BuildForUser(User user, string sessionKind)
    {
        var displayName = UserDisplayName.Format(user);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, displayName),
            new(ClaimTypes.Email, user.Email ?? ""),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(AuthClaims.SessionKind, sessionKind),
            new(AuthClaims.LoginId, user.LoginId)
        };
        if (user.MustChangePassword)
            claims.Add(new Claim(AuthClaims.MustChangePassword, "true"));
        return claims;
    }
}
