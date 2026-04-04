namespace HelpDesk.Logging;

/// <summary>Optional per-scope user id for entity change history (null when unavailable).</summary>
public interface ICurrentUserContext
{
    /// <summary>Authenticated user id from claims, or null when unknown.</summary>
    int? UserId { get; }
}

