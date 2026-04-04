namespace HelpDesk.Web.Services;

/// <summary>
/// Application footer status line state for the main layout.
/// </summary>
public sealed class StatusBarService : IStatusBarService
{
    public string Message { get; private set; } = "";

    public event Action? Changed;

    /// <summary>
    /// Set footer message text and notify listeners.
    /// </summary>
    /// <param name="message">Status text, or null to clear.</param>
    /// <returns>This method does not return a value.</returns>
    public void SetMessage(string? message)
    {
        Message = message ?? "";
        Changed?.Invoke();
    }

    /// <summary>
    /// Clear the footer message.
    /// </summary>
    /// <returns>This method does not return a value.</returns>
    public void Clear() => SetMessage(null);
}
