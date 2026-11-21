using MarketingStudio.Application.Abstractions;
using MarketingStudio.Domain.Knowledge;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MarketingStudio.Infrastructure.Persistence;

internal sealed class KnowledgeDocumentRepository(MongoContext context, ITenantContext tenantContext, MongoSessionAccessor sessions)
    : TenantScopedRepository<KnowledgeDocument>(context.KnowledgeDocuments, tenantContext, sessions), IKnowledgeDocumentRepository
{
    private static readonly FilterDefinitionBuilder<KnowledgeDocument> F = Builders<KnowledgeDocument>.Filter;

    private static FilterDefinition<KnowledgeDocument> InBrand(string brandId) => F.Eq(d => d.BrandId, brandId);

    public Task AddAsync(KnowledgeDocument document, CancellationToken cancellationToken) => InsertAsync(document, cancellationToken);

    public Task<KnowledgeDocument?> GetByIdAsync(string brandId, string documentId, CancellationToken cancellationToken) =>
        FindOneAsync(InBrand(brandId) & F.Eq(d => d.Id, documentId), cancellationToken);

    public Task<IReadOnlyList<KnowledgeDocument>> ListAsync(string brandId, CancellationToken cancellationToken) =>
        FindManyAsync(InBrand(brandId), cancellationToken);

    public Task<long> CountForBrandAsync(string brandId, CancellationToken cancellationToken) =>
        CountAsync(InBrand(brandId) & F.Ne(d => d.Status, IngestionStatus.Rejected), cancellationToken);

    public async Task<long> TotalBytesAsync(CancellationToken cancellationToken)
    {
        var result = await Collection.Aggregate()
            .Match(Scoped(F.Ne(d => d.Status, IngestionStatus.Rejected)))
            .Group(new BsonDocument { { "_id", BsonNull.Value }, { "total", new BsonDocument("$sum", "$sizeBytes") } })
            .FirstOrDefaultAsync(cancellationToken);
        return result is null ? 0 : result["total"].ToInt64();
    }

    public async Task<bool> ChecksumExistsAsync(string brandId, string checksum, CancellationToken cancellationToken) =>
        await CountAsync(InBrand(brandId) & F.Eq(d => d.Checksum, checksum) & F.Ne(d => d.Status, IngestionStatus.Rejected), cancellationToken) > 0;

    public Task<long> CountReadyAsync(CancellationToken cancellationToken) =>
        CountAsync(F.Eq(d => d.Status, IngestionStatus.Ready), cancellationToken);

    public Task UpdateAsync(KnowledgeDocument document, CancellationToken cancellationToken) => ReplaceAsync(document, cancellationToken);

    public Task DeleteAsync(string brandId, string documentId, CancellationToken cancellationToken) =>
        DeleteManyAsync(InBrand(brandId) & F.Eq(d => d.Id, documentId), cancellationToken);

    public Task DeleteForBrandAsync(string brandId, CancellationToken cancellationToken) => DeleteManyAsync(InBrand(brandId), cancellationToken);

    // System (recovery after restart): across tenants by design; returns only ids and tenancy for re-queuing.
    public async Task<IReadOnlyList<KnowledgeDocument>> ListUnfinishedAsync(CancellationToken cancellationToken) =>
        await context.KnowledgeDocuments
            .Find(F.In(d => d.Status, [IngestionStatus.Uploaded, IngestionStatus.Processing]))
            .ToListAsync(cancellationToken);
}

internal sealed class FaqRepository(MongoContext context, ITenantContext tenantContext, MongoSessionAccessor sessions)
    : TenantScopedRepository<Faq>(context.Faqs, tenantContext, sessions), IFaqRepository
{
    private static readonly FilterDefinitionBuilder<Faq> F = Builders<Faq>.Filter;

    private static FilterDefinition<Faq> InBrand(string brandId) => F.Eq(f => f.BrandId, brandId);

    public Task AddAsync(Faq faq, CancellationToken cancellationToken) => InsertAsync(faq, cancellationToken);

    public Task<Faq?> GetByIdAsync(string brandId, string faqId, CancellationToken cancellationToken) =>
        FindOneAsync(InBrand(brandId) & F.Eq(f => f.Id, faqId), cancellationToken);

    public Task<IReadOnlyList<Faq>> ListAsync(string brandId, CancellationToken cancellationToken) => FindManyAsync(InBrand(brandId), cancellationToken);

    public Task<long> CountAsync(CancellationToken cancellationToken) => CountAsync(null, cancellationToken);

    public Task UpdateAsync(Faq faq, CancellationToken cancellationToken) => ReplaceAsync(faq, cancellationToken);

    public Task DeleteAsync(string brandId, string faqId, CancellationToken cancellationToken) =>
        DeleteManyAsync(InBrand(brandId) & F.Eq(f => f.Id, faqId), cancellationToken);

    public Task DeleteForBrandAsync(string brandId, CancellationToken cancellationToken) => DeleteManyAsync(InBrand(brandId), cancellationToken);
}
