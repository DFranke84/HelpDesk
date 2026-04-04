using Microsoft.Extensions.Logging;

namespace HelpDesk.Logging;

/// <summary>
/// Central logging wrapper for application instrumentation.
/// Every log method automatically pushes the NLog alert level (Trace, Debug, Info, Warn, Error, Fatal)
/// as the <c>activityCategory</c> scope property unless a custom value is provided by the caller
/// via method parameter or <see cref="BeginContext"/>.
/// </summary>
public sealed class EventLogManager
{
    private const string CriticalExceptionLogFormat = "Critical Exception Occurred! {0}";
    private const int MinElapsedTimeDecimals = 5;

    private readonly ILogger<EventLogManager> _logger;
    private readonly ILogger _manualEmailLogger;
    private readonly ILogger _fileLogger;
    private readonly ILogger _bothLogger;
    private readonly ICorrelationContext _correlation;

    /// <summary>
    /// Create event log manager with primary, manual-email, file, and both loggers.
    /// </summary>
    /// <param name="logger">Primary application logger <see cref="ILogger{EventLogManager}"/>.</param>
    /// <param name="loggerFactory">Factory for named loggers <see cref="ILoggerFactory"/>.</param>
    /// <param name="correlation">Correlation scope <see cref="ICorrelationContext"/>.</param>
    public EventLogManager(ILogger<EventLogManager> logger, ILoggerFactory loggerFactory, ICorrelationContext correlation)
    {
        _logger = logger;
        _manualEmailLogger = loggerFactory.CreateLogger("ManualEmail");
        _fileLogger = loggerFactory.CreateLogger("ManualFile");
        _bothLogger = loggerFactory.CreateLogger("ManualBoth");
        _correlation = correlation;
    }

    /// <summary>
    /// Active correlation id from the current scope, or null when no scope is active.
    /// </summary>
    public Guid? CorrelationId => _correlation.Id;

    /// <summary>
    /// Begin a logging scope with optional logger properties.
    /// When <paramref name="correlationId"/> is omitted the active <see cref="ICorrelationContext"/> id is used.
    /// Only non-null properties are added to the scope so outer-scope values are never shadowed with null.
    /// When <paramref name="activityCategory"/> is set it overrides the automatic NLog-level category
    /// for any log calls made inside the scope.
    /// </summary>
    /// <param name="correlationId">Optional correlation id (falls back to <see cref="ICorrelationContext"/>).</param>
    /// <param name="loginId">Optional login id.</param>
    /// <param name="activityCategory">Optional activity category (overrides the automatic NLog-level value).</param>
    /// <param name="returnCode">Optional return code.</param>
    /// <returns>Disposable that ends the scope.</returns>
    public IDisposable BeginContext(
        Guid? correlationId = null,
        string? loginId = null,
        string? activityCategory = null,
        int? returnCode = null)
    {
        var resolvedCorrelation = correlationId ?? _correlation.Id;

        var scope = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (resolvedCorrelation is not null) scope["correlationId"] = resolvedCorrelation;
        if (loginId is not null) scope["loginId"] = loginId;
        if (activityCategory is not null) scope["activityCategory"] = activityCategory;
        if (returnCode is not null) scope["returnCode"] = returnCode;

        if (scope.Count == 0) return NoopDisposable.Instance;
        return _logger.BeginScope(scope) ?? NoopDisposable.Instance;
    }

