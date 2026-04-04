namespace HelpDesk.Models;

public class EntityChangeHistory
{
    public long Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public EntityChangeAction Action { get; set; }
    public string? JsonChanges { get; set; }
    public int? ChangedByUserId { get; set; }
    public User? ChangedByUser { get; set; }
    public DateTime ChangedAt { get; set; }
}

