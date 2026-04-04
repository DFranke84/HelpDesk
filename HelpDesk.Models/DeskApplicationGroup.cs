namespace HelpDesk.Models;

/// <summary>Many-to-many: an application can use multiple groups; a group can belong to multiple applications (or none).</summary>
public class DeskApplicationGroup
{
    public int DeskApplicationId { get; set; }

    public DeskApplication DeskApplication { get; set; } = null!;

    public int DeskGroupId { get; set; }

    public DeskGroup DeskGroup { get; set; } = null!;
}

