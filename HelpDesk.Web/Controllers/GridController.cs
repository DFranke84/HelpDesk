using HelpDesk.Models.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Web.Controllers;

/// <summary>
/// Serve NLog grid rows for admin logger views.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/grid")]
public class GridController : ControllerBase
{
    private readonly HelpDeskDbContext _db;

    /// <summary>
    /// Create grid controller.
    /// </summary>
    /// <param name="db">Help Desk database <see cref="HelpDeskDbContext"/>.</param>
    public GridController(HelpDeskDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Query recent NLog rows with optional filters.
    /// </summary>
    /// <param name="correlationId">Optional correlation id filter.</param>
    /// <param name="levels">Comma-separated level filter.</param>
    /// <param name="activityCategories">Comma-separated activity category filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>JSON projection of NLog entries.</returns>
    [HttpGet("nlog")]
    public async Task<IActionResult> NLog(
        [FromQuery] Guid? correlationId,
        [FromQuery] string? levels,
        [FromQuery] string? activityCategories,
        CancellationToken ct)
    {
        var q = _db.NLogEntries.AsNoTracking().AsQueryable();
        if (correlationId.HasValue && correlationId.Value != Guid.Empty)
            q = q.Where(x => x.CorrelationId == correlationId.Value);

        if (!string.IsNullOrWhiteSpace(levels))
        {
            var set = levels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToUpperInvariant())
                .ToHashSet();
            q = q.Where(x => set.Contains(x.Level.ToUpper()));
        }

        if (!string.IsNullOrWhiteSpace(activityCategories))
        {
            var set = activityCategories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToUpperInvariant())
                .ToHashSet();
            q = q.Where(x => x.ActivityCategory != null && set.Contains(x.ActivityCategory.ToUpper()));
        }

        var rows = await q.OrderByDescending(x => x.Logged).Take(500).ToListAsync(ct).ConfigureAwait(false);
        return Ok(rows.Select(x => new
        {
            x.Id,
            // NLog stores the server's local time, so do not label it as UTC ("Z").
            Logged = x.Logged.ToString("yyyy-MM-dd HH:mm:ss"),
            x.Level,
            x.ActivityCategory,
            x.Message,
            x.Logger,
            x.Exception,
            x.ElapsedTime,
            x.NumIterations,
            x.CorrelationId,
            loginId = x.LoginId,
            x.ReturnCode
        }));
    }
}
