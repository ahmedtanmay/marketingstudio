using MarketingStudio.Domain.Common;

namespace MarketingStudio.Domain.Users;

public enum InviteStatus
{
    Pending,
    Accepted,
    Revoked,
}

/// <summary>
/// AUTH-5: an invitation for an email address to join a tenant with a role. The emailed link carries
/// a random token; only its hash is stored. Valid for 7 days.
/// </summary>
public sealed class Invite : ITenantOwned
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public string Id { get; set; } = IdGenerator.NewId();
    public string TenantId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string Role { get; set; } = Roles.Viewer;
    public string TokenHash { get; set; } = string.Empty;
    public InviteStatus Status { get; set; } = InviteStatus.Pending;
    public string InvitedBy { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastSentAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public string? AcceptedUserId { get; set; }

    public static Invite Create(
        string tenantId, string email, string role, string invitedBy, string tokenHash, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        if (!Roles.TenantRoles.Contains(role))
        {
            throw new ArgumentException($"Unknown tenant role '{role}'.", nameof(role));
        }

        var trimmed = email.Trim();
        return new Invite
        {
            TenantId = tenantId,
            Email = trimmed,
            NormalizedEmail = Normalize(trimmed),
            Role = role,
            InvitedBy = invitedBy,
            TokenHash = tokenHash,
            CreatedAt = nowUtc,
            LastSentAt = nowUtc,
            ExpiresAt = nowUtc + Lifetime,
        };
    }

    public static string Normalize(string email) => email.Trim().ToUpperInvariant();

    public bool IsOpen(DateTime nowUtc) => Status == InviteStatus.Pending && ExpiresAt > nowUtc;

    /// <summary>New link and a fresh 7 days; the old link stops working.</summary>
    public void Renew(string tokenHash, DateTime nowUtc)
    {
        TokenHash = tokenHash;
        LastSentAt = nowUtc;
        ExpiresAt = nowUtc + Lifetime;
    }

    public void Accept(string userId, DateTime nowUtc)
    {
        Status = InviteStatus.Accepted;
        AcceptedAt = nowUtc;
        AcceptedUserId = userId;
    }
}
