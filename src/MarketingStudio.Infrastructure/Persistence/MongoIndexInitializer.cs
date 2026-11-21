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

        await context.Invites.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<Invite>(
                    Builders<Invite>.IndexKeys.Ascending(i => i.TokenHash),
                    new CreateIndexOptions { Unique = true, Name = "ux_tokenHash" }),
                new CreateIndexModel<Invite>(
                    Builders<Invite>.IndexKeys.Ascending(i => i.TenantId).Ascending(i => i.Status).Ascending(i => i.NormalizedEmail),
                    new CreateIndexOptions { Name = "ix_tenantId_status_email" }),
            ],
            cancellationToken);

        await context.Brands.Indexes.CreateOneAsync(
            new CreateIndexModel<Domain.Brands.Brand>(
                Builders<Domain.Brands.Brand>.IndexKeys.Ascending(b => b.TenantId).Ascending(b => b.NormalizedName),
                new CreateIndexOptions { Unique = true, Name = "ux_tenantId_normalizedName" }),
            cancellationToken: cancellationToken);

        await context.Products.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<Domain.Brands.Product>(
                    Builders<Domain.Brands.Product>.IndexKeys.Ascending(p => p.TenantId).Ascending(p => p.BrandId).Ascending(p => p.Name),
                    new CreateIndexOptions { Name = "ix_tenantId_brandId_name" }),
                new CreateIndexModel<Domain.Brands.Product>(
                    Builders<Domain.Brands.Product>.IndexKeys.Ascending(p => p.TenantId).Ascending(p => p.BrandId).Ascending(p => p.Category),
                    new CreateIndexOptions { Name = "ix_tenantId_brandId_category" }),
            ],
            cancellationToken);

        await context.Media.Indexes.CreateOneAsync(
            new CreateIndexModel<Domain.Brands.MediaAsset>(
                Builders<Domain.Brands.MediaAsset>.IndexKeys.Ascending(a => a.TenantId).Ascending(a => a.BrandId).Descending(a => a.CreatedAt),
                new CreateIndexOptions { Name = "ix_tenantId_brandId_createdAt" }),
            cancellationToken: cancellationToken);

        await context.KnowledgeDocuments.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<Domain.Knowledge.KnowledgeDocument>(
                    Builders<Domain.Knowledge.KnowledgeDocument>.IndexKeys.Ascending(d => d.TenantId).Ascending(d => d.BrandId).Descending(d => d.CreatedAt),
                    new CreateIndexOptions { Name = "ix_tenantId_brandId_createdAt" }),
                new CreateIndexModel<Domain.Knowledge.KnowledgeDocument>(
                    Builders<Domain.Knowledge.KnowledgeDocument>.IndexKeys.Ascending(d => d.TenantId).Ascending(d => d.BrandId).Ascending(d => d.Checksum),
                    new CreateIndexOptions { Name = "ix_tenantId_brandId_checksum" }),
                new CreateIndexModel<Domain.Knowledge.KnowledgeDocument>(
                    Builders<Domain.Knowledge.KnowledgeDocument>.IndexKeys.Ascending(d => d.Status),
                    new CreateIndexOptions { Name = "ix_status" }),
            ],
            cancellationToken);

        await context.Faqs.Indexes.CreateOneAsync(
            new CreateIndexModel<Domain.Knowledge.Faq>(
                Builders<Domain.Knowledge.Faq>.IndexKeys.Ascending(f => f.TenantId).Ascending(f => f.BrandId),
                new CreateIndexOptions { Name = "ix_tenantId_brandId" }),
            cancellationToken: cancellationToken);

        await context.AuditLog.Indexes.CreateOneAsync(
            new CreateIndexModel<AuditEntry>(
                Builders<AuditEntry>.IndexKeys.Ascending(a => a.TenantId).Descending(a => a.CreatedAt),
                new CreateIndexOptions { Name = "ix_tenantId_createdAt" }),
            cancellationToken: cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
