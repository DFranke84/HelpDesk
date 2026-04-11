using HelpDesk.Models;

namespace HelpDesk.Web.Models;

/// <summary>Represents a Windows group discovered for a user.</summary>
public class WindowsGroupInfo
{
    /// <summary>Windows Security Identifier (SID) for the group.</summary>
    public string Sid { get; set; } = string.Empty;
    
    /// <summary>Friendly name of the group (e.g., DOMAIN\GroupName).</summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>True if this is a domain group, false for local groups.</summary>
    public bool IsDomainGroup { get; set; }
    
    /// <summary>True if this is a local computer group.</summary>
    public bool IsLocalGroup { get; set; }
    
    /// <summary>True if group name was successfully resolved from SID.</summary>
    public bool NameResolved { get; set; } = true;
}

/// <summary>Represents a user's group membership with HelpDesk access information.</summary>
public class UserGroupInfo
{
    /// <summary>HelpDesk User entity ID for this group.</summary>
    public int Id { get; set; }
    
    /// <summary>Full qualified group name.</summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>Calculated access level for ranking groups in dropdown.</summary>
    public int AccessLevel { get; set; }
    
    /// <summary>Number of applications this group can access.</summary>
    public int ApplicationCount { get; set; }
    
    /// <summary>Total page permissions this group has.</summary>
    public int PermissionCount { get; set; }
    
    /// <summary>Role of this group (User/Admin).</summary>
    public UserRole Role { get; set; }
}