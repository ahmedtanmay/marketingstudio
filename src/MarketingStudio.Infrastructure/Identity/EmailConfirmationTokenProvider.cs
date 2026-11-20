using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketingStudio.Infrastructure.Identity;

/// <summary>Email-verification links live longer (3 days) than password-reset links (1 hour).</summary>
internal sealed class EmailConfirmationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public const string ProviderName = "EmailConfirmation";

    public EmailConfirmationTokenProviderOptions()
    {
        Name = "EmailConfirmationDataProtectorTokenProvider";
        TokenLifespan = TimeSpan.FromDays(3);
    }
}

internal sealed class EmailConfirmationTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<EmailConfirmationTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<TUser>> logger)
    : DataProtectorTokenProvider<TUser>(dataProtectionProvider, options, logger)
    where TUser : class;
