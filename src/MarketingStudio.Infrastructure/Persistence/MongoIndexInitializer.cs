using MarketingStudio.Domain.Audit;
using MarketingStudio.Domain.Auth;
using MarketingStudio.Domain.Billing;
using MarketingStudio.Domain.Users;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;

namespace MarketingStudio.Infrastructure.Persistence;

/// <summary>
/// Creates indexes at startup (idempotent). Tenant-owned collections index on tenantId first.
/// Moves to a versioned migration runner in a later sprint (DevOps Deployment Guide.md → Deployment steps).
/// </summary>
internal sealed class MongoIndexInitializer(MongoContext context) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await context.Users.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<User>(
                    Builders<User>.IndexKeys.Ascending(u => u.NormalizedEmail),
                    new CreateIndexOptions { Unique = true, Name = "ux_normalizedEmail" }),
                new CreateIndexModel<User>(
                    Builders<User>.IndexKeys.Ascending(u => u.TenantId),
                    new CreateIndexOptions { Name = "ix_tenantId" }),
            ],
            cancellationToken);

        await context.Subscriptions.Indexes.CreateOneAsync(
            new CreateIndexModel<Subscription>(
                Builders<Subscription>.IndexKeys.Ascending(s => s.TenantId),
                new CreateIndexOptions { Unique = true, Name = "ux_tenantId" }),
            cancellationToken: cancellationToken);

        await context.RefreshTokens.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<RefreshToken>(
                    Builders<RefreshToken>.IndexKeys.Ascending(t => t.TokenHash),
                    new CreateIndexOptions { Unique = true, Name = "ux_tokenHash" }),
                new CreateIndexModel<RefreshToken>(
                    Builders<RefreshToken>.IndexKeys.Ascending(t => t.FamilyId),
                    new CreateIndexOptions { Name = "ix_familyId" }),
                new CreateIndexModel<RefreshToken>(
                    Builders<RefreshToken>.IndexKeys.Ascending(t => t.UserId),
                    new CreateIndexOptions { Name = "ix_userId" }),
                new CreateIndexModel<RefreshToken>(
                    Builders<RefreshToken>.IndexKeys.Ascending(t => t.ExpiresAt),
                    new CreateIndexOptions { ExpireAfter = TimeSpan.Zero, Name = "ttl_expiresAt" }),
            ],
            cancellationToken);

        await context.AuditLog.Indexes.CreateOneAsync(
            new CreateIndexModel<AuditEntry>(
                Builders<AuditEntry>.IndexKeys.Ascending(a => a.TenantId).Descending(a => a.CreatedAt),
                new CreateIndexOptions { Name = "ix_tenantId_createdAt" }),
            cancellationToken: cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
