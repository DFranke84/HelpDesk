namespace HelpDesk.Logging;

public interface IObservabilityService
{
    /// <summary>Active correlation id when <c>BeginNew()</c> was used; otherwise null. Add methods use a one-off id per row when this is null.</summary>
    Guid? CorrelationId { get; }
    Task AddAuditAsync(string actionType, string? loginId, string? message, string? details, CancellationToken cancellationToken = default);
    Task AddErrorAsync(string message, string? stackTrace, string severity, CancellationToken cancellationToken = default);
    Task AddLogAsync(string message, string level, CancellationToken cancellationToken = default);
}

