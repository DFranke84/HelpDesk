namespace HelpDesk.Web.Services;

/// <summary>Sends transactional email (password resets, notifications) via the configured SMTP profile.</summary>
public interface IEmailSender
{
    /// <summary>
    /// Send a single plain-text email.
    /// </summary>
    /// <param name="to">Recipient address.</param>
    /// <param name="subject">Message subject.</param>
    /// <param name="body">Message body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the message is sent or skipped.</returns>
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}
