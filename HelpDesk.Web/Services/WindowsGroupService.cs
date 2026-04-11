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

                _logger.LogDebug("Resolved group: {GroupName} (SID: {Sid}, Domain: {IsDomain})", 
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

    public async Task<List<User>> GetHelpdeskGroupEntitiesAsync(List<WindowsGroupInfo> windowsGroups)
    {
        if (!windowsGroups.Any()) return new List<User>();

        using var db = await _dbFactory.CreateDbContextAsync();
        
        var groupNames = windowsGroups.Select(g => g.Name).ToList();
        var groupSids = windowsGroups.Select(g => g.Sid).ToList();

        return await db.Users
            .Where(u => u.UserType == UserType.Group && u.IsActive &&
                       (groupNames.Contains(u.LoginId) || 
                        groupNames.Contains(u.DomainSamAccountName) ||
                        groupSids.Contains(u.DomainUserPrincipalName))) // Store SID in UPN field for groups
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