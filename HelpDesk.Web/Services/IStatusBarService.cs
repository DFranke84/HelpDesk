namespace HelpDesk.Web.Services;

/// <summary>
/// Application-wide status line shown in the main layout footer (left side).
/// Inject <see cref="IStatusBarService"/> in pages or services and call <see cref="SetMessage"/> / <see cref="Clear"/>.
/// </summary>
public interface IStatusBarService
{
    string Message { get; }

    event Action? Changed;

    /// <summary>
    /// Set footer status text and notify listeners.
    /// </summary>
    /// <param name="message">Status text, or null to clear.</param>
    /// <returns>This method does not return a value.</returns>
    void SetMessage(string? message);

    /// <summary>
    /// Clear the footer message.
    /// </summary>
    /// <returns>This method does not return a value.</returns>
    void Clear();
}
