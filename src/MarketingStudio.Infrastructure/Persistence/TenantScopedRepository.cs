using MarketingStudio.Application.Abstractions;
using MarketingStudio.Domain.Common;
using MongoDB.Driver;

namespace MarketingStudio.Infrastructure.Persistence;

/// <summary>
/// Base for every repository of tenant-owned data. All reads add <c>tenantId == caller's tenant</c>;
/// inserts by a signed-in caller must carry the caller's tenant. There is deliberately no unscoped
/// query method here (Multi-Tenant Design.md → Isolation by Store).
/// </summary>
internal abstract class TenantScopedRepository<T>(
    IMongoCollection<T> collection,
    ITenantContext tenantContext,
    MongoSessionAccessor sessionAccessor)
    where T : ITenantOwned
{
    protected ITenantContext TenantContext { get; } = tenantContext;

    protected MongoSessionAccessor Sessions { get; } = sessionAccessor;

    protected FilterDefinition<T> Scoped(FilterDefinition<T>? filter = null)
    {
        var tenantFilter = Builders<T>.Filter.Eq(x => x.TenantId, TenantContext.RequireTenantId());
        return filter is null ? tenantFilter : Builders<T>.Filter.And(tenantFilter, filter);
    }

    protected Task<T?> FindOneAsync(FilterDefinition<T>? filter, CancellationToken cancellationToken)
    {
        var session = Sessions.Session;
        var find = session is null
            ? collection.Find(Scoped(filter))
            : collection.Find(session, Scoped(filter));
        return find.FirstOrDefaultAsync(cancellationToken)!;
    }

    protected Task InsertAsync(T entity, CancellationToken cancellationToken)
    {
        // Anonymous inserts happen only during sign-up, where the new tenant is created in the same transaction.
        if (TenantContext.TenantId is { } callerTenant && entity.TenantId != callerTenant)
        {
            throw new InvalidOperationException("Cannot write a record for another tenant.");
        }

        var session = Sessions.Session;
        return session is null
            ? collection.InsertOneAsync(entity, cancellationToken: cancellationToken)
            : collection.InsertOneAsync(session, entity, cancellationToken: cancellationToken);
    }

    protected async Task<IReadOnlyList<T>> FindManyAsync(FilterDefinition<T>? filter, CancellationToken cancellationToken) =>
        await collection.Find(Scoped(filter)).ToListAsync(cancellationToken);

    protected Task<long> CountAsync(FilterDefinition<T>? filter, CancellationToken cancellationToken) =>
        collection.CountDocumentsAsync(Scoped(filter), cancellationToken: cancellationToken);

    /// <summary>Replaces a record only if it belongs to the caller's tenant.</summary>
    protected async Task ReplaceAsync(T entity, CancellationToken cancellationToken)
    {
        var filter = Scoped(Builders<T>.Filter.Eq(x => x.Id, entity.Id));
        var session = Sessions.Session;
        var result = session is null
            ? await collection.ReplaceOneAsync(filter, entity, cancellationToken: cancellationToken)
            : await collection.ReplaceOneAsync(session, filter, entity, cancellationToken: cancellationToken);

        if (result.MatchedCount == 0)
        {
            throw new Application.Abstractions.NotFoundException();
        }
    }
}
