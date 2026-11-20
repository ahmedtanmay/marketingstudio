using MarketingStudio.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace MarketingStudio.Infrastructure.Identity;

/// <summary>
/// Rejects well-known passwords (AUTH-1: "not in a common-password list").
/// This built-in list is a starter set; before launch replace it with a large breached-password
/// list (e.g. the top 100k from public breach corpora, loaded from a file).
/// </summary>
internal sealed class CommonPasswordValidator : IPasswordValidator<User>
{
    public const string ErrorCode = "CommonPassword";
    public const string ErrorMessage = "This password is too common. Choose something harder to guess.";

    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "1234567890", "12345678910", "0123456789", "0987654321", "9876543210", "1111111111", "0000000000",
        "1234512345", "1122334455", "123456789a", "1234567890a", "abc1234567", "abcd123456", "abcdef1234",
        "qwertyuiop", "qwerty1234", "qwerty12345", "qwerty123456", "qwertyui12", "1q2w3e4r5t", "1q2w3e4r5t6y",
        "1qaz2wsx3edc", "zaq12wsxcde", "asdfghjkl1", "asdfghjkl;", "zxcvbnm123", "zxcvbnmasd", "aaaaaaaaaa",
        "password12", "password123", "password1234", "password!1", "password1!", "passw0rd123", "p@ssw0rd123",
        "p@ssword123", "p@ssw0rd!", "pass123456", "passpass12", "letmein123", "welcome123", "welcome1234",
        "iloveyou12", "iloveyou123", "loveyou123", "football123", "baseball123", "basketball", "sunshine123",
        "princess123", "dragon1234", "monkey1234", "shadow1234", "master1234", "superman123", "batman12345",
        "starwars123", "whatever123", "computer123", "internet123", "trustno1234", "hello12345", "freedom123",
        "charlie123", "michael123", "jennifer123", "liverpool1", "chelsea123", "arsenal123", "manchester",
        "december12", "september1", "admin12345", "admin123456", "administrator", "changeme123", "changeme!",
        "test123456", "testtest12", "marketing1", "marketing123", "company123", "business123", "summer2026",
        "winter2026", "spring2026", "autumn2026", "january2026", "welcome2026", "password2026", "qwerty2026",
    };

    public Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user, string? password)
    {
        if (password is not null && Common.Contains(password.Trim()))
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = ErrorCode,
                Description = ErrorMessage,
            }));
        }

        return Task.FromResult(IdentityResult.Success);
    }
}
