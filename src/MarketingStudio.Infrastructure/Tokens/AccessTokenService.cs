using System.Security.Claims;
using MarketingStudio.Application.Abstractions;
using MarketingStudio.Domain.Users;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MarketingStudio.Infrastructure.Tokens;

public static class ClaimNames
{
    public const string Subject = JwtRegisteredClaimNames.Sub;
    public const string TenantId = "tid";
    public const string Role = "role";
    public const string TokenId = JwtRegisteredClaimNames.Jti;
}

/// <summary>Issues short-lived (15 min) ES256 access tokens. Claims: sub, tid, role(s), jti — nothing sensitive.</summary>
internal sealed class AccessTokenService(
    JwtSigningKeyProvider keys,
    IOptions<JwtOptions> options,
    TimeProvider clock) : IAccessTokenService
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken CreateAccessToken(User user)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(settings.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(ClaimNames.Subject, user.Id),
            new(ClaimNames.TenantId, user.TenantId),
            new(ClaimNames.TokenId, Guid.NewGuid().ToString("N")),
        };
        claims.AddRange(user.Roles.Select(r => new Claim(ClaimNames.Role, r)));

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = keys.Credentials,
        });

        return new AccessToken(token, expires);
    }
}
