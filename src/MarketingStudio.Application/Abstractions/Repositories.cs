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
    Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken);
    Task<long> CountActiveAsync(CancellationToken cancellationToken);
    Task<long> CountActiveWithRoleAsync(string role, CancellationToken cancellationToken);
}

public interface IInviteRepository
{
    // Tenant-scoped (caller's tenant)
    Task AddAsync(Invite invite, CancellationToken cancellationToken);
    Task<Invite?> GetByIdAsync(string inviteId, CancellationToken cancellationToken);
    Task<Invite?> FindOpenByEmailAsync(string normalizedEmail, DateTime nowUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<Invite>> ListOpenAsync(DateTime nowUtc, CancellationToken cancellationToken);
    Task<long> CountOpenAsync(DateTime nowUtc, CancellationToken cancellationToken);
    Task UpdateAsync(Invite invite, CancellationToken cancellationToken);

    // Token-based (the emailed token is the capability; used before the invitee has an account)
    Task<Invite?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);
    Task SaveAcceptedAsync(Invite invite, CancellationToken cancellationToken);

    /// <summary>Tenant name for the invite page; looked up by the invite's own tenant id.</summary>
    Task<string?> GetTenantNameAsync(string tenantId, CancellationToken cancellationToken);
}

/// <summary>
/// Plan limits until the full entitlement service arrives in Phase 5 (Billing and Subscriptions Design.md).
/// </summary>
public interface IPlanLimits
{
    int MaxSeats(string planId);
}

public interface IAuditLog
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken);
}
