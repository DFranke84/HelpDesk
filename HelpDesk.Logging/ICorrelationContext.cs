namespace HelpDesk.Logging;

public interface ICorrelationContext
{
    /// <summary>
    /// Current correlation id for Audit, Log, and Error rows. <see langword="null"/> until
    /// <see cref="BeginNew"/> is called.
    /// </summary>
    Guid? Id { get; }

    /// <summary>
    /// Starts a new correlation id (or replaces the current one). Call when you want logger
    /// entries to share an id for a logical sequence of work.
    /// </summary>
    /// <returns>The new correlation id.</returns>
    Guid BeginNew();

    /// <summary>
    /// Drop the active correlation id so later logger calls allocate per-entry ids until <see cref="BeginNew"/> runs again.
    /// </summary>
    /// <returns>This method does not return a value.</returns>
    void CancelCorrelation();
}

