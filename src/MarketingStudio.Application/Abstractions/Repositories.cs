using MarketingStudio.Domain.Audit;
using MarketingStudio.Domain.Billing;
using MarketingStudio.Domain.Tenants;
using MarketingStudio.Domain.Users;

namespace MarketingStudio.Application.Abstractions;

public interface ITenantRepository
{
    Task AddAsync(Tenant tenant, CancellationToken cancellationToken);

    /// <summary>Returns the caller's own tenant (id taken from the tenant context).</summary>
    Task<Tenant?> GetCurrentAsync(CancellationToken cancellationToken);
}

/// <summary>Tenant-scoped: every read is filtered by the caller's tenant.</summary>
public interface ISubscriptionRepository
{
    Task AddAsync(Subscription subscription, CancellationToken cancellationToken);
    Task<Subscription?> GetForCurrentTenantAsync(CancellationToken cancellationToken);
}

/// <summary>Tenant-scoped user reads for features (not for sign-in, which happens before a tenant is known).</summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(string userId, CancellationToken cancellationToken);
}

public interface IAuditLog
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken);
}
