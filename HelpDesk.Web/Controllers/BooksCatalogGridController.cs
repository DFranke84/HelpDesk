using HelpDesk.Models.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Web.Controllers;

/// <summary>
/// Serve books catalog grid JSON from the external Books database.
/// </summary>
[ApiController]
[Authorize]
[Route("api/grid/books")]
public sealed class BooksCatalogGridController : ControllerBase
{
    private readonly BooksDbContext _db;

    /// <summary>
    /// Create books catalog grid controller.
    /// </summary>
    /// <param name="db">Books database <see cref="BooksDbContext"/>.</param>
    public BooksCatalogGridController(BooksDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// List books for the catalog grid.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>JSON rows for books.</returns>
    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(CancellationToken ct)
    {
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
