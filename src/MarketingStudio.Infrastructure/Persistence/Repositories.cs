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

    public Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken) =>
        FindManyAsync(null, cancellationToken);

    public Task<long> CountActiveAsync(CancellationToken cancellationToken) =>
        CountAsync(Builders<User>.Filter.Eq(u => u.Status, UserStatus.Active), cancellationToken);

    public Task<long> CountActiveWithRoleAsync(string role, CancellationToken cancellationToken) =>
        CountAsync(
            Builders<User>.Filter.Eq(u => u.Status, UserStatus.Active) & Builders<User>.Filter.AnyEq(u => u.Roles, role),
            cancellationToken);
}

internal sealed class InviteRepository(MongoContext context, ITenantContext tenantContext, MongoSessionAccessor sessions)
    : TenantScopedRepository<Invite>(context.Invites, tenantContext, sessions), IInviteRepository
{
    private static FilterDefinition<Invite> Open(DateTime nowUtc) =>
        Builders<Invite>.Filter.Eq(i => i.Status, InviteStatus.Pending) & Builders<Invite>.Filter.Gt(i => i.ExpiresAt, nowUtc);

    public Task AddAsync(Invite invite, CancellationToken cancellationToken) => InsertAsync(invite, cancellationToken);

    public Task<Invite?> GetByIdAsync(string inviteId, CancellationToken cancellationToken) =>
        FindOneAsync(Builders<Invite>.Filter.Eq(i => i.Id, inviteId), cancellationToken);

    public Task<Invite?> FindOpenByEmailAsync(string normalizedEmail, DateTime nowUtc, CancellationToken cancellationToken) =>
        FindOneAsync(Open(nowUtc) & Builders<Invite>.Filter.Eq(i => i.NormalizedEmail, normalizedEmail), cancellationToken);

    public Task<IReadOnlyList<Invite>> ListOpenAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
        FindManyAsync(Open(nowUtc), cancellationToken);

    public Task<long> CountOpenAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
        CountAsync(Open(nowUtc), cancellationToken);

    public Task UpdateAsync(Invite invite, CancellationToken cancellationToken) => ReplaceAsync(invite, cancellationToken);

    // ---- Token-based, cross-tenant by design: the emailed token itself is the capability. ----

    public async Task<Invite?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        await context.Invites.Find(i => i.TokenHash == tokenHash).FirstOrDefaultAsync(cancellationToken);

    public async Task SaveAcceptedAsync(Invite invite, CancellationToken cancellationToken)
    {
        // Only a still-pending invite can be accepted, so a link can't be used twice.
        var filter = Builders<Invite>.Filter.Eq(i => i.Id, invite.Id)
            & Builders<Invite>.Filter.Eq(i => i.Status, InviteStatus.Pending);
        var session = Sessions.Session;
        var result = session is null
            ? await context.Invites.ReplaceOneAsync(filter, invite, cancellationToken: cancellationToken)
            : await context.Invites.ReplaceOneAsync(session, filter, invite, cancellationToken: cancellationToken);

        if (result.MatchedCount == 0)
        {
            throw new NotFoundException(Application.Users.GetInviteQueryHandler.InvalidLink);
        }
    }

    public async Task<string?> GetTenantNameAsync(string tenantId, CancellationToken cancellationToken) =>
        await context.Tenants.Find(t => t.Id == tenantId).Project(t => t.Name).FirstOrDefaultAsync(cancellationToken);
}

/// <summary>Seat limits per plan from configuration (section "Plans"); defaults match Billing and Subscriptions Design.md.</summary>
internal sealed class ConfiguredPlanLimits(Microsoft.Extensions.Options.IOptions<PlanLimitOptions> options) : IPlanLimits
{
    public int MaxSeats(string planId) =>
        options.Value.Seats.TryGetValue(planId, out var seats) ? seats : options.Value.Seats[Domain.Billing.Subscription.TrialPlanId];
}

public sealed class PlanLimitOptions
{
    public const string Section = "Plans";

    public Dictionary<string, int> Seats { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["trial"] = 2,
        ["starter"] = 3,
        ["growth"] = 15,
        ["enterprise"] = 1000,
    };
}

internal sealed class MongoAuditLog(MongoContext context) : IAuditLog
{
    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken) =>
        context.AuditLog.InsertOneAsync(entry, cancellationToken: cancellationToken);
}
