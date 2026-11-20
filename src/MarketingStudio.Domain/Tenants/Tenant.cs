using MarketingStudio.Domain.Common;

namespace MarketingStudio.Domain.Tenants;

public enum TenantStatus
{
    Active,
    Suspended,
    Deleted,
}

public sealed class Tenant
{
    public string Id { get; set; } = IdGenerator.NewId();
    public string Name { get; set; } = string.Empty;
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    public string? SubscriptionId { get; set; }
    public DateTime CreatedAt { get; set; }

    public static Tenant Create(string name, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Tenant { Name = name.Trim(), CreatedAt = nowUtc };
    }
}
