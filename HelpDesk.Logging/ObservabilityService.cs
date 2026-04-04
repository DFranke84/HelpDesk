using NLog;

namespace HelpDesk.Logging;

public class ObservabilityService : IObservabilityService
{
    private readonly ICorrelationContext _correlation;
    private readonly EventLogManager _log;

    public ObservabilityService(ICorrelationContext correlation, EventLogManager log)
    {
        _correlation = correlation;
        _log = log;
    }

    public Guid? CorrelationId => _correlation.Id;

    private Guid CorrelationIdForThisEntry() => _correlation.Id ?? Guid.NewGuid();

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

    public Task AddErrorAsync(string message, string? stackTrace, string severity,
        CancellationToken cancellationToken = default)
    {
        var corr = CorrelationIdForThisEntry();
        using (ScopeContext.PushProperty("activityCategory", "Error"))
        using (ScopeContext.PushProperty("correlationId", corr))
            _log.LogError("{Severity}: {Message}\n{StackTrace}", severity, message, stackTrace ?? "");
        return Task.CompletedTask;
    }

    public Task AddLogAsync(string message, string level, CancellationToken cancellationToken = default)
    {
        var corr = CorrelationIdForThisEntry();
        using (ScopeContext.PushProperty("activityCategory", "Log"))
        using (ScopeContext.PushProperty("correlationId", corr))
            _log.LogInformation("{Level}: {Message}", level, message);
        return Task.CompletedTask;
    }
}

