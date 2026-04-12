using System.Security.Claims;
using System.Text.Json;
using HelpDesk.Models;
using HelpDesk.Web.Models;

namespace HelpDesk.Web.Services;

public static class CookieAuthClaims
{
    /// <summary>
    /// Build claims for a forms-authenticated session.
    /// </summary>
    /// <param name="user">Application user <see cref="User"/>.</param>
    /// <returns>Claims list for cookie sign-in.</returns>
    public static List<Claim> BuildForFormsUser(User user) => BuildForUser(user, AuthClaims.SessionForms);

    /// <summary>
    /// Build claims for an integrated Windows session.
    /// </summary>
    /// <param name="user">Application user <see cref="User"/>.</param>
    /// <returns>Claims list for cookie sign-in.</returns>
    public static List<Claim> BuildForWindowsUser(User user) => BuildForUser(user, AuthClaims.SessionWindows);

    /// <summary>
    /// Build claims for an integrated Windows session with group information.
    /// </summary>
    /// <param name="user">Application user <see cref="User"/>.</param>
    /// <param name="userGroups">HelpDesk groups the user belongs to.</param>
    /// <returns>Claims list for cookie sign-in.</returns>
    public static List<Claim> BuildForWindowsUser(User user, List<UserGroupInfo> userGroups)
    {
        var claims = BuildForUser(user, AuthClaims.SessionWindows);

        if (userGroups?.Any() == true)
        {
            // Store group information as JSON for UI use
            var groupData = userGroups.Select(g => new { Id = g.Id, Name = g.Name, AccessLevel = g.AccessLevel }).ToList();
            claims.Add(new Claim(AuthClaims.HelpdeskGroups, JsonSerializer.Serialize(groupData)));
            
            // Primary group = group with most access
            var primaryGroup = userGroups.OrderByDescending(g => g.AccessLevel).First();
            claims.Add(new Claim(AuthClaims.PrimaryGroup, primaryGroup.Name));
        }

        return claims;
    }

    /// <summary>
    /// Build cookie claims and preserve session kind when re-signing in after password change.
    /// </summary>
    /// <param name="user">Application user <see cref="User"/>.</param>
    /// <param name="sessionKind">Session kind marker (forms or windows).</param>
    /// <returns>Claims list for cookie sign-in.</returns>
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
            new(AuthClaims.LoginId, user.LoginId),
            new(AuthClaims.UserType, user.UserType.ToString())
        };
        if (user.MustChangePassword)
            claims.Add(new Claim(AuthClaims.MustChangePassword, "true"));
        return claims;
    }
}
