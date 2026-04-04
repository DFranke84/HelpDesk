namespace HelpDesk.Logging;

public interface ICorrelationContext
{
    /// <summary>
    /// Current correlation id for Audit, Log, and Error rows. <see langword="null"/> until
    /// <see cref="BeginNew"/> is called.
    /// </summary>
    Guid? Id { get; }

    /// <summary>
    /// Starts a new correlation id (or replaces the current one). Call when you want observability
    /// entries to share an id for a logical sequence of work.
    /// </summary>
    /// <returns>The new correlation id.</returns>
    Guid BeginNew();

    /// <summary>
    /// Drops the active correlation id. Later <c>AddAuditAsync</c>, <c>AddLogAsync</c>, and <c>AddErrorAsync</c>
    /// calls behave as if no correlation was started (each gets its own id per call) until <see cref="BeginNew"/> again.
    /// </summary>
    void CancelCorrelation();
}

