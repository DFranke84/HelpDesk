using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace HelpDesk.Web.Services;

/// <summary>Shows a modal with the failure reason when <see cref="DbContext.SaveChangesAsync"/> throws.</summary>
public static class DbSaveErrorUi
{
    public static async Task<bool> TrySaveChangesAsync(this DbContext db, IJSRuntime js, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            var message = ex.GetBaseException().Message;
            try
            {
                await js.InvokeVoidAsync("helpdeskDiag.showText", "Operation failed", message).ConfigureAwait(false);
            }
            catch
            {
                // JS/circuit unavailable
            }
            return false;
        }
    }
}
