using NLog;

namespace HelpDesk.Logging;

/// <summary>Base logger implementation that writes audit, error, and log entries to the NLog file logger only (no database).</summary>
public sealed class LoggerService : ILoggerService
{
    private readonly ICorrelationContext _correlation;
    private readonly EventLogManager _log;

    /// <summary>
    /// Create file-only logger service.
    /// </summary>
    /// <param name="correlation">Correlation scope <see cref="ICorrelationContext"/>.</param>
    /// <param name="log">NLog bridge <see cref="EventLogManager"/>.</param>
    public LoggerService(ICorrelationContext correlation, EventLogManager log)
    {
        _correlation = correlation;
        _log = log;
    }

    public Guid? CorrelationId => _correlation.Id;

    private Guid CorrelationIdForThisEntry() => _correlation.Id ?? Guid.NewGuid();

    /// <summary>
    /// Write audit line to the NLog file logger.
    /// </summary>
    /// <param name="actionType">Audit action classification.</param>
    /// <param name="loginId">Subject login id when known.</param>
    /// <param name="message">Primary message text.</param>
    /// <param name="details">Additional detail text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task when the entry is written.</returns>
    public Task AddAuditAsync(string actionType, string? loginId, string? message, string? details,
        CancellationToken cancellationToken = default)
    {
        var corr = CorrelationIdForThisEntry();
        using (ScopeContext.PushProperty("activityCategory", "Audit"))
        using (ScopeContext.PushProperty("correlationId", corr))
        using (ScopeContext.PushProperty("loginId", loginId))
            _log.LogInformation("{ActionType} {Message} {Details}", actionType, message ?? "", details ?? "");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Write error line to the NLog file logger.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="stackTrace">Optional stack trace.</param>
    /// <param name="severity">Severity label for routing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task when the entry is written.</returns>
    public Task AddErrorAsync(string message, string? stackTrace, string severity,
        CancellationToken cancellationToken = default)
    {
        var corr = CorrelationIdForThisEntry();
        using (ScopeContext.PushProperty("activityCategory", "Error"))
        using (ScopeContext.PushProperty("correlationId", corr))
            _log.LogError("{Severity}: {Message}\n{StackTrace}", severity, message, stackTrace ?? "");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Write general log line to the NLog file logger.
    /// </summary>
    /// <param name="message">Log message.</param>
    /// <param name="level">Logical level name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task when the entry is written.</returns>
    public Task AddLogAsync(string message, string level, CancellationToken cancellationToken = default)
    {
        var corr = CorrelationIdForThisEntry();
        using (ScopeContext.PushProperty("activityCategory", "Log"))
        using (ScopeContext.PushProperty("correlationId", corr))
            _log.LogInformation("{Level}: {Message}", level, message);
        return Task.CompletedTask;
    }
}
