namespace MarketingStudio.Application.Abstractions;

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

/// <summary>Transactional email. Provider is still to be chosen (Decision Register D1).</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Builds links into the portal for emails.</summary>
public interface IAppLinks
{
    string SignIn();
    string VerifyEmail(string userId, string token);
    string ResetPassword(string email, string token);
    string ForgotPassword();
    string AcceptInvite(string token);
}
