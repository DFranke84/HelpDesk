namespace HelpDesk.Models;

public class UserGroup
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int GroupId { get; set; }
    public DeskGroup Group { get; set; } = null!;
    public DateTime AssignedAt { get; set; }
}

