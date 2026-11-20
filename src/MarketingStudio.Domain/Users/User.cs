using MarketingStudio.Domain.Common;

namespace MarketingStudio.Domain.Users;

public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string TenantAdmin = "TenantAdmin";
    public const string MarketingManager = "MarketingManager";
    public const string ContentManager = "ContentManager";
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyList<string> TenantRoles =
        [TenantAdmin, MarketingManager, ContentManager, Viewer];
}

public enum UserStatus
{
    Active,
    Deactivated,
}

/// <summary>
/// Platform user. Persisted by the MongoDB Identity store; one user belongs to one tenant (MVP).
/// </summary>
public sealed class User : ITenantOwned
{
    public string Id { get; set; } = IdGenerator.NewId();
    public string TenantId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public List<string> Roles { get; set; } = [];
    public UserStatus Status { get; set; } = UserStatus.Active;
    public bool EmailConfirmed { get; set; }

    // Identity bookkeeping
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
    public int AccessFailedCount { get; set; }
    public bool LockoutEnabled { get; set; } = true;
    public DateTime? LockoutEnd { get; set; }

    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public static User Create(string tenantId, string email, string displayName, string role, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        return new User
        {
            TenantId = tenantId,
            Email = email.Trim(),
            DisplayName = displayName.Trim(),
            Roles = [role],
            CreatedAt = nowUtc,
        };
    }
}
