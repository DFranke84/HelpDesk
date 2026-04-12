using System.Security.Principal;
using Microsoft.EntityFrameworkCore;
using HelpDesk.Models;
using HelpDesk.Models.Data;
using HelpDesk.Web.Models;

namespace HelpDesk.Web.Services;

/// <summary>Service for discovering and managing Windows group memberships.</summary>
public interface IWindowsGroupService
{
    /// <summary>Get all Windows groups for the specified user identity.</summary>
    Task<List<WindowsGroupInfo>> GetUserWindowsGroupsAsync(WindowsIdentity identity);

    /// <summary>
    /// Resolve only Windows token groups that correspond to active <see cref="UserType.Group"/> rows
    /// (avoids translating and logging every token group).
    /// </summary>
    Task<List<WindowsGroupInfo>> GetUserWindowsGroupsMatchingHelpdeskAsync(WindowsIdentity identity);
    
    /// <summary>Get HelpDesk group entities that match the user's Windows groups.</summary>
    Task<List<User>> GetHelpdeskGroupEntitiesAsync(List<WindowsGroupInfo> windowsGroups);
    
    /// <summary>Get user's HelpDesk groups with access level information.</summary>
    Task<List<UserGroupInfo>> GetUserHelpdeskGroupsAsync(int userId);
    
    /// <summary>Calculate access level for a group (for dropdown ranking).</summary>
    Task<int> CalculateGroupAccessLevelAsync(int groupId);
}

public class WindowsGroupService : IWindowsGroupService
{
    private readonly IDbContextFactory<HelpDeskDbContext> _dbFactory;
    private readonly ILogger<WindowsGroupService> _logger;
    private readonly IConfiguration _configuration;

    public WindowsGroupService(
        IDbContextFactory<HelpDeskDbContext> dbFactory, 
        ILogger<WindowsGroupService> logger,
        IConfiguration configuration)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<List<WindowsGroupInfo>> GetUserWindowsGroupsAsync(WindowsIdentity identity)
    {
        var groups = new List<WindowsGroupInfo>();
        var includeLocalGroups = _configuration.GetValue("App:IncludeLocalGroups", true);
        var includeDomainGroups = _configuration.GetValue("App:IncludeDomainGroups", true);

        if (identity.Groups == null)
        {
            _logger.LogWarning("No groups found for Windows identity {Name}", identity.Name);
            return groups;
        }

        foreach (var groupSid in identity.Groups)
        {
            try
            {
                // Resolve SID to friendly name
                var account = groupSid.Translate(typeof(NTAccount)) as NTAccount;
                if (account?.Value == null) continue;

                var groupName = account.Value;
                var isDomainGroup = IsDomainGroup(groupName);
                var isLocalGroup = IsLocalGroup(groupName);

                // Apply configuration filters
                if (isDomainGroup && !includeDomainGroups) continue;
                if (isLocalGroup && !includeLocalGroups) continue;

                groups.Add(new WindowsGroupInfo 
                { 
                    Sid = groupSid.Value,
                    Name = groupName,
                    IsDomainGroup = isDomainGroup,
                    IsLocalGroup = isLocalGroup,
                    NameResolved = true
                });

                _logger.LogTrace("Resolved group: {GroupName} (SID: {Sid}, Domain: {IsDomain})", 
                    groupName, groupSid.Value, isDomainGroup);
            }
            catch (Exception ex)
            {
                // Some SIDs may not resolve (deleted accounts, etc.)
                _logger.LogWarning("Could not resolve group SID {Sid}: {Error}", 
                    groupSid.Value, ex.Message);
                
                // Still add the group with unresolved name
                groups.Add(new WindowsGroupInfo 
                { 
                    Sid = groupSid.Value,
                    Name = $"SID:{groupSid.Value}",
                    IsDomainGroup = false,
                    IsLocalGroup = false,
                    NameResolved = false
                });
            }
        }

        _logger.LogInformation("Found {Count} Windows groups for user {Name}", 
            groups.Count, identity.Name);

        return groups;
    }

