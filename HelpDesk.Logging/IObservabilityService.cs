namespace HelpDesk.Logging;

/// <summary>Writes structured observability entries (audit, error, log) to the NLog database target.</summary>
public interface IObservabilityService
{
    /// <summary>Active correlation id when <c>BeginNew()</c> was used; otherwise null. Add methods use a one-off id per row when this is null.</summary>
    Guid? CorrelationId { get; }

    /// <summary>Record an auditable action (login, logout, data change, etc.).</summary>
    Task AddAuditAsync(string actionType, string? loginId, string? message, string? details, CancellationToken cancellationToken = default);

    /// <summary>Record an application error with optional stack trace.</summary>
    Task AddErrorAsync(string message, string? stackTrace, string severity, CancellationToken cancellationToken = default);

    /// <summary>Record a general log entry at the given NLog level.</summary>
    Task AddLogAsync(string message, string level, CancellationToken cancellationToken = default);
}

