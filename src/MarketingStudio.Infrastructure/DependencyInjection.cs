using MarketingStudio.Application.Abstractions;
using MarketingStudio.Domain.Users;
using MarketingStudio.Infrastructure.Email;
using MarketingStudio.Infrastructure.Identity;
using MarketingStudio.Infrastructure.Persistence;
using MarketingStudio.Infrastructure.Tokens;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace MarketingStudio.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        MongoMappings.Register();

        services.Configure<MongoOptions>(configuration.GetSection(MongoOptions.Section));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.Configure<RefreshTokenOptions>(configuration.GetSection(RefreshTokenOptions.Section));
        services.Configure<AppLinkOptions>(configuration.GetSection(AppLinkOptions.Section));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.Section));

        // MongoDB
        services.AddSingleton<IMongoClient>(sp =>
            new MongoClient(sp.GetRequiredService<IOptions<MongoOptions>>().Value.ConnectionString));
        services.AddSingleton<MongoContext>();
        services.AddScoped<MongoSessionAccessor>();
        services.AddScoped<IUnitOfWork, MongoUnitOfWork>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IInviteRepository, InviteRepository>();
        services.Configure<PlanLimitOptions>(configuration.GetSection(PlanLimitOptions.Section));
        services.AddSingleton<IPlanLimits, ConfiguredPlanLimits>();
        services.AddScoped<IBrandRepository, BrandRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IMediaRepository, MediaRepository>();
        services.AddSingleton<IAuditLog, MongoAuditLog>();
        services.AddHostedService<MongoIndexInitializer>();
        services.AddHealthChecks().AddCheck<MongoHealthCheck>("mongodb", tags: ["ready"]);

        // Object storage (S3-compatible; SeaweedFS when self-hosted — Decision Register D31)
        services.Configure<Storage.StorageOptions>(configuration.GetSection(Storage.StorageOptions.Section));
        services.AddSingleton<Amazon.S3.IAmazonS3>(sp =>
            Storage.S3ClientFactory.Create(sp.GetRequiredService<IOptions<Storage.StorageOptions>>().Value));
        services.AddSingleton<IFileStorage, Storage.S3FileStorage>();
        services.AddSingleton<IMediaUrlSigner, Storage.DataProtectionMediaUrlSigner>();
        services.AddHostedService<Storage.StorageBucketInitializer>();

        // Data Protection (key ring on a persistent volume in production)
        var dataProtection = services.AddDataProtection().SetApplicationName("MarketingStudio");
        var keysPath = configuration.GetSection(DataProtectionSettings.Section).Get<DataProtectionSettings>()?.KeysPath;
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        // Identity (AUTH-1..4): password policy, lockout, token lifetimes
        services
            .AddIdentityCore<User>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 10;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredUniqueChars = 4;
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Tokens.EmailConfirmationTokenProvider = EmailConfirmationTokenProviderOptions.ProviderName;
            })
            .AddUserStore<MongoUserStore>()
            .AddPasswordValidator<CommonPasswordValidator>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<EmailConfirmationTokenProvider<User>>(EmailConfirmationTokenProviderOptions.ProviderName);

        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(1));
        services.AddScoped<IIdentityService, IdentityService>();

        // Tokens
        services.AddSingleton<JwtSigningKeyProvider>();
        services.AddSingleton<IAccessTokenService, AccessTokenService>();
        services.AddSingleton<IRefreshTokenService, RefreshTokenService>();

        // Email
        services.AddSingleton<IAppLinks, AppLinks>();
        var smtpHost = configuration.GetSection(EmailOptions.Section).Get<EmailOptions>()?.SmtpHost;
        if (string.IsNullOrWhiteSpace(smtpHost))
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }

        return services;
    }
}
