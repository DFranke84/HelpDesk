namespace HelpDesk.Web.Services;

public sealed class StatusBarService : IStatusBarService
{
    public string Message { get; private set; } = "";

    public event Action? Changed;

    public void SetMessage(string? message)
    {
        Message = message ?? "";
        Changed?.Invoke();
    }

    public void Clear() => SetMessage(null);
}