    public async Task<List<WindowsGroupInfo>> GetUserWindowsGroupsMatchingHelpdeskAsync(WindowsIdentity identity)
    {
        var groups = new List<WindowsGroupInfo>();
        var includeLocalGroups = _configuration.GetValue("App:IncludeLocalGroups", true);
        var includeDomainGroups = _configuration.GetValue("App:IncludeDomainGroups", true);

        if (identity.Groups == null)
        {
            _logger.LogWarning("No groups found for Windows identity {Name}", identity.Name);
            return groups;
        }

        using var db = await _dbFactory.CreateDbContextAsync();
        var knownRows = await db.Users.AsNoTracking()
            .Where(u => u.UserType == UserType.Group && u.IsActive)
            .Select(u => new { u.LoginId, u.DomainSamAccountName, u.DomainUserPrincipalName })
            .ToListAsync();

        if (knownRows.Count == 0)
        {
            _logger.LogInformation(
                "Found {Count} HelpDesk-matched Windows groups for user {Name} (no active group rows in database)",
                0, identity.Name);
            return groups;
        }

        var sidToRow = new Dictionary<string, (string LoginId, string? Sam)>(StringComparer.OrdinalIgnoreCase);
        var nameUpper = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in knownRows)
        {
            nameUpper.Add(r.LoginId.ToUpperInvariant());
            if (r.DomainSamAccountName != null)
                nameUpper.Add(r.DomainSamAccountName.ToUpperInvariant());
            if (LooksLikeWindowsSid(r.DomainUserPrincipalName))
            {
                var key = NormalizeSidKey(r.DomainUserPrincipalName!);
                if (!sidToRow.ContainsKey(key))
                    sidToRow[key] = (r.LoginId, r.DomainSamAccountName);
            }
        }

        var anyRowNeedsNameResolution = knownRows.Any(r =>
            string.IsNullOrEmpty(r.DomainUserPrincipalName) || !LooksLikeWindowsSid(r.DomainUserPrincipalName));

        var matchedTokenSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Phase 1: token SID matches DomainUserPrincipalName (object SID) — no Translate required.
        foreach (IdentityReference groupSid in identity.Groups)
        {
            var sidVal = groupSid.Value;
            var key = NormalizeSidKey(sidVal);
            if (!sidToRow.TryGetValue(key, out var row))
                continue;

            var displayName = PickNtAccountName(row.LoginId, row.Sam);
            if (!PassesGroupLocationFilters(displayName, includeDomainGroups, includeLocalGroups))
                continue;

            groups.Add(new WindowsGroupInfo
            {
                Sid = sidVal,
                Name = displayName,
                IsDomainGroup = IsDomainGroup(displayName),
                IsLocalGroup = IsLocalGroup(displayName),
                NameResolved = true
            });
            matchedTokenSids.Add(sidVal);
            _logger.LogTrace(
                "HelpDesk-matched group (SID): {GroupName} (SID: {Sid})",
                displayName, sidVal);
        }

        // Phase 2: rows without stored SID — Translate token SIDs not already matched and compare names.
        if (anyRowNeedsNameResolution)
        {
            foreach (IdentityReference groupSid in identity.Groups)
            {
                if (matchedTokenSids.Contains(groupSid.Value))
                    continue;

                try
                {
                    var account = groupSid.Translate(typeof(NTAccount)) as NTAccount;
                    if (account?.Value == null)
                        continue;

                    var groupName = account.Value;
                    if (!nameUpper.Contains(groupName.ToUpperInvariant()))
                        continue;

                    if (!PassesGroupLocationFilters(groupName, includeDomainGroups, includeLocalGroups))
                        continue;

                    groups.Add(new WindowsGroupInfo
                    {
                        Sid = groupSid.Value,
                        Name = groupName,
                        IsDomainGroup = IsDomainGroup(groupName),
                        IsLocalGroup = IsLocalGroup(groupName),
                        NameResolved = true
                    });
                    matchedTokenSids.Add(groupSid.Value);
                    _logger.LogTrace(
                        "HelpDesk-matched group (name): {GroupName} (SID: {Sid})",
                        groupName, groupSid.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(
                        "Skipped token SID {Sid} during HelpDesk name match: {Error}",
                        groupSid.Value, ex.Message);
                }
            }
        }

        _logger.LogInformation(
            "Found {Count} HelpDesk-matched Windows groups for user {Name}",
            groups.Count, identity.Name);

        return groups;
    }

    private static bool PassesGroupLocationFilters(string displayName, bool includeDomainGroups, bool includeLocalGroups)
    {
        var isDomain = IsDomainGroupStatic(displayName);
        if (isDomain && !includeDomainGroups)
            return false;
        if (!isDomain && !includeLocalGroups)
            return false;
        return true;
    }

    private static string PickNtAccountName(string loginId, string? domainSamAccountName) =>
        !string.IsNullOrWhiteSpace(domainSamAccountName) ? domainSamAccountName : loginId;

    private static bool LooksLikeWindowsSid(string? s) =>
        !string.IsNullOrEmpty(s) && s.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSidKey(string sid) => sid.ToUpperInvariant();

