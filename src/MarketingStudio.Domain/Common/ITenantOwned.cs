namespace MarketingStudio.Domain.Common;

/// <summary>
/// Marks a record that belongs to exactly one tenant.
/// Tenant-scoped repositories always filter on <see cref="TenantId"/> (see Multi-Tenant Design.md).
/// </summary>
public interface ITenantOwned
{
    string Id { get; }
    string TenantId { get; }
}