    /// <summary>
    /// Send an email by logging to the <c>ManualEmail</c> logger (routed to the normal mail target in NLog).
    /// Automatic log-based email is only for Fatal; use this for all other mail from the app.
    /// </summary>
    /// <param name="to">Recipient address.</param>
    /// <param name="subject">Email subject.</param>
    /// <param name="body">Email body.</param>
    /// <param name="activityCategory">Optional activity category (defaults to <c>Error</c>).</param>
    /// <param name="correlationId">Optional correlation id (falls back to <see cref="ICorrelationContext"/>).</param>
    /// <param name="loginId">Optional login id.</param>
    /// <param name="returnCode">Optional return code.</param>
    /// <returns>This method does not return a value.</returns>
    public void SendEmail(string to, string subject, string body, string? activityCategory = null,
        Guid? correlationId = null, string? loginId = null, int? returnCode = null)
    {
        var resolvedCorrelation = correlationId ?? _correlation.Id;

        var scope = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["mailTo"] = to,
            ["mailSubject"] = subject,
            ["mailBody"] = body
        };
        if (resolvedCorrelation is not null) scope["correlationId"] = resolvedCorrelation;
        if (loginId is not null) scope["loginId"] = loginId;
        if (returnCode is not null) scope["returnCode"] = returnCode;

