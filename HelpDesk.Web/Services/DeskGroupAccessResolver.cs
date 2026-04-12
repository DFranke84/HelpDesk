using System.Security.Claims;
using System.Text.Json;
using HelpDesk.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Web.Services;

/// <summary>
/// Resolves <see cref="HelpDesk.Models.DeskGroup"/> catalog ids for menus and page access checks.
/// Session <see cref="AuthClaims.HelpdeskGroups"/> stores HelpDesk <c>User</c> ids (Windows group rows),
/// not <c>Groups</c> table ids; <see cref="HelpDesk.Models.DeskApplicationGroup.DeskGroupId"/> is always a catalog group id.
/// </summary>
internal static class DeskGroupAccessResolver
{
    public static List<int> GetWindowsMappedGroupUserIdsFromClaims(ClaimsPrincipal? user)
    {
        if (user == null) return new List<int>();

        var groupsClaim = user.FindFirst(AuthClaims.HelpdeskGroups)?.Value;
        if (string.IsNullOrEmpty(groupsClaim)) return new List<int>();

        try
        {
            var groupData = JsonSerializer.Deserialize<List<HelpdeskGroupClaim>>(groupsClaim);
            return groupData?.Select(g => g.Id).ToList() ?? new List<int>();
        }
        catch
        {
            return new List<int>();
        }
    }

    /// <summary>
    /// Desk group ids: <c>UserGroups</c> for the person plus <c>UserGroups</c> for each Windows-mapped group user id.
    /// </summary>
    public static async Task<List<int>> GetEffectiveDeskGroupIdsAsync(
        HelpDeskDbContext db,
        int userId,
        IReadOnlyList<int> windowsGroupUserIds,
        CancellationToken ct = default)
    {
        var fromPerson = await db.UserGroups.AsNoTracking()
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.GroupId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (windowsGroupUserIds.Count == 0)
            return fromPerson.Distinct().ToList();

        var fromWindowsGroupUsers = await db.UserGroups.AsNoTracking()
            .Where(ug => windowsGroupUserIds.Contains(ug.UserId))
            .Select(ug => ug.GroupId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return fromPerson.Union(fromWindowsGroupUsers).Distinct().ToList();
    }

    private sealed class HelpdeskGroupClaim
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int AccessLevel { get; set; }
    }
}
