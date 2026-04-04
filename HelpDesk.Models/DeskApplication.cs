using System.ComponentModel.DataAnnotations;

namespace HelpDesk.Models;

public class DeskApplication
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool UsesExternalDatabase { get; set; }

    /// <summary>Key in IConfiguration connection strings (not the secret itself).</summary>
    [MaxLength(200)]
    public string? ExternalConnectionStringName { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public byte[]? RowVersion { get; set; }

    public ICollection<DeskApplicationGroup> ApplicationGroups { get; set; } = new List<DeskApplicationGroup>();
}

