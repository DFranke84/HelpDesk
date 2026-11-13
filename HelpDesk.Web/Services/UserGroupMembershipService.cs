using System.Security.Claims;
using System.Text.Json;
using HelpDesk.Logging;
using HelpDesk.Models;
using HelpDesk.Web.Models;

namespace HelpDesk.Web.Services;

/// <summary>Service for managing user's group memberships and providing UI-friendly group information.</summary>
public interface IUserGroupMembershipService
{
    /// <summary>Get all HelpDesk groups the current user belongs to, ranked by access level.</summary>
    Task<List<UserGroupInfo>> GetCurrentUserGroupsAsync();
    
    /// <summary>Get all HelpDesk groups a specific user belongs to, ranked by access level.</summary>
    Task<List<UserGroupInfo>> GetUserGroupsAsync(int userId);
    
    /// <summary>Get the primary group (highest access) for the current user.</summary>
    Task<UserGroupInfo?> GetPrimaryGroupAsync();
    
    /// <summary>Check if the current user belongs to multiple groups.</summary>
    bool HasMultipleGroups();
    
    /// <summary>Get group information from claims (for performance when available).</summary>
    List<UserGroupInfo> GetGroupsFromClaims();
}

public class UserGroupMembershipService : IUserGroupMembershipService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IWindowsGroupService _windowsGroupService;
    private readonly ICurrentUserContext _currentUserContext;

    public UserGroupMembershipService(
        IHttpContextAccessor httpContextAccessor,
        IWindowsGroupService windowsGroupService,
        ICurrentUserContext currentUserContext)
    {
        _httpContextAccessor = httpContextAccessor;
        _windowsGroupService = windowsGroupService;
        _currentUserContext = currentUserContext;
    }

    public async Task<List<UserGroupInfo>> GetCurrentUserGroupsAsync()
    {
        try
        {
            var userId = _currentUserContext.UserId;
            if (userId == null) return new List<UserGroupInfo>();

            return await GetUserGroupsAsync(userId.Value);
        }
        catch (Exception)
        {
            // Return empty list if user context is unavailable (e.g., during logout)
            return new List<UserGroupInfo>();
        }
    }

    public async Task<List<UserGroupInfo>> GetUserGroupsAsync(int userId)
    {
        try
        {
            // The cookie only describes the signed-in user; for anyone else go to the database.
            if (_currentUserContext.UserId == userId)
            {
                var groupsFromClaims = GetGroupsFromClaims();
                if (groupsFromClaims.Any())
                {
                    return groupsFromClaims;
                }
            }

            // Fallback to database query
            return await _windowsGroupService.GetUserHelpdeskGroupsAsync(userId);
        }
        catch (Exception)
        {
            // Return empty list if services are unavailable (e.g., during logout)
            return new List<UserGroupInfo>();
        }
    }

    public async Task<UserGroupInfo?> GetPrimaryGroupAsync()
    {
        var groups = await GetCurrentUserGroupsAsync();
        return groups.OrderByDescending(g => g.AccessLevel).FirstOrDefault();
    }

    public bool HasMultipleGroups()
    {
        var groupsFromClaims = GetGroupsFromClaims();
        return groupsFromClaims.Count > 1;
    }

    public List<UserGroupInfo> GetGroupsFromClaims()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) return new List<UserGroupInfo>();

        var groupsClaim = user.FindFirst(AuthClaims.HelpdeskGroups)?.Value;
        if (string.IsNullOrEmpty(groupsClaim)) return new List<UserGroupInfo>();

        try
        {
            var groupData = JsonSerializer.Deserialize<List<UserGroupClaim>>(groupsClaim);
            return groupData?.Select(g => new UserGroupInfo
            {
                Id = g.Id,
                Name = g.Name,
                AccessLevel = g.AccessLevel
            }).OrderByDescending(g => g.AccessLevel).ToList() ?? new List<UserGroupInfo>();
        }
        catch
        {
            return new List<UserGroupInfo>();
        }
    }

    /// <summary>Helper class for deserializing group claims.</summary>
    private class UserGroupClaim
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int AccessLevel { get; set; }
    }
}