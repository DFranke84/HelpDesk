namespace HelpDesk.Models;

public class DeskGroup
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public ICollection<DeskApplicationGroup> ApplicationGroups { get; set; } = new List<DeskApplicationGroup>();

    public ICollection<UserGroup> UserGroups { get; set; } = new List<UserGroup>();
}

