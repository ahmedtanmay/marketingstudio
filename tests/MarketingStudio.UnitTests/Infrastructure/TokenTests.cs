using FluentAssertions;
using MarketingStudio.Domain.Users;
using MarketingStudio.Infrastructure;
using MarketingStudio.Infrastructure.Tokens;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MarketingStudio.UnitTests.Infrastructure;

public sealed class TokenTests : IDisposable
{
    private readonly string _keyPath = Path.Combine(Path.GetTempPath(), $"ms-test-{Guid.NewGuid():N}", "jwt.pem");

    private IOptions<JwtOptions> Options(bool create = true) => Microsoft.Extensions.Options.Options.Create(new JwtOptions
    {
        Issuer = "https://issuer.test",
        Audience = "aud.test",
        AccessTokenMinutes = 15,
        SigningKeyPath = _keyPath,
        CreateKeyIfMissing = create,
    });

    [Fact]
    public void Signing_key_is_created_once_and_reused()
    {
        var first = new JwtSigningKeyProvider(Options());
        var second = new JwtSigningKeyProvider(Options(create: false));

        File.Exists(_keyPath).Should().BeTrue();
        second.Key.KeyId.Should().Be(first.Key.KeyId);
    }

    [Fact]
    public void Missing_key_fails_fast_when_creation_is_not_allowed() =>
        FluentActions.Invoking(() => new JwtSigningKeyProvider(Options(create: false)))
            .Should().Throw<InvalidOperationException>().WithMessage("*signing key not found*");

    [Fact]
    [Trait("Story", "AUTH-2")]
    public async Task Access_token_carries_only_subject_tenant_roles_and_expires_in_15_minutes()
    {
        var now = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var keys = new JwtSigningKeyProvider(Options());
        var service = new AccessTokenService(keys, Options(), new FixedTimeProvider(now));
        var user = User.Create("tenant-1", "ana@example.com", "Ana", Roles.TenantAdmin, now.UtcDateTime);

        var token = service.CreateAccessToken(user);

        token.ExpiresAt.Should().Be(now.UtcDateTime.AddMinutes(15));

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Token, new TokenValidationParameters
        {
            ValidIssuer = "https://issuer.test",
            ValidAudience = "aud.test",
            IssuerSigningKey = keys.Key,
            ValidateLifetime = false,
        });

        result.IsValid.Should().BeTrue();
        var jwt = (JsonWebToken)result.SecurityToken;
        jwt.Alg.Should().Be(SecurityAlgorithms.EcdsaSha256);
        jwt.Subject.Should().Be(user.Id);
        jwt.GetClaim(ClaimNames.TenantId).Value.Should().Be("tenant-1");
        jwt.GetClaim(ClaimNames.Role).Value.Should().Be(Roles.TenantAdmin);
        jwt.Claims.Select(c => c.Type).Should().NotContain(["email", "name", "password"]);
    }

    [Fact]
    [Trait("Story", "AUTH-3")]
    public void Refresh_tokens_are_stored_as_sha256_hashes() =>
        RefreshTokenService.Hash("abc").Should()
            .Be("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD");

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_keyPath);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
