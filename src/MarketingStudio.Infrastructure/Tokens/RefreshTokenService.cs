using System.Security.Cryptography;
using System.Text;
using MarketingStudio.Application.Abstractions;
using MarketingStudio.Domain.Auth;
using MarketingStudio.Domain.Users;
using MarketingStudio.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;

namespace MarketingStudio.Infrastructure.Tokens;

/// <summary>
/// Opaque, rotating refresh tokens (Security Architecture.md → Authentication):
/// 7-day lifetime, 30-day absolute session, single use, and reuse of a rotated token revokes the family.
/// Only SHA-256 hashes are stored.
/// </summary>
internal sealed class RefreshTokenService(
    MongoContext context,
    IOptions<RefreshTokenOptions> options,
    TimeProvider clock) : IRefreshTokenService
{
    private IMongoCollection<RefreshToken> Tokens => context.RefreshTokens;
    private TimeSpan Lifetime => TimeSpan.FromDays(options.Value.LifetimeDays);
    private TimeSpan AbsoluteLifetime => TimeSpan.FromDays(options.Value.AbsoluteSessionDays);

    public async Task<IssuedRefreshToken> IssueAsync(User user, CancellationToken cancellationToken)
    {
        var now = Now();
        var (raw, hash) = NewToken();
        var token = new RefreshToken
        {
            TenantId = user.TenantId,
            UserId = user.Id,
            TokenHash = hash,
            FamilyId = Domain.Common.IdGenerator.NewId(),
            FamilyCreatedAt = now,
            CreatedAt = now,
            ExpiresAt = now + Lifetime,
        };

        await Tokens.InsertOneAsync(token, cancellationToken: cancellationToken);
        return new IssuedRefreshToken(raw, token.ExpiresAt);
    }

    public async Task<RefreshRotation> RotateAsync(string rawToken, CancellationToken cancellationToken)
    {
        var now = Now();
        var hash = Hash(rawToken);
        var current = await Tokens.Find(t => t.TokenHash == hash).FirstOrDefaultAsync(cancellationToken);

        if (current is null)
        {
            return RefreshRotation.Fail(RefreshFailure.Invalid);
        }

        if (current.RevokedAt is not null)
        {
            if (current.ReplacedByHash is null)
            {
                // Revoked by sign-out or a family revocation — not a replay of a rotated token.
                return RefreshRotation.Fail(RefreshFailure.Invalid);
            }

            await RevokeFamilyByIdAsync(current.FamilyId, now, cancellationToken);
            return RefreshRotation.Fail(RefreshFailure.ReuseDetected, current.UserId, current.TenantId);
        }

        if (!current.IsUsable(now, AbsoluteLifetime))
        {
            return RefreshRotation.Fail(RefreshFailure.Expired);
        }

        var (newRaw, newHash) = NewToken();

        // Atomically claim the current token so two concurrent refreshes cannot both succeed.
        var claimed = await Tokens.FindOneAndUpdateAsync(
            Builders<RefreshToken>.Filter.Eq(t => t.Id, current.Id)
                & Builders<RefreshToken>.Filter.Eq(t => t.RevokedAt, null),
            Builders<RefreshToken>.Update.Set(t => t.RevokedAt, now).Set(t => t.ReplacedByHash, newHash),
            cancellationToken: cancellationToken);

        if (claimed is null)
        {
            await RevokeFamilyByIdAsync(current.FamilyId, now, cancellationToken);
            return RefreshRotation.Fail(RefreshFailure.ReuseDetected, current.UserId, current.TenantId);
        }

        var familyEnd = current.FamilyCreatedAt + AbsoluteLifetime;
        var next = new RefreshToken
        {
            TenantId = current.TenantId,
            UserId = current.UserId,
            TokenHash = newHash,
            FamilyId = current.FamilyId,
            FamilyCreatedAt = current.FamilyCreatedAt,
            CreatedAt = now,
            ExpiresAt = now + Lifetime < familyEnd ? now + Lifetime : familyEnd,
        };
        await Tokens.InsertOneAsync(next, cancellationToken: cancellationToken);

        return new RefreshRotation(
            true,
            current.UserId,
            current.TenantId,
            new IssuedRefreshToken(newRaw, next.ExpiresAt),
            null);
    }

    public async Task RevokeFamilyAsync(string rawToken, CancellationToken cancellationToken)
    {
        var hash = Hash(rawToken);
        var token = await Tokens.Find(t => t.TokenHash == hash).FirstOrDefaultAsync(cancellationToken);
        if (token is not null)
        {
            await RevokeFamilyByIdAsync(token.FamilyId, Now(), cancellationToken);
        }
    }

    public async Task RevokeAllForUserAsync(string userId, CancellationToken cancellationToken) =>
        await Tokens.UpdateManyAsync(
            t => t.UserId == userId && t.RevokedAt == null,
            Builders<RefreshToken>.Update.Set(t => t.RevokedAt, Now()),
            cancellationToken: cancellationToken);

    internal static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private async Task RevokeFamilyByIdAsync(string familyId, DateTime now, CancellationToken cancellationToken) =>
        await Tokens.UpdateManyAsync(
            t => t.FamilyId == familyId && t.RevokedAt == null,
            Builders<RefreshToken>.Update.Set(t => t.RevokedAt, now),
            cancellationToken: cancellationToken);

    private static (string Raw, string Hash) NewToken()
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (raw, Hash(raw));
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
