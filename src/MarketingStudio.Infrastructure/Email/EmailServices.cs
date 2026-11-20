using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using MarketingStudio.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketingStudio.Infrastructure.Email;

/// <summary>
/// SMTP sender — works with Mailpit locally and with Postmark/SES SMTP endpoints once the provider is
/// chosen (Decision Register D1). An HTTP API sender can replace it behind <see cref="IEmailSender"/>.
/// </summary>
internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var mail = new MailMessage
        {
            From = new MailAddress(settings.From),
            Subject = message.Subject,
            Body = message.TextBody,
        };
        mail.To.Add(message.To);
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, null, MediaTypeNames.Text.Html));

        using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort) { EnableSsl = settings.UseSsl };
        if (!string.IsNullOrEmpty(settings.Username))
        {
            client.Credentials = new NetworkCredential(settings.Username, settings.Password);
        }

        await client.SendMailAsync(mail, cancellationToken);
    }
}

/// <summary>Used when no SMTP host is configured. Logs recipient and subject only — never bodies, links or tokens.</summary>
internal sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation("Email not sent (no SMTP configured): subject {Subject}", message.Subject);
        return Task.CompletedTask;
    }
}

internal sealed class AppLinks(IOptions<AppLinkOptions> options) : IAppLinks
{
    private string Base => options.Value.PortalBaseUrl.TrimEnd('/');

    public string SignIn() => $"{Base}/login";

    public string ForgotPassword() => $"{Base}/forgot-password";

    public string VerifyEmail(string userId, string token) =>
        $"{Base}/verify-email?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";

    public string ResetPassword(string email, string token) =>
        $"{Base}/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
}
