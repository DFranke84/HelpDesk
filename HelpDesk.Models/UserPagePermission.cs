using System.ComponentModel.DataAnnotations;

namespace HelpDesk.Models;

/// <summary>CRUD permissions for a specific user on a specific application page.</summary>
public sealed class UserPagePermission
{
    public int UserId { get; set; }
    public int PageId { get; set; }
    
    /// <summary>Can create new records on this page.</summary>
    public bool CanCreate { get; set; }
    
    /// <summary>Can read/view content on this page.</summary>
    public bool CanRead { get; set; }
    
    /// <summary>Can update existing records on this page.</summary>
    public bool CanUpdate { get; set; }
    
    /// <summary>Can delete records on this page.</summary>
    public bool CanDelete { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public byte[]? RowVersion { get; set; }
    
    // Navigation properties
    public User User { get; set; } = null!;
    public DeskPage DeskPage { get; set; } = null!;
}