using Microsoft.Extensions.Logging;

namespace HelpDesk.Logging;

/// <summary>
/// Central logging wrapper for application instrumentation.
/// </summary>
public sealed class EventLogManager
{
    private const string CriticalExceptionLogFormat = "Critical Exception Occurred! {0}";
    private const int MinElapsedTimeDecimals = 5;

    private readonly ILogger<EventLogManager> _logger;
    private readonly ILogger _manualEmailLogger;

    /// <summary>
    /// Create event log manager with primary and manual-email loggers.
    /// </summary>
    /// <param name="logger">Primary application logger <see cref="ILogger{EventLogManager}"/>.</param>
    /// <param name="loggerFactory">Factory for named loggers <see cref="ILoggerFactory"/>.</param>
    public EventLogManager(ILogger<EventLogManager> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _manualEmailLogger = loggerFactory.CreateLogger("ManualEmail");
    }

    /// <summary>
    /// Begin a logging scope with optional observability properties.
    /// </summary>
    /// <param name="activityCategory">Optional activity category label.</param>
    /// <param name="correlationId">Optional correlation id.</param>
    /// <param name="loginId">Optional login id.</param>
    /// <param name="returnCode">Optional return code.</param>
    /// <returns>Disposable that ends the scope.</returns>
    public IDisposable BeginContext(
        string? activityCategory = null,
        Guid? correlationId = null,
        string? loginId = null,
        int? returnCode = null)
    {
        var scope = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["activityCategory"] = activityCategory,
            ["correlationId"] = correlationId,
            ["loginId"] = loginId,
            ["returnCode"] = returnCode
        };
        return _logger.BeginScope(scope) ?? NoopDisposable.Instance;
    }

    /// <summary>
    /// Send an email by logging to the <c>ManualEmail</c> logger (routed to the normal mail target in NLog).
    /// Automatic log-based email is only for Fatal; use this for all other mail from the app.
    /// </summary>
    /// <summary>
    /// Route a manual email by logging to the ManualEmail logger.
    /// </summary>
    /// <param name="to">Recipient address.</param>
    /// <param name="subject">Email subject.</param>
    /// <param name="body">Email body.</param>
    /// <param name="activityCategory">Optional activity category.</param>
    /// <param name="correlationId">Optional correlation id.</param>
    /// <param name="loginId">Optional login id.</param>
    /// <param name="returnCode">Optional return code.</param>
    /// <returns>This method does not return a value.</returns>
    public void SendEmail(string to, string subject, string body, string? activityCategory = null, Guid? correlationId = null,
        string? loginId = null, int? returnCode = null)
    {
        using var _ = _manualEmailLogger.BeginScope(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["mailTo"] = to,
            ["mailSubject"] = subject,
            ["mailBody"] = body,
            ["activityCategory"] = activityCategory,
            ["correlationId"] = correlationId,
            ["loginId"] = loginId,
            ["returnCode"] = returnCode
        });

        _manualEmailLogger.LogError("{Subject}", subject);
    }

    /// <summary>
    /// Write trace log with exception.
    /// </summary>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogTrace(Exception e, string message, params object?[] args) => _logger.LogTrace(e, message, args);

    /// <summary>
    /// Write trace log without exception.
    /// </summary>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogTrace(string message, params object?[] args) => _logger.LogTrace(message, args);

    /// <summary>
    /// Write debug log without exception.
    /// </summary>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogDebug(string message, params object?[] args) => _logger.LogDebug(message, args);

    /// <summary>
    /// Write debug log with exception.
    /// </summary>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogDebug(Exception e, string message, params object?[] args) => _logger.LogDebug(e, message, args);

    /// <summary>
    /// Write information log without exception.
    /// </summary>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogInformation(string message, params object?[] args) => _logger.LogInformation(message, args);

    /// <summary>
    /// Write information log with exception.
    /// </summary>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogInformation(Exception e, string message, params object?[] args) => _logger.LogInformation(e, message, args);

    /// <summary>
    /// Write warning log without exception.
    /// </summary>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogWarning(string message, params object?[] args) => _logger.LogWarning(message, args);

    /// <summary>
    /// Write warning log with exception.
    /// </summary>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogWarning(Exception e, string message, params object?[] args) => _logger.LogWarning(e, message, args);

    /// <summary>
    /// Write error log without exception.
    /// </summary>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogError(string message, params object?[] args) => _logger.LogError(message, args);

    /// <summary>
    /// Write error log with exception.
    /// </summary>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogError(Exception e, string message, params object?[] args) => _logger.LogError(e, message, args);

    /// <summary>
    /// Write critical log using the default critical format.
    /// </summary>
    /// <param name="e">Exception to log.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogCritical(Exception e) => LogCritical(e, CriticalExceptionLogFormat, e.Message);

    /// <summary>
    /// Write critical log with a custom template.
    /// </summary>
    /// <param name="e">Exception to include.</param>
    /// <param name="message">Message template.</param>
    /// <param name="args">Template arguments.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogCritical(Exception e, string message, params object?[] args) => _logger.LogCritical(e, message, args);

    /// <summary>
    /// Write elapsed-time activity log with structured scope properties.
    /// </summary>
    /// <param name="activityCategory">Activity category label.</param>
    /// <param name="activityDescription">Human-readable description.</param>
    /// <param name="activityTime">Measured duration.</param>
    /// <param name="numIterations">Iteration count for throughput messages.</param>
    /// <param name="logLevel">Severity level for the entry.</param>
    /// <param name="correlationId">Optional correlation id.</param>
    /// <param name="loginId">Optional login id.</param>
    /// <param name="returnCode">Optional return code.</param>
    /// <returns>This method does not return a value.</returns>
    public void LogTimeElapsed(
        string activityCategory,
        string activityDescription,
        TimeSpan activityTime,
        int numIterations = 1,
        LogLevel logLevel = LogLevel.Information,
        Guid? correlationId = null,
        string? loginId = null,
        int? returnCode = null)
    {
        var elapsedTime = decimal.Round((decimal)activityTime.TotalSeconds, MinElapsedTimeDecimals);
        using var _ = _logger.BeginScope(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["activityCategory"] = activityCategory,
            ["correlationId"] = correlationId,
            ["loginId"] = loginId,
            ["returnCode"] = returnCode,
            ["elapsedTime"] = elapsedTime,
            ["numIterations"] = numIterations
        }) ?? NoopDisposable.Instance;

        if (numIterations > 0)
        {
            _logger.Log(logLevel,
                "{ActivityCategory} - {activityDescription}, {numIterations} iterations completed in {elapsedTime} seconds",
                activityCategory, activityDescription, numIterations, elapsedTime);
        }
        else
        {
            _logger.Log(logLevel, "{ActivityCategory} - {activityDescription}, Completed in {elapsedTime} seconds",
                activityCategory, activityDescription, elapsedTime);
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose()
        {
        }
    }
}

