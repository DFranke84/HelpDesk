using HelpDesk.Models;
using HelpDesk.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Web.Services;

/// <summary>
/// Stops the application from being left with no administrator (which would lock everyone out of the admin pages).
/// </summary>
public static class LastAdminGuard
{
    /// <summary>Message shown when a change is refused.</summary>
    public const string Message =
        "This is the last active administrator. Make another account an administrator before changing or deleting this one.";

    /// <summary>
    /// True when the row is an active administrator and no other active administrator row exists.
    /// </summary>
    /// <param name="db">Help Desk context.</param>
    /// <param name="userId">Row about to be deleted, deactivated or demoted.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<bool> IsLastActiveAdminAsync(HelpDeskDbContext db, int userId, CancellationToken ct = default)
    {
        var isActiveAdmin = await db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.Role == UserRole.Admin, ct)
            .ConfigureAwait(false);
        if (!isActiveAdmin)
            return false;

        var anotherAdmin = await db.Users.AsNoTracking()
            .AnyAsync(u => u.Id != userId && u.IsActive && u.Role == UserRole.Admin, ct)
            .ConfigureAwait(false);
        return !anotherAdmin;
    }
}
