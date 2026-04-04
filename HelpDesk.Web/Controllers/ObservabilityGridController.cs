using HelpDesk.Models.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Web.Controllers;

[ApiController]
[Authorize]
[Route("api/grid")]
public class ObservabilityGridController : ControllerBase
{
    private readonly HelpDeskDbContext _db;

    public ObservabilityGridController(HelpDeskDbContext db)
    {
        _db = db;
    }

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
            Logged = x.Logged.ToString("u"),
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
