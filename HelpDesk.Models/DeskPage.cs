using System.ComponentModel.DataAnnotations;

namespace HelpDesk.Models;

/// <summary>
/// Defines the type of a desk page.
/// </summary>
public enum PageType
{
    /// <summary>A regular navigable page with content.</summary>
    Page = 0,
    
    /// <summary>A gradient separator for visual grouping.</summary>
    GradientSeparator = 1,
    
    /// <summary>A shaded separator with optional text for visual grouping.</summary>
    ShadedSeparator = 2
}

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

    /// <summary>The type of page (Page, GradientSeparator, or ShadedSeparator).</summary>
    public PageType PageType { get; set; } = PageType.Page;

    /// <summary>Optional text for shaded separators to indicate grouping.</summary>
    [MaxLength(200)]
    public string? SeparatorText { get; set; }

    public int DeskApplicationId { get; set; }

    public DeskApplication DeskApplication { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public byte[]? RowVersion { get; set; }
}

