using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace HelpDesk.Web.Services;

/// <summary>Shows a modal with the failure reason when <see cref="DbContext.SaveChangesAsync"/> throws.</summary>
public static class DbSaveErrorUi
{
    private static readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(b => b.AddConsole());
    private static readonly ILogger _logger = _loggerFactory.CreateLogger(typeof(DbSaveErrorUi));

    /// <summary>
    /// Save changes and show a generic error modal on failure. Full exception details are logged server-side only.
    /// </summary>
    /// <param name="db">The <see cref="DbContext"/> to save.</param>
    /// <param name="js">The <see cref="IJSRuntime"/> used to display the modal.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if save succeeded; false if an error was shown.</returns>
    public static async Task<bool> TrySaveChangesAsync(this DbContext db, IJSRuntime js, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SaveChangesAsync failed");
            try
            {
                await js.InvokeVoidAsync("helpdeskDiag.showText", "Operation failed",
                    "The operation could not be completed. Please try again or contact an administrator if the problem persists.")
                    .ConfigureAwait(false);
            }
            catch
            {
                // JS/circuit unavailable
            }
            return false;
        }
    }
}
