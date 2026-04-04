namespace HelpDesk.Web.Services;

/// <summary>
/// Broadcast menu data changes to Blazor subscribers.
/// </summary>
public sealed class MenuRefreshNotifier : IMenuRefreshNotifier
{
    public event Action? MenuChanged;

    /// <summary>
    /// Raise <see cref="MenuChanged"/> so listeners reload navigation.
    /// </summary>
    /// <returns>This method does not return a value.</returns>
    public void NotifyMenuChanged() => MenuChanged?.Invoke();
}
