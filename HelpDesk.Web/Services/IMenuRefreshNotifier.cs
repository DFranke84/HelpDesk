namespace HelpDesk.Web.Services;

/// <summary>
/// Raised when catalog data that feeds the sidebar (applications, pages, etc.) changes
/// so the nav can reload without a full navigation.
/// </summary>
/// <remarks>
/// Registered as singleton. Subscribers (typically scoped Blazor components) <b>must</b>
/// unsubscribe in <c>Dispose</c> to avoid pinning the component in memory for the
/// lifetime of the application process.
/// </remarks>
public interface IMenuRefreshNotifier
{
    event Action? MenuChanged;

    void NotifyMenuChanged();
}
