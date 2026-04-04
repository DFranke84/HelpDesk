namespace HelpDesk.Logging;

/// <summary>Optional per-scope user id for entity change history (null when unavailable).</summary>
public interface ICurrentUserContext
{
    int? UserId { get; }
}

