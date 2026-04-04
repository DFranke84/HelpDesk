namespace HelpDesk.Web.Services;

/// <summary>Sends transactional email (password resets, notifications) via the configured SMTP profile.</summary>
public interface IEmailSender
{
    /// <summary>Send a single plain-text email.</summary>
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}
