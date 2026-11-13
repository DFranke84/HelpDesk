using System.Security.Claims;

namespace HelpDesk.Books.Services;

/// <summary>What a signed-in user may do on the Books page.</summary>
public sealed record BooksPermissions(bool CanRead, bool CanCreate, bool CanUpdate, bool CanDelete)
{
    /// <summary>No access at all.</summary>
    public static readonly BooksPermissions None = new(false, false, false, false);
}

/// <summary>
/// Answers "what may this user do with Books?" using the host application's central permission rules,
/// so the Books page and the Books API cannot drift from the rest of Help Desk.
/// </summary>
public interface IBooksAccess
{
    /// <summary>
    /// Get the current Books permissions for a user (always read fresh; never cached by the caller).
    /// </summary>
    /// <param name="user">Signed-in principal <see cref="ClaimsPrincipal"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Effective permissions; <see cref="BooksPermissions.None"/> when not signed in.</returns>
    Task<BooksPermissions> GetPermissionsAsync(ClaimsPrincipal? user, CancellationToken ct = default);
}
