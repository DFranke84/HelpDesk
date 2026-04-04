using System.Text.Json;
using HelpDesk.Models;
using HelpDesk.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Web.Services;

/// <summary>
/// Load and label entity change history rows for admin grids.
/// </summary>
public static class EntityChangeHistoryFormatter
{
    /// <summary>
    /// Query change history with friendly labels for related entities.
    /// </summary>
    /// <param name="db">Help Desk database <see cref="HelpDeskDbContext"/>.</param>
    /// <param name="filterType">Optional entity type substring filter.</param>
    /// <param name="take">Maximum rows to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Formatted rows for display.</returns>
    public static async Task<List<EntityChangeHistoryRowDto>> LoadRowsAsync(
        HelpDeskDbContext db,
        string? filterType,
        int take = 300,
        CancellationToken ct = default)
    {
        var q = db.EntityChangeHistories.AsNoTracking().Include(h => h.ChangedByUser).AsQueryable();
        if (!string.IsNullOrWhiteSpace(filterType))
            q = q.Where(h => h.EntityType.Contains(filterType.Trim()));

        var raw = await q.OrderByDescending(h => h.ChangedAt).Take(take).ToListAsync(ct).ConfigureAwait(false);

        var userEntityIds = new HashSet<int>();
        var appIds = new HashSet<int>();
        var pageIds = new HashSet<int>();
        var groupIds = new HashSet<int>();

        foreach (var h in raw)
        {
            switch (h.EntityType)
            {
                case var t when Matches(t, TrackedEntityType.User):
                    if (int.TryParse(h.EntityId, out var uid)) userEntityIds.Add(uid);
                    break;
                case var t when Matches(t, TrackedEntityType.DeskApplication):
                    if (int.TryParse(h.EntityId, out var aid)) appIds.Add(aid);
                    break;
                case var t when Matches(t, TrackedEntityType.DeskPage):
                    if (int.TryParse(h.EntityId, out var pid)) pageIds.Add(pid);
                    break;
                case var t when Matches(t, TrackedEntityType.DeskGroup):
                    if (int.TryParse(h.EntityId, out var gid)) groupIds.Add(gid);
                    break;
                case var t when Matches(t, TrackedEntityType.UserGroup):
                    var parts = h.EntityId.Split(':');
                    if (parts.Length == 2 && int.TryParse(parts[0], out var uu) && int.TryParse(parts[1], out var gg))
                    {
                        userEntityIds.Add(uu);
                        groupIds.Add(gg);
                    }
                    break;
                case var t when Matches(t, TrackedEntityType.DeskApplicationGroup):
                    var agParts = h.EntityId.Split(':');
                    if (agParts.Length == 2 && int.TryParse(agParts[0], out var dagApp) && int.TryParse(agParts[1], out var dagGrp))
                    {
                        appIds.Add(dagApp);
                        groupIds.Add(dagGrp);
                    }
                    break;
            }
        }

        var usersMap = await db.Users.AsNoTracking()
            .Where(u => userEntityIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct).ConfigureAwait(false);

        var appsMap = await db.Applications.AsNoTracking()
            .Where(a => appIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct).ConfigureAwait(false);

        var pagesMap = await db.Pages.AsNoTracking()
            .Include(p => p.DeskApplication)
            .Where(p => pageIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);

        var groupsMap = await db.Groups.AsNoTracking()
            .Include(g => g.ApplicationGroups).ThenInclude(ag => ag.DeskApplication)
            .Where(g => groupIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, ct).ConfigureAwait(false);

        return raw.Select(h => new EntityChangeHistoryRowDto
        {
            Id = h.Id,
            EntityType = h.EntityType,
            EntityLabel = ResolveEntityLabel(h, usersMap, appsMap, pagesMap, groupsMap),
            Action = h.Action.ToString(),
            ChangedBy = h.ChangedByUser is { } cu ? UserDisplayName.Format(cu) : "—",
            ChangedAt = h.ChangedAt.ToString("u"),
            JsonChanges = h.JsonChanges
        }).ToList();
    }

