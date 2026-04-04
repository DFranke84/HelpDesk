using HelpDesk.Logging;
using HelpDesk.Models;
using HelpDesk.Models.Data;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace HelpDesk.Web.Services;

/// <summary>
/// Writes audit/error/log lines to the file logger and <b>also</b> inserts rows into the <c>NLOG</c> table via EF
/// so login/logoff and other observability events appear even when the NLog Database target or stored procedure is misconfigured.
/// </summary>
public sealed class WebObservabilityService : IObservabilityService
{
    private readonly ICorrelationContext _correlation;
    private readonly EventLogManager _log;
    private readonly HelpDeskDbContext _db;
    private readonly ILogger<WebObservabilityService> _logger;

    public WebObservabilityService(ICorrelationContext correlation, EventLogManager log, HelpDeskDbContext db, ILogger<WebObservabilityService> logger)
    {
        _correlation = correlation;
        _log = log;
        _db = db;
        _logger = logger;
    }

    public Guid? CorrelationId => _correlation.Id;

    private Guid CorrelationIdForThisEntry() => _correlation.Id ?? Guid.NewGuid();

    public async Task AddAuditAsync(string actionType, string? loginId, string? message, string? details,
        CancellationToken cancellationToken = default)
    {
        var corr = CorrelationIdForThisEntry();
        var text = $"{actionType} {message} {details}".Trim();
        using (ScopeContext.PushProperty("activityCategory", "Audit"))
        using (ScopeContext.PushProperty("correlationId", corr))
        using (ScopeContext.PushProperty("loginId", loginId))
            _log.LogInformation("{ActionType} {Message} {Details}", actionType, message ?? "", details ?? "");

        await TryInsertNLogAsync("Info", text, "Audit", corr, loginId, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddErrorAsync(string message, string? stackTrace, string severity,
        CancellationToken cancellationToken = default)
    {
        var corr = CorrelationIdForThisEntry();
        var text = $"{severity}: {message}";
        using (ScopeContext.PushProperty("activityCategory", "Error"))
        using (ScopeContext.PushProperty("correlationId", corr))
            _log.LogError("{Severity}: {Message}\n{StackTrace}", severity, message, stackTrace ?? "");

        await TryInsertNLogAsync("Error", text, "Error", corr, null, stackTrace, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddLogAsync(string message, string level, CancellationToken cancellationToken = default)
    {
        var corr = CorrelationIdForThisEntry();
        using (ScopeContext.PushProperty("activityCategory", "Log"))
        using (ScopeContext.PushProperty("correlationId", corr))
            _log.LogInformation("{Level}: {Message}", level, message);

        var nlogLevel = level.Equals("Error", StringComparison.OrdinalIgnoreCase) ? "Error" : "Info";
        await TryInsertNLogAsync(nlogLevel, $"{level}: {message}", "Log", corr, null, null, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task TryInsertNLogAsync(
        string level,
        string message,
        string? activityCategory,
        Guid? correlationId,
        string? loginId,
        string? exception,
        CancellationToken cancellationToken)
    {
        try
        {
            string? domainLogin = null;
            if (ScopeContext.TryGetProperty("domainLogin", out var d) && d is not null)
                domainLogin = d.ToString();

            _db.NLogEntries.Add(new NLogEntry
            {
                MachineName = Environment.MachineName,
                Logged = DateTime.UtcNow,
                Level = level,
                Message = message,
                Logger = "HelpDesk.Observability",
                Exception = exception,
                ActivityCategory = activityCategory,
                CorrelationId = correlationId,
                LoginId = loginId,
                DomainLogin = domainLogin
            });
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "NLog table insert failed (non-blocking)");
        }
    }
}
