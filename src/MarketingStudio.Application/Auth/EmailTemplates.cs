using System.Net;
using MarketingStudio.Application.Abstractions;

namespace MarketingStudio.Application.Auth;

internal static class EmailTemplates
{
    public static EmailMessage VerifyEmail(string to, string name, string link) => Build(
        to,
        "Verify your email for AI Marketing Studio",
        $"Hi {name},",
        "Please confirm your email address to connect channels and publish content.",
        "Verify email",
        link,
        "If you didn't create an account, you can ignore this email.");

    public static EmailMessage AccountAlreadyExists(string to, string signInLink, string resetLink) => Build(
        to,
        "Sign-up attempt for your AI Marketing Studio account",
        "Hi,",
        "Someone tried to create a new account with this email address, but you already have one. "
        + $"If this was you, sign in instead, or reset your password here: {resetLink}",
        "Sign in",
        signInLink,
        "If this wasn't you, no action is needed.");

    public static EmailMessage ResetPassword(string to, string name, string link) => Build(
        to,
        "Reset your AI Marketing Studio password",
        $"Hi {name},",
        "Use the link below to choose a new password. The link expires in 1 hour and can be used once.",
        "Reset password",
        link,
        "If you didn't ask to reset your password, you can ignore this email.");

    private static EmailMessage Build(
        string to, string subject, string greeting, string body, string action, string link, string footer)
    {
        static string e(string value) => WebUtility.HtmlEncode(value);
        var html =
            $"<p>{e(greeting)}</p><p>{e(body)}</p>"
            + $"<p><a href=\"{e(link)}\">{e(action)}</a></p><p style=\"color:#666\">{e(footer)}</p>";
        var text = $"{greeting}\n\n{body}\n\n{action}: {link}\n\n{footer}";
        return new EmailMessage(to, subject, html, text);
    }
}
