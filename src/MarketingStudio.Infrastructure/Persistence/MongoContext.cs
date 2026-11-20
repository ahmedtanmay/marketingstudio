using MarketingStudio.Domain.Audit;
using MarketingStudio.Domain.Auth;
using MarketingStudio.Domain.Billing;
using MarketingStudio.Domain.Tenants;
using MarketingStudio.Domain.Users;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace MarketingStudio.Infrastructure.Persistence;

/// <summary>
/// The only place collections are created. Feature code never sees <see cref="IMongoCollection{T}"/>;
/// it goes through tenant-scoped repositories (Multi-Tenant Design.md → Enforcement).
/// </summary>
internal sealed class MongoContext
{
    public MongoContext(IMongoClient client, IOptions<MongoOptions> options)
    {
        Client = client;
        Database = client.GetDatabase(options.Value.Database);
    }

    public IMongoClient Client { get; }
    public IMongoDatabase Database { get; }

    public IMongoCollection<Tenant> Tenants => Database.GetCollection<Tenant>("tenants");
    public IMongoCollection<Subscription> Subscriptions => Database.GetCollection<Subscription>("subscriptions");
    public IMongoCollection<User> Users => Database.GetCollection<User>("users");
    public IMongoCollection<RefreshToken> RefreshTokens => Database.GetCollection<RefreshToken>("refreshTokens");
    public IMongoCollection<AuditEntry> AuditLog => Database.GetCollection<AuditEntry>("auditLog");
    public IMongoCollection<Invite> Invites => Database.GetCollection<Invite>("invites");
}

/// <summary>Holds the session of the transaction running in the current request scope, if any.</summary>
internal sealed class MongoSessionAccessor
{
    public IClientSessionHandle? Session { get; set; }
}

internal sealed class MongoUnitOfWork(MongoContext context, MongoSessionAccessor accessor)
    : Application.Abstractions.IUnitOfWork
{
    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        using var session = await context.Client.StartSessionAsync(cancellationToken: cancellationToken);
        accessor.Session = session;
        try
        {
            await session.WithTransactionAsync(
                async (_, ct) =>
                {
                    await work(ct);
                    return true;
                },
                cancellationToken: cancellationToken);
        }
        finally
        {
            accessor.Session = null;
        }
    }
}
