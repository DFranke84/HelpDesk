namespace HelpDesk.Web.Services;

/// <summary>Raised when catalog data that feeds the sidebar (applications, pages, …) changes so the nav can reload without a full navigation.</summary>
public interface IMenuRefreshNotifier
{
    event Action? MenuChanged;

    void NotifyMenuChanged();
}
