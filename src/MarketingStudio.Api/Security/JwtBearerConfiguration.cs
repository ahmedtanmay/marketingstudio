using MarketingStudio.Infrastructure;
using MarketingStudio.Infrastructure.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MarketingStudio.Api.Security;

/// <summary>Validates access tokens with the same key and settings used to issue them.</summary>
internal sealed class JwtBearerConfiguration(JwtSigningKeyProvider keys, IOptions<JwtOptions> jwtOptions)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        var settings = jwtOptions.Value;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = settings.Issuer,
            ValidAudience = settings.Audience,
            IssuerSigningKey = keys.Key,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            NameClaimType = ClaimNames.Subject,
            RoleClaimType = ClaimNames.Role,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    }

    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);
}
