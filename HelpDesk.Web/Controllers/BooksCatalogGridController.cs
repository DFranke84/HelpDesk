using HelpDesk.Models.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Web.Controllers;

[ApiController]
[Authorize]
[Route("api/grid/books")]
public sealed class BooksCatalogGridController : ControllerBase
{
    private readonly BooksDbContext _db;

    public BooksCatalogGridController(BooksDbContext db)
    {
        _db = db;
    }

    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(CancellationToken ct)
    {
        var rows = await _db.Books.AsNoTracking()
            .OrderBy(b => b.Title)
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