    private static bool Matches(string? stored, string expected) =>
        string.Equals(stored, expected, StringComparison.OrdinalIgnoreCase);

    private static string ResolveEntityLabel(
        EntityChangeHistory h,
        IReadOnlyDictionary<int, User> usersMap,
        IReadOnlyDictionary<int, DeskApplication> appsMap,
        IReadOnlyDictionary<int, DeskPage> pagesMap,
        IReadOnlyDictionary<int, DeskGroup> groupsMap)
    {
        var t = h.EntityType ?? "";

        if (Matches(t, TrackedEntityType.User))
        {
            if (int.TryParse(h.EntityId, out var uid) && usersMap.TryGetValue(uid, out var u))
                return UserDisplayName.Format(u);
            if (TryGetNameFromJson(h.JsonChanges, out var jn))
                return jn;
            return h.EntityId;
        }

        if (Matches(t, TrackedEntityType.DeskApplication))
        {
            if (int.TryParse(h.EntityId, out var aid) && appsMap.TryGetValue(aid, out var app))
                return app.Name;
            if (TryGetNameFromJson(h.JsonChanges, out var jn))
                return jn;
            return h.EntityId;
        }

        if (Matches(t, TrackedEntityType.DeskPage))
        {
            if (int.TryParse(h.EntityId, out var pid) && pagesMap.TryGetValue(pid, out var p))
                return FormatPageLabel(p);
            if (TryGetNameFromJson(h.JsonChanges, out var jn))
                return jn;
            return h.EntityId;
        }

        if (Matches(t, TrackedEntityType.DeskGroup))
        {
            if (int.TryParse(h.EntityId, out var gid) && groupsMap.TryGetValue(gid, out var g))
                return FormatGroupLabel(g);
            if (TryGetNameFromJson(h.JsonChanges, out var jn))
                return jn;
            return h.EntityId;
        }

        if (Matches(t, TrackedEntityType.UserGroup))
        {
            var parts = h.EntityId.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out var uu) && int.TryParse(parts[1], out var gg)
                && usersMap.TryGetValue(uu, out var u2) && groupsMap.TryGetValue(gg, out var g2))
                return $"{UserDisplayName.Format(u2)} → {FormatGroupLabel(g2)}";
            return h.EntityId;
        }

        if (Matches(t, TrackedEntityType.DeskApplicationGroup))
        {
            var agParts2 = h.EntityId.Split(':');
            if (agParts2.Length == 2 && int.TryParse(agParts2[0], out var linkAppId) && int.TryParse(agParts2[1], out var linkGrpId)
                && appsMap.TryGetValue(linkAppId, out var linkApp) && groupsMap.TryGetValue(linkGrpId, out var linkGrp))
                return $"{linkApp.Name} / {linkGrp.Name}";
            return h.EntityId;
        }

        return h.EntityId;
    }

    private static bool TryGetNameFromJson(string? json, out string name)
    {
        name = "";
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            if (doc.RootElement.TryGetProperty("Name", out var nameEl))
            {
                var s = nameEl.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    name = s!;
                    return true;
                }
            }
        }
        catch
        {
            /* ignore */
        }

        return false;
    }

    private static string FormatPageLabel(DeskPage p)
    {
        var appPart = p.DeskApplication?.Name ?? "—";
        return $"{appPart} / {p.Name}";
    }

    private static string FormatGroupLabel(DeskGroup g)
    {
        var names = g.ApplicationGroups
            .Select(ag => ag.DeskApplication?.Name)
            .Where(s => !string.IsNullOrEmpty(s))
            .OrderBy(s => s)
            .ToList();
        var appPart = names.Count > 0 ? string.Join(", ", names) : "—";
        return $"{appPart} / {g.Name}";
    }
}

public sealed class EntityChangeHistoryRowDto
{
    public long Id { get; init; }
    public string EntityType { get; init; } = "";
    public string EntityLabel { get; init; } = "";
    public string Action { get; init; } = "";
    public string ChangedBy { get; init; } = "";
    public string ChangedAt { get; init; } = "";
    public string? JsonChanges { get; init; }
}
