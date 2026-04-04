namespace HelpDesk.Web.Services;

/// <summary>
/// Application-wide status line shown in the main layout footer (left side).
/// Inject <see cref="IStatusBarService"/> in pages or services and call <see cref="SetMessage"/> / <see cref="Clear"/>.
/// </summary>
public interface IStatusBarService
{
    string Message { get; }

    event Action? Changed;

    void SetMessage(string? message);

    void Clear();
}
