using MarketingStudio.Application.Abstractions;
using MarketingStudio.Domain.Audit;
using MarketingStudio.Domain.Billing;
using MarketingStudio.Domain.Tenants;
using MarketingStudio.Domain.Users;
using MongoDB.Driver;

namespace MarketingStudio.Infrastructure.Persistence;

/// <summary>Tenants are looked up only by the caller's own tenant id from the token.</summary>
internal sealed class TenantRepository(MongoContext context, ITenantContext tenantContext, MongoSessionAccessor sessions)
    : ITenantRepository
{
    public Task AddAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        var session = sessions.Session;
        return session is null
            ? context.Tenants.InsertOneAsync(tenant, cancellationToken: cancellationToken)
            : context.Tenants.InsertOneAsync(session, tenant, cancellationToken: cancellationToken);
    }

    public async Task<Tenant?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.RequireTenantId();
        return await context.Tenants.Find(t => t.Id == tenantId).FirstOrDefaultAsync(cancellationToken);
    }
}

internal sealed class SubscriptionRepository(MongoContext context, ITenantContext tenantContext, MongoSessionAccessor sessions)
    : TenantScopedRepository<Subscription>(context.Subscriptions, tenantContext, sessions), ISubscriptionRepository
{
    public Task AddAsync(Subscription subscription, CancellationToken cancellationToken) =>
        InsertAsync(subscription, cancellationToken);

    public Task<Subscription?> GetForCurrentTenantAsync(CancellationToken cancellationToken) =>
        FindOneAsync(null, cancellationToken);
}

internal sealed class UserRepository(MongoContext context, ITenantContext tenantContext, MongoSessionAccessor sessions)
    : TenantScopedRepository<User>(context.Users, tenantContext, sessions), IUserRepository
{
    public Task<User?> GetByIdAsync(string userId, CancellationToken cancellationToken) =>
        FindOneAsync(Builders<User>.Filter.Eq(u => u.Id, userId), cancellationToken);
}

internal sealed class MongoAuditLog(MongoContext context) : IAuditLog
{
    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken) =>
        context.AuditLog.InsertOneAsync(entry, cancellationToken: cancellationToken);
}
