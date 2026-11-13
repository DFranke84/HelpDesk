using System.Security.Claims;
using HelpDesk.Books.Services;
using HelpDesk.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Web.Services;

/// <summary>
/// Books permissions resolved through <see cref="IPagePermissionService"/> (same rules as every other page).
/// </summary>
public sealed class BooksAccess : IBooksAccess
{
    /// <summary>Catalog route of the Books page.</summary>
    public const string BooksRoute = "/user/books/books";

    private readonly IDbContextFactory<HelpDeskDbContext> _factory;
    private readonly IPagePermissionService _permissions;

    /// <summary>
    /// Create Books access resolver.
    /// </summary>
    /// <param name="factory">Help Desk context factory.</param>
    /// <param name="permissions">Central page permission service <see cref="IPagePermissionService"/>.</param>
    public BooksAccess(IDbContextFactory<HelpDeskDbContext> factory, IPagePermissionService permissions)
    {
        _factory = factory;
        _permissions = permissions;
    }

    /// <inheritdoc />
    public async Task<BooksPermissions> GetPermissionsAsync(ClaimsPrincipal? user, CancellationToken ct = default)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return BooksPermissions.None;

        int pageId;
        await using (var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false))
        {
            // 0 = page not in the catalog: nobody but an administrator gets in.
            pageId = await db.Pages.AsNoTracking()
                .Where(p => p.Route == BooksRoute)
                .Select(p => p.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
        }

        var p = await _permissions.GetPermissionsAsync(user, pageId).ConfigureAwait(false);
        return new BooksPermissions(p.CanRead, p.CanCreate, p.CanUpdate, p.CanDelete);
    }
}
