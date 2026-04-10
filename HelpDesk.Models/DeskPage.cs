using System.ComponentModel.DataAnnotations;

namespace HelpDesk.Models;

/// <summary>A navigable page within a <see cref="DeskApplication"/>.</summary>
public sealed class DeskPage
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Route { get; set; }

    [MaxLength(500)]
    public string? Url { get; set; }

    public bool OpenInNewTab { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public int DeskApplicationId { get; set; }

    public DeskApplication DeskApplication { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public byte[]? RowVersion { get; set; }
}

