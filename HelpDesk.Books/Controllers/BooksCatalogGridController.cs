using HelpDesk.Books.Models.Data;
using HelpDesk.Books.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Books.Controllers;

/// <summary>
/// Serve books catalog grid JSON from the external Books database.
/// </summary>
[ApiController]
[Authorize]
[Route("api/grid/books")]
public sealed class BooksCatalogGridController : ControllerBase
{
    private readonly BooksDbContext _db;
    private readonly IBooksAccess _access;

    /// <summary>
    /// Create books catalog grid controller.
    /// </summary>
    /// <param name="db">Books database <see cref="BooksDbContext"/>.</param>
    /// <param name="access">Books permission resolver <see cref="IBooksAccess"/>.</param>
    public BooksCatalogGridController(BooksDbContext db, IBooksAccess access)
    {
        _db = db;
        _access = access;
    }

    /// <summary>
    /// List books for the catalog grid. Requires Read permission on the Books page.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>JSON rows for books, or 403 without Read permission.</returns>
    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(CancellationToken ct)
    {
        var permissions = await _access.GetPermissionsAsync(User, ct).ConfigureAwait(false);
        if (!permissions.CanRead)
            return StatusCode(StatusCodes.Status403Forbidden);

        var rows = await _db.Books.AsNoTracking()
            .OrderBy(b => b.Title)
            .Take(5000)
            .Select(b => new
            {
                id = b.BookId,
                isbn13 = b.Isbn13.Trim(),
                title = b.Title,
                publisher = b.Publisher != null ? b.Publisher.Name : "",
                year = b.PublicationYear,
                pages = b.Pages,
                lang = b.LanguageCode.Trim()
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Ok(rows);
    }
}