    /// <summary>Same rules as <see cref="IsDomainGroup"/> but static for use in early filters.</summary>
    private static bool IsDomainGroupStatic(string groupName)
    {
        if (!groupName.Contains('\\'))
            return false;
        var parts = groupName.Split('\\');
        if (parts.Length != 2)
            return false;
        var domain = parts[0];
        return !string.Equals(domain, Environment.MachineName, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(domain, "BUILTIN", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<List<User>> GetHelpdeskGroupEntitiesAsync(List<WindowsGroupInfo> windowsGroups)
    {
        if (!windowsGroups.Any()) return new List<User>();

        using var db = await _dbFactory.CreateDbContextAsync();

        // Windows NTAccount names are often mixed case; HelpDesk login IDs are stored uppercase.
        var groupNamesUpper = windowsGroups.Select(g => g.Name.ToUpperInvariant()).ToList();
        var groupSids = windowsGroups.Select(g => g.Sid).ToList();

        return await db.Users
            .Where(u => u.UserType == UserType.Group && u.IsActive &&
                       (groupNamesUpper.Contains(u.LoginId.ToUpper()) ||
                        (u.DomainSamAccountName != null &&
                         groupNamesUpper.Contains(u.DomainSamAccountName.ToUpper())) ||
                        (u.DomainUserPrincipalName != null &&
                         groupSids.Contains(u.DomainUserPrincipalName))))
            .ToListAsync();
    }

    public async Task<List<UserGroupInfo>> GetUserHelpdeskGroupsAsync(int userId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        // Get user's Windows identity to find their groups
        var user = await db.Users.FindAsync(userId);
        if (user == null) return new List<UserGroupInfo>();

        // For this implementation, we'll get groups from existing UserGroups relationships
        // In practice, you might want to refresh from Windows identity periodically
        var userGroupIds = await db.UserGroups
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.GroupId)
            .ToListAsync();

        var helpdeskGroups = await db.Users
            .Where(u => userGroupIds.Contains(u.Id) && u.UserType == UserType.Group && u.IsActive)
            .ToListAsync();

        var groupInfos = new List<UserGroupInfo>();
        
        foreach (var group in helpdeskGroups)
        {
            var accessLevel = await CalculateGroupAccessLevelAsync(group.Id);
            var appCount = await GetApplicationCountForGroupAsync(group.Id);
            var permCount = await GetPermissionCountForGroupAsync(group.Id);

            groupInfos.Add(new UserGroupInfo 
            { 
                Id = group.Id,
                Name = group.LoginId,
                AccessLevel = accessLevel,
                ApplicationCount = appCount,
                PermissionCount = permCount,
                Role = group.Role
            });
        }

        return groupInfos.OrderByDescending(g => g.AccessLevel).ToList();
    }

    public async Task<int> CalculateGroupAccessLevelAsync(int groupId)
    {
        var roleWeight = _configuration.GetValue("GroupAccessRanking:RoleWeight", 100);
        var applicationWeight = _configuration.GetValue("GroupAccessRanking:ApplicationWeight", 10);
        var pageWeight = _configuration.GetValue("GroupAccessRanking:PageWeight", 1);

        using var db = await _dbFactory.CreateDbContextAsync();

        var group = await db.Users.FindAsync(groupId);
        if (group == null) return 0;

        var accessLevel = 0;

        // Add role weight (Admin = 100 points)
        if (group.Role == UserRole.Admin)
        {
            accessLevel += roleWeight;
        }

        // Add application access weight
        var appCount = await GetApplicationCountForGroupAsync(groupId);
        accessLevel += appCount * applicationWeight;

        // Add page permission weight
        var permCount = await GetPermissionCountForGroupAsync(groupId);
        accessLevel += permCount * pageWeight;

        return accessLevel;
    }

    private async Task<int> GetApplicationCountForGroupAsync(int groupId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        
        return await db.UserGroups
            .Where(ug => ug.UserId == groupId)
            .Select(ug => ug.GroupId)
            .Distinct()
            .CountAsync();
    }

    private async Task<int> GetPermissionCountForGroupAsync(int groupId)
    {
        var createWeight = _configuration.GetValue("GroupAccessRanking:CrudWeights:Create", 4);
        var updateWeight = _configuration.GetValue("GroupAccessRanking:CrudWeights:Update", 3);
        var deleteWeight = _configuration.GetValue("GroupAccessRanking:CrudWeights:Delete", 2);
        var readWeight = _configuration.GetValue("GroupAccessRanking:CrudWeights:Read", 1);

        using var db = await _dbFactory.CreateDbContextAsync();

        var permissions = await db.UserPagePermissions
            .Where(upp => upp.UserId == groupId)
            .ToListAsync();

        var totalWeight = 0;
        foreach (var perm in permissions)
        {
            if (perm.CanCreate) totalWeight += createWeight;
            if (perm.CanUpdate) totalWeight += updateWeight;
            if (perm.CanDelete) totalWeight += deleteWeight;
            if (perm.CanRead) totalWeight += readWeight;
        }

        return totalWeight;
    }

    private bool IsDomainGroup(string groupName)
    {
        if (!groupName.Contains('\\')) return false;
        
        var parts = groupName.Split('\\');
        if (parts.Length != 2) return false;

        var domain = parts[0];
        
        // Not a domain group if it's the local machine name or BUILTIN
        return !string.Equals(domain, Environment.MachineName, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(domain, "BUILTIN", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsLocalGroup(string groupName) => !IsDomainGroup(groupName);
}