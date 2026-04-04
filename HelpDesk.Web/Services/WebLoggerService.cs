using HelpDesk.Logging;
using HelpDesk.Models;
using HelpDesk.Models.Data;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace HelpDesk.Web.Services;

/// <summary>
/// Writes audit/error/log lines to the file logger and <b>also</b> inserts rows into the <c>NLOG</c> table via EF
/// so login/logoff and other logger events appear even when the NLog Database target or stored procedure is misconfigured.
/// </summary>
public sealed class WebLoggerService : ILoggerService
{
    private readonly ICorrelationContext _correlation;
    private readonly EventLogManager _log;
    private readonly IDbContextFactory<HelpDeskDbContext> _dbFactory;
    private readonly ILogger<WebLoggerService> _logger;

    /// <summary>
    /// Create web logger service with database mirroring.
    /// </summary>
    /// <param name="correlation">Correlation scope <see cref="ICorrelationContext"/>.</param>
    /// <param name="log">NLog bridge <see cref="EventLogManager"/>.</param>
    /// <param name="dbFactory">Help Desk context factory <see cref="IDbContextFactory{TContext}"/>.</param>
    /// <param name="logger">Diagnostic logger <see cref="ILogger{WebLoggerService}"/>.</param>
    public WebLoggerService(ICorrelationContext correlation, EventLogManager log,
        IDbContextFactory<HelpDeskDbContext> dbFactory, ILogger<WebLoggerService> logger)
    {
        _correlation = correlation;
        _log = log;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public Guid? CorrelationId => _correlation.Id;

    private Guid CorrelationIdForThisEntry() => _correlation.Id ?? Guid.NewGuid();

    /// <summary>
    /// Write audit line to the log file and NLog table.
    /// </summary>
    /// <param name="actionType">Audit action classification.</param>
    /// <param name="loginId">Subject login id when known.</param>
    /// <param name="message">Primary message text.</param>
    /// <param name="details">Additional detail text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the entry is written.</returns>
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

    /// <summary>
    /// Write error line to the log file and NLog table.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="stackTrace">Optional stack trace.</param>
    /// <param name="severity">Severity label for routing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the entry is written.</returns>
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

    /// <summary>
    /// Write general log line to the log file and NLog table.
    /// </summary>
    /// <param name="message">Log message.</param>
    /// <param name="level">Logical level name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the entry is written.</returns>
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
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            string? domainLogin = null;
            if (ScopeContext.TryGetProperty("domainLogin", out var d) && d is not null)
                domainLogin = d.ToString();

            db.NLogEntries.Add(new NLogEntry
            {
                MachineName = Environment.MachineName,
                Logged = DateTime.UtcNow,
                Level = level,
                Message = message,
                Logger = "HelpDesk.Logger",
                Exception = exception,
                ActivityCategory = activityCategory,
                CorrelationId = correlationId,
                LoginId = loginId,
                DomainLogin = domainLogin
            });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NLog table insert failed (non-blocking)");
        }
    }
}
