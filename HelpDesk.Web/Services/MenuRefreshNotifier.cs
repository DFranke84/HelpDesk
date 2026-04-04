namespace HelpDesk.Web.Services;

public sealed class MenuRefreshNotifier : IMenuRefreshNotifier
{
    public event Action? MenuChanged;

    public void NotifyMenuChanged() => MenuChanged?.Invoke();
}
