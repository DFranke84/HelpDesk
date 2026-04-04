using HelpDesk.Models;
using HelpDesk.Models.Data;
using HelpDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Web.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/grid/admin")]
public sealed class AdminCatalogGridController : ControllerBase
{
    private readonly HelpDeskDbContext _db;

    public AdminCatalogGridController(HelpDeskDbContext db)
    {
        _db = db;
    }

    [HttpGet("applications")]
    public async Task<IActionResult> Applications(CancellationToken ct)
    {
        var rows = await _db.Applications.AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new
            {
                a.Id,
                a.Name,
                externalKey = a.ExternalConnectionStringName,
                usesExternalDatabase = a.UsesExternalDatabase,
                a.IsActive
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return Ok(rows);
    }

    [HttpGet("pages")]
    public async Task<IActionResult> Pages(CancellationToken ct)
    {
        var rows = await _db.Pages.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new
            {
                p.Id,
                applicationName = p.DeskApplication!.Name,
                p.Name,
                p.Route,
                displayOrder = p.DisplayOrder,
                p.IsActive
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return Ok(rows);
    }

    [HttpGet("groups")]
    public async Task<IActionResult> Groups(CancellationToken ct)
    {
        var rows = await _db.Groups.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new { g.Id, groupName = g.Name, description = g.Description ?? "" })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return Ok(rows);
    }

    [HttpGet("application-groups")]
    public async Task<IActionResult> ApplicationGroups(CancellationToken ct)
    {
        var rows = await _db.DeskApplicationGroups.AsNoTracking()
            .Include(ag => ag.DeskApplication)
            .Include(ag => ag.DeskGroup)
            .OrderBy(ag => ag.DeskApplication!.Name).ThenBy(ag => ag.DeskGroup!.Name)
            .Select(ag => new
            {
                ag.DeskApplicationId,
                ag.DeskGroupId,
                applicationName = ag.DeskApplication!.Name,
                groupName = ag.DeskGroup!.Name
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return Ok(rows);
    }

    [HttpGet("user-groups")]
    public async Task<IActionResult> UserGroups(CancellationToken ct)
    {
        var projected = await _db.UserGroups.AsNoTracking()
            .OrderByDescending(ug => ug.AssignedAt)
            .Take(500)
            .Select(ug => new
            {
                ug.UserId,
                ug.GroupId,
                loginId = ug.User!.LoginId,
                firstName = ug.User!.FirstName,
                lastName = ug.User!.LastName,
                groupName = ug.Group!.Name,
                ug.AssignedAt
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var rows = projected.Select(ug => new
        {
            ug.UserId,
            ug.GroupId,
            ug.loginId,
            displayName = UserDisplayName.Format(ug.firstName, ug.lastName, ug.loginId),
            ug.groupName,
            assignedAt = ug.AssignedAt.ToString("u")
        });
        return Ok(rows);
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users(CancellationToken ct)
    {
        var projected = await _db.Users.AsNoTracking()
            .OrderBy(u => u.LoginId)
            .Select(u => new
            {
                u.Id,
                u.FirstName,
                u.LastName,
                u.LoginId,
                u.Email,
                u.Role,
                u.IsActive
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var rows = projected.Select(u => new
        {
            u.Id,
            name = UserDisplayName.Format(u.FirstName, u.LastName, u.LoginId),
            u.LoginId,
            u.Email,
            role = u.Role.ToString(),
            u.IsActive
        });
        return Ok(rows);
    }

    [HttpGet("change-history")]
    public async Task<IActionResult> ChangeHistory([FromQuery] string? filterType, CancellationToken ct)
    {
        var rows = await EntityChangeHistoryFormatter.LoadRowsAsync(_db, filterType, 300, ct).ConfigureAwait(false);
        return Ok(rows);
    }
}
