namespace HelpDesk.Logging;

/// <summary>
/// Thread-safe correlation id holder for observability scopes.
/// </summary>
public sealed class CorrelationContext : ICorrelationContext
{
    private readonly object _lock = new();
    private Guid? _id;

    /// <summary>
    /// Current correlation id when set; otherwise null.
    /// </summary>
    public Guid? Id { get { lock (_lock) return _id; } }

    /// <summary>
    /// Replace the active correlation id with a new value.
    /// </summary>
    /// <returns>The new correlation id.</returns>
    public Guid BeginNew()
    {
        var g = Guid.NewGuid();
        lock (_lock) _id = g;
        return g;
    }

    /// <summary>
    /// Clear the active correlation id.
    /// </summary>
    /// <returns>This method does not return a value.</returns>
    public void CancelCorrelation()
    {
        lock (_lock) _id = null;
    }
}

