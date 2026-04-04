using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace HelpDesk.Web.Services;

/// <summary>Sends transactional emails via SMTP using MailKit, configured through <c>Smtp:*</c> settings.</summary>
public sealed class EmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(IConfiguration configuration, ILogger<EmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        var host = _configuration["Smtp:Host"];
        if (!int.TryParse(_configuration["Smtp:Port"], out var port))
            port = 25;
        var from = _configuration["Smtp:From"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
        {
            _logger.LogWarning("SMTP not configured; email skipped. Subject: {Subject}", subject);
            return;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(from));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };

            using var client = new SmtpClient();
            var socketOptions = ResolveSecureSocketOptions();
            await client.ConnectAsync(host, port, socketOptions, ct).ConfigureAwait(false);
            var smtpUser = _configuration["Smtp:User"];
            var smtpPass = _configuration["Smtp:Password"];
            if (!string.IsNullOrEmpty(smtpUser))
                await client.AuthenticateAsync(smtpUser, smtpPass ?? "", ct).ConfigureAwait(false);

            await client.SendAsync(message, ct).ConfigureAwait(false);
            await client.DisconnectAsync(true, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to send email to {To}, Subject: {Subject}", to, subject);
            throw;
        }
    }

    private SecureSocketOptions ResolveSecureSocketOptions()
    {
        var raw = _configuration["Smtp:SecureSocketOption"];
        if (!string.IsNullOrWhiteSpace(raw) &&
            Enum.TryParse<SecureSocketOptions>(raw, ignoreCase: true, out var parsed))
            return parsed;

        var enableSsl = _configuration.GetValue("Smtp:EnableSsl", false);
        return enableSsl ? SecureSocketOptions.Auto : SecureSocketOptions.None;
    }
}
