namespace HelpDesk.Logging;

/// <summary>Writes structured logger entries (audit, error, log) to the NLog database target.</summary>
public interface ILoggerService
{
    /// <summary>Active correlation id when correlation scope started; otherwise null.</summary>
    Guid? CorrelationId { get; }

    /// <summary>
    /// Record an auditable action (login, logout, data change, etc.).
    /// </summary>
    /// <param name="actionType">Audit action classification.</param>
    /// <param name="loginId">Subject login id when known.</param>
    /// <param name="message">Primary message text.</param>
    /// <param name="details">Additional detail text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the entry is written.</returns>
    Task AddAuditAsync(string actionType, string? loginId, string? message, string? details, CancellationToken cancellationToken = default);

    /// <summary>
    /// Record an application error with optional stack trace.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="stackTrace">Optional stack trace.</param>
    /// <param name="severity">Severity label for routing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the entry is written.</returns>
    Task AddErrorAsync(string message, string? stackTrace, string severity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Record a general log entry at the given NLog level.
    /// </summary>
    /// <param name="message">Log message.</param>
    /// <param name="level">Logical level name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the entry is written.</returns>
    Task AddLogAsync(string message, string level, CancellationToken cancellationToken = default);
}
