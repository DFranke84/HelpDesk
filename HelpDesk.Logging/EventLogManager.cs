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

    public EventLogManager(ILogger<EventLogManager> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _manualEmailLogger = loggerFactory.CreateLogger("ManualEmail");
    }

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

    public void LogTrace(Exception e, string message, params object?[] args) => _logger.LogTrace(e, message, args);
    public void LogTrace(string message, params object?[] args) => _logger.LogTrace(message, args);

    public void LogDebug(string message, params object?[] args) => _logger.LogDebug(message, args);
    public void LogDebug(Exception e, string message, params object?[] args) => _logger.LogDebug(e, message, args);

    public void LogInformation(string message, params object?[] args) => _logger.LogInformation(message, args);
    public void LogInformation(Exception e, string message, params object?[] args) => _logger.LogInformation(e, message, args);

    public void LogWarning(string message, params object?[] args) => _logger.LogWarning(message, args);
    public void LogWarning(Exception e, string message, params object?[] args) => _logger.LogWarning(e, message, args);

    public void LogError(string message, params object?[] args) => _logger.LogError(message, args);
    public void LogError(Exception e, string message, params object?[] args) => _logger.LogError(e, message, args);

    public void LogCritical(Exception e) => LogCritical(e, CriticalExceptionLogFormat, e.Message);
    public void LogCritical(Exception e, string message, params object?[] args) => _logger.LogCritical(e, message, args);

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

