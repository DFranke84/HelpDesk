using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Web.Services;

/// <summary>
/// Logs exceptions caught by Blazor <see cref="ErrorBoundary"/> to the standard logging pipeline (console, NLog, etc.).
/// </summary>
public sealed class HelpDeskErrorBoundaryLogger : IErrorBoundaryLogger
{
    private readonly ILogger<HelpDeskErrorBoundaryLogger> _log;

    public HelpDeskErrorBoundaryLogger(ILogger<HelpDeskErrorBoundaryLogger> log)
    {
        _log = log;
    }

    /// <inheritdoc />
    public ValueTask LogErrorAsync(Exception exception)
    {
        _log.LogError(exception, "Blazor ErrorBoundary caught an exception.");
        return ValueTask.CompletedTask;
    }
}
