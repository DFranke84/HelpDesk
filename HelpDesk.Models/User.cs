using System.ComponentModel.DataAnnotations.Schema;

namespace HelpDesk.Models;

public class User
{
    public int Id { get; set; }

    [Column("LoginID")]
    public string LoginId { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string? Email { get; set; }
    public UserRole Role { get; set; }
    /// <summary>Optional SAM account name (DOMAIN\user) for Windows matching.</summary>
    public string? DomainSamAccountName { get; set; }
    /// <summary>Optional UPN (user@domain.com) for Windows matching.</summary>
    public string? DomainUserPrincipalName { get; set; }
    /// <summary>When true, the user must change password before using the app (e.g. initial admin).</summary>
    public bool MustChangePassword { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<UserGroup> UserGroups { get; set; } = new List<UserGroup>();
}

