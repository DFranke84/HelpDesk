using System.ComponentModel.DataAnnotations;

namespace HelpDesk.Models;

public class DeskGroup
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public byte[]? RowVersion { get; set; }

    public ICollection<DeskApplicationGroup> ApplicationGroups { get; set; } = new List<DeskApplicationGroup>();

    public ICollection<UserGroup> UserGroups { get; set; } = new List<UserGroup>();
}

