using MarketingStudio.Domain.Common;

namespace MarketingStudio.Domain.Auth;

/// <summary>
/// One issued refresh token. Only the SHA-256 hash is stored. All rotations that descend from one
/// sign-in share a <see cref="FamilyId"/>; reuse of a rotated token revokes the whole family
/// (Security Architecture.md → Authentication).
/// </summary>
public sealed class RefreshToken : ITenantOwned
{
    public string Id { get; set; } = IdGenerator.NewId();
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string FamilyId { get; set; } = string.Empty;

    /// <summary>When the sign-in that started this family happened; bounds the absolute session lifetime.</summary>
    public DateTime FamilyCreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByHash { get; set; }

    public bool IsUsable(DateTime nowUtc, TimeSpan absoluteLifetime) =>
        RevokedAt is null && ExpiresAt > nowUtc && FamilyCreatedAt + absoluteLifetime > nowUtc;
}
