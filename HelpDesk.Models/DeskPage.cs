namespace HelpDesk.Models;

public class DeskPage
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Route { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public int DeskApplicationId { get; set; }

    public DeskApplication DeskApplication { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

