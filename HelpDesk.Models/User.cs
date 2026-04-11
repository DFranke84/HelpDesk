using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace HelpDesk.Models;

/// <summary>Application user with login credentials, role, and optional Active Directory mapping.</summary>
public sealed class User
{
    public int Id { get; set; }

    [Required, MaxLength(256)]
    [Column("LoginID")]
    public string LoginId { get; set; } = string.Empty;

    /// <summary>Type of user: individual person or Windows group.</summary>
    public UserType UserType { get; set; } = UserType.User;

    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    [JsonIgnore]
    [MaxLength(500)]
    public string? PasswordHash { get; set; }

    [MaxLength(256)]
    public string? Email { get; set; }

    public UserRole Role { get; set; }

    /// <summary>Optional SAM account name (DOMAIN\user) for Windows matching.</summary>
    [MaxLength(256)]
    public string? DomainSamAccountName { get; set; }

    /// <summary>Optional UPN (user@domain.com) for Windows matching.</summary>
    [MaxLength(256)]
    public string? DomainUserPrincipalName { get; set; }

    /// <summary>When true, the user must change password before using the app (e.g. initial admin).</summary>
    public bool MustChangePassword { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public byte[]? RowVersion { get; set; }

    public ICollection<UserGroup> UserGroups { get; set; } = new List<UserGroup>();
}

