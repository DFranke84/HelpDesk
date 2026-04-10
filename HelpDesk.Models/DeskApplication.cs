using System.ComponentModel.DataAnnotations;

namespace HelpDesk.Models;

/// <summary>A registered application in the help desk catalog, optionally backed by an external database.</summary>
public sealed class DeskApplication
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

    public int DisplayOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public byte[]? RowVersion { get; set; }

    public ICollection<DeskApplicationGroup> ApplicationGroups { get; set; } = new List<DeskApplicationGroup>();
}