        using var s = _manualEmailLogger.BeginScope(scope);
        using var c = PushCategory(LogLevel.Error, activityCategory);
        _manualEmailLogger.LogError("{Subject}", subject);
    }

    // ── Trace ───────────────────────────────────────────────────────

    /// <summary>
    /// Write trace log without exception.
    /// </summary>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogTrace(string message, params object?[] args)
        => LogCore(LogLevel.Trace, LogTarget.Database, null, null, message, args);

    /// <summary>
    /// Write trace log with exception.
    /// </summary>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogTrace(Exception e, string message, params object?[] args)
        => LogCore(LogLevel.Trace, LogTarget.Database, e, null, message, args);

    /// <summary>
    /// Write trace log to the specified target without exception.
    /// </summary>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogTrace(LogTarget target, string message, params object?[] args)
        => LogCore(LogLevel.Trace, target, null, null, message, args);

    /// <summary>
    /// Write trace log to the specified target with exception.
    /// </summary>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogTrace(LogTarget target, Exception e, string message, params object?[] args)
        => LogCore(LogLevel.Trace, target, e, null, message, args);

    /// <summary>
    /// Write trace log to the specified target with a custom activity category.
    /// </summary>
    /// <param name="activityCategory">Activity category (null falls back to <c>Trace</c>).</param>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogTrace(string? activityCategory, LogTarget target, string message, params object?[] args)
        => LogCore(LogLevel.Trace, target, null, activityCategory, message, args);

    /// <summary>
    /// Write trace log to the specified target with a custom activity category and exception.
    /// </summary>
    /// <param name="activityCategory">Activity category (null falls back to <c>Trace</c>).</param>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogTrace(string? activityCategory, LogTarget target, Exception e, string message, params object?[] args)
        => LogCore(LogLevel.Trace, target, e, activityCategory, message, args);

    // ── Debug ───────────────────────────────────────────────────────

    /// <summary>
    /// Write debug log without exception.
    /// </summary>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogDebug(string message, params object?[] args)
        => LogCore(LogLevel.Debug, LogTarget.Database, null, null, message, args);

    /// <summary>
    /// Write debug log with exception.
    /// </summary>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogDebug(Exception e, string message, params object?[] args)
        => LogCore(LogLevel.Debug, LogTarget.Database, e, null, message, args);

    /// <summary>
    /// Write debug log to the specified target without exception.
    /// </summary>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogDebug(LogTarget target, string message, params object?[] args)
        => LogCore(LogLevel.Debug, target, null, null, message, args);

    /// <summary>
    /// Write debug log to the specified target with exception.
    /// </summary>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogDebug(LogTarget target, Exception e, string message, params object?[] args)
        => LogCore(LogLevel.Debug, target, e, null, message, args);

    /// <summary>
    /// Write debug log to the specified target with a custom activity category.
    /// </summary>
    /// <param name="activityCategory">Activity category (null falls back to <c>Debug</c>).</param>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogDebug(string? activityCategory, LogTarget target, string message, params object?[] args)
        => LogCore(LogLevel.Debug, target, null, activityCategory, message, args);

    /// <summary>
    /// Write debug log to the specified target with a custom activity category and exception.
    /// </summary>
    /// <param name="activityCategory">Activity category (null falls back to <c>Debug</c>).</param>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogDebug(string? activityCategory, LogTarget target, Exception e, string message, params object?[] args)
        => LogCore(LogLevel.Debug, target, e, activityCategory, message, args);

    // ── Information ─────────────────────────────────────────────────

    /// <summary>
    /// Write information log without exception.
    /// </summary>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogInformation(string message, params object?[] args)
        => LogCore(LogLevel.Information, LogTarget.Database, null, null, message, args);

    /// <summary>
    /// Write information log with exception.
    /// </summary>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogInformation(Exception e, string message, params object?[] args)
        => LogCore(LogLevel.Information, LogTarget.Database, e, null, message, args);

    /// <summary>
    /// Write information log to the specified target without exception.
    /// </summary>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogInformation(LogTarget target, string message, params object?[] args)
        => LogCore(LogLevel.Information, target, null, null, message, args);

    /// <summary>
    /// Write information log to the specified target with exception.
    /// </summary>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogInformation(LogTarget target, Exception e, string message, params object?[] args)
        => LogCore(LogLevel.Information, target, e, null, message, args);

    /// <summary>
    /// Write information log to the specified target with a custom activity category.
    /// </summary>
    /// <param name="activityCategory">Activity category (null falls back to <c>Info</c>).</param>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogInformation(string? activityCategory, LogTarget target, string message, params object?[] args)
        => LogCore(LogLevel.Information, target, null, activityCategory, message, args);

    /// <summary>
    /// Write information log to the specified target with a custom activity category and exception.
    /// </summary>
    /// <param name="activityCategory">Activity category (null falls back to <c>Info</c>).</param>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogInformation(string? activityCategory, LogTarget target, Exception e, string message, params object?[] args)
        => LogCore(LogLevel.Information, target, e, activityCategory, message, args);

    // ── Warning ─────────────────────────────────────────────────────

    /// <summary>
    /// Write warning log without exception.
    /// </summary>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogWarning(string message, params object?[] args)
        => LogCore(LogLevel.Warning, LogTarget.Database, null, null, message, args);

    /// <summary>
    /// Write warning log with exception.
    /// </summary>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogWarning(Exception e, string message, params object?[] args)
        => LogCore(LogLevel.Warning, LogTarget.Database, e, null, message, args);

    /// <summary>
    /// Write warning log to the specified target without exception.
    /// </summary>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogWarning(LogTarget target, string message, params object?[] args)
        => LogCore(LogLevel.Warning, target, null, null, message, args);

    /// <summary>
    /// Write warning log to the specified target with exception.
    /// </summary>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogWarning(LogTarget target, Exception e, string message, params object?[] args)
        => LogCore(LogLevel.Warning, target, e, null, message, args);

    /// <summary>
    /// Write warning log to the specified target with a custom activity category.
    /// </summary>
    /// <param name="activityCategory">Activity category (null falls back to <c>Warn</c>).</param>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogWarning(string? activityCategory, LogTarget target, string message, params object?[] args)
        => LogCore(LogLevel.Warning, target, null, activityCategory, message, args);

    /// <summary>
    /// Write warning log to the specified target with a custom activity category and exception.
    /// </summary>
    /// <param name="activityCategory">Activity category (null falls back to <c>Warn</c>).</param>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogWarning(string? activityCategory, LogTarget target, Exception e, string message, params object?[] args)
        => LogCore(LogLevel.Warning, target, e, activityCategory, message, args);

    // ── Error ───────────────────────────────────────────────────────

    /// <summary>
    /// Write error log without exception.
    /// </summary>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogError(string message, params object?[] args)
        => LogCore(LogLevel.Error, LogTarget.Database, null, null, message, args);

    /// <summary>
    /// Write error log with exception.
    /// </summary>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogError(Exception e, string message, params object?[] args)
        => LogCore(LogLevel.Error, LogTarget.Database, e, null, message, args);

    /// <summary>
    /// Write error log to the specified target without exception.
    /// </summary>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogError(LogTarget target, string message, params object?[] args)
        => LogCore(LogLevel.Error, target, null, null, message, args);

    /// <summary>
    /// Write error log to the specified target with exception.
    /// </summary>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogError(LogTarget target, Exception e, string message, params object?[] args)
        => LogCore(LogLevel.Error, target, e, null, message, args);

    /// <summary>
    /// Write error log to the specified target with a custom activity category.
    /// </summary>
    /// <param name="activityCategory">Activity category (null falls back to <c>Error</c>).</param>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogError(string? activityCategory, LogTarget target, string message, params object?[] args)
        => LogCore(LogLevel.Error, target, null, activityCategory, message, args);

    /// <summary>
    /// Write error log to the specified target with a custom activity category and exception.
    /// </summary>
    /// <param name="activityCategory">Activity category (null falls back to <c>Error</c>).</param>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogError(string? activityCategory, LogTarget target, Exception e, string message, params object?[] args)
        => LogCore(LogLevel.Error, target, e, activityCategory, message, args);

    // ── Critical ────────────────────────────────────────────────────

    /// <summary>
    /// Write critical log using the default critical format.
    /// </summary>
    /// <param name="e">Exception to log.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogCritical(Exception e)
        => LogCritical(e, CriticalExceptionLogFormat, e.Message);

    /// <summary>
    /// Write critical log with a custom template.
    /// </summary>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogCritical(Exception e, string message, params object?[] args)
        => LogCore(LogLevel.Critical, LogTarget.Database, e, null, message, args);

    /// <summary>
    /// Write critical log to the specified target using the default critical format.
    /// </summary>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="e">Exception to log.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogCritical(LogTarget target, Exception e)
        => LogCritical(target, e, CriticalExceptionLogFormat, e.Message);

    /// <summary>
    /// Write critical log to the specified target with a custom template.
    /// </summary>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogCritical(LogTarget target, Exception e, string message, params object?[] args)
        => LogCore(LogLevel.Critical, target, e, null, message, args);

    /// <summary>
    /// Write critical log with a custom activity category using the default format.
    /// </summary>
    /// <param name="activityCategory">Activity category (null falls back to <c>Fatal</c>).</param>
    /// <param name="e">Exception to log.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogCritical(string? activityCategory, Exception e)
        => LogCritical(activityCategory, e, CriticalExceptionLogFormat, e.Message);

    /// <summary>
    /// Write critical log with a custom activity category and template.
    /// </summary>
    /// <param name="activityCategory">Activity category (null falls back to <c>Fatal</c>).</param>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogCritical(string? activityCategory, Exception e, string message, params object?[] args)
        => LogCore(LogLevel.Critical, LogTarget.Database, e, activityCategory, message, args);

    /// <summary>
    /// Write critical log to the specified target with a custom activity category using the default format.
    /// </summary>
    /// <param name="activityCategory">Activity category (null falls back to <c>Fatal</c>).</param>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="e">Exception to log.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogCritical(string? activityCategory, LogTarget target, Exception e)
        => LogCritical(activityCategory, target, e, CriticalExceptionLogFormat, e.Message);

    /// <summary>
    /// Write critical log to the specified target with a custom activity category and template.
    /// </summary>
    /// <param name="activityCategory">Activity category (null falls back to <c>Fatal</c>).</param>
    /// <param name="target">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogCritical(string? activityCategory, LogTarget target, Exception e, string message, params object?[] args)
        => LogCore(LogLevel.Critical, target, e, activityCategory, message, args);

    // ── Elapsed-time activity log ───────────────────────────────────

    /// <summary>
    /// Write elapsed-time activity log with structured scope properties.
    /// </summary>
    /// <param name="activityName">Activity name for the log message.</param>
    /// <param name="activityDescription">Human-readable description.</param>
    /// <param name="activityTime">Measured duration.</param>
    /// <param name="numIterations">Iteration count for throughput messages.</param>
    /// <param name="logLevel">Severity level for the entry.</param>
    /// <param name="logTarget">Target routing <see cref="LogTarget"/>.</param>
    /// <param name="activityCategory">Optional activity category (null falls back to the NLog level name).</param>
    /// <param name="correlationId">Optional correlation id (falls back to <see cref="ICorrelationContext"/>).</param>
    /// <param name="loginId">Optional login id.</param>
    /// <param name="returnCode">Optional return code.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogTimeElapsed(
        string activityName,
        string activityDescription,
        TimeSpan activityTime,
        int numIterations = 1,
        LogLevel logLevel = LogLevel.Information,
        LogTarget logTarget = LogTarget.Database,
        string? activityCategory = null,
        Guid? correlationId = null,
        string? loginId = null,
        int? returnCode = null)
    {
        var elapsedTime = decimal.Round((decimal)activityTime.TotalSeconds, MinElapsedTimeDecimals);
        var resolvedCorrelation = correlationId ?? _correlation.Id;

        var scope = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["elapsedTime"] = elapsedTime,
            ["numIterations"] = numIterations
        };
        if (resolvedCorrelation is not null) scope["correlationId"] = resolvedCorrelation;
        if (loginId is not null) scope["loginId"] = loginId;
        if (returnCode is not null) scope["returnCode"] = returnCode;

        var logger = GetLogger(logTarget);
        using var s = logger.BeginScope(scope) ?? NoopDisposable.Instance;
        using var c = PushCategory(logLevel, activityCategory);

        if (numIterations > 0)
        {
            logger.Log(logLevel,
                "{ActivityName} - {activityDescription}, {numIterations} iterations completed in {elapsedTime} seconds",
                activityName, activityDescription, numIterations, elapsedTime);
        }
        else
        {
            logger.Log(logLevel, "{ActivityName} - {activityDescription}, Completed in {elapsedTime} seconds",
                activityName, activityDescription, elapsedTime);
        }
    }

    // ── Internals ───────────────────────────────────────────────────

    private void LogCore(LogLevel level, LogTarget target, Exception? exception, string? activityCategory, string message, object?[] args)
    {
        using var _ = PushCategory(level, activityCategory);
        var logger = GetLogger(target);
        if (exception is not null)
            logger.Log(level, exception, message, args);
        else
            logger.Log(level, message, args);
    }

    private ILogger GetLogger(LogTarget target) => target switch
    {
        LogTarget.File => _fileLogger,
        LogTarget.Both => _bothLogger,
        _ => _logger
    };

    /// <summary>
    /// Push <c>activityCategory</c> to the NLog scope.
    /// Priority: explicit <paramref name="activityCategory"/> &gt; value already in scope (from <see cref="BeginContext"/>) &gt; auto-derived from <paramref name="level"/>.
    /// </summary>
    private static IDisposable PushCategory(LogLevel level, string? activityCategory)
    {
        if (activityCategory is not null)
            return NLog.ScopeContext.PushProperty("activityCategory", activityCategory);

        if (NLog.ScopeContext.TryGetProperty("activityCategory", out var existing) && existing is not null)
            return NoopDisposable.Instance;

        return NLog.ScopeContext.PushProperty("activityCategory", MapLevelToCategory(level));
    }

    private static string MapLevelToCategory(LogLevel level) => level switch
    {
        LogLevel.Trace => "Trace",
        LogLevel.Debug => "Debug",
        LogLevel.Information => "Info",
        LogLevel.Warning => "Warn",
        LogLevel.Error => "Error",
        LogLevel.Critical => "Fatal",
        _ => "Info"
    };

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose()
        {
        }
    }
}
