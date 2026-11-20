using System.Text.RegularExpressions;
using MarketingStudio.Application.Abstractions;
using MarketingStudio.Domain.Brands;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MarketingStudio.Infrastructure.Persistence;

internal sealed class BrandRepository(MongoContext context, ITenantContext tenantContext, MongoSessionAccessor sessions)
    : TenantScopedRepository<Brand>(context.Brands, tenantContext, sessions), IBrandRepository
{
    private static readonly FilterDefinitionBuilder<Brand> F = Builders<Brand>.Filter;

    public Task AddAsync(Brand brand, CancellationToken cancellationToken) => InsertAsync(brand, cancellationToken);

    public Task<Brand?> GetByIdAsync(string brandId, CancellationToken cancellationToken) =>
        FindOneAsync(F.Eq(b => b.Id, brandId), cancellationToken);

    public Task<IReadOnlyList<Brand>> ListAsync(CancellationToken cancellationToken) => FindManyAsync(null, cancellationToken);

    public Task<long> CountAsync(CancellationToken cancellationToken) => CountAsync(null, cancellationToken);

    public async Task<bool> NameExistsAsync(string normalizedName, string? exceptBrandId, CancellationToken cancellationToken)
    {
        var filter = F.Eq(b => b.NormalizedName, normalizedName);
        if (exceptBrandId is not null)
        {
            filter &= F.Ne(b => b.Id, exceptBrandId);
        }

        return await CountAsync(filter, cancellationToken) > 0;
    }

    public Task UpdateAsync(Brand brand, CancellationToken cancellationToken) => ReplaceAsync(brand, cancellationToken);

    public Task DeleteAsync(string brandId, CancellationToken cancellationToken) =>
        DeleteManyAsync(F.Eq(b => b.Id, brandId), cancellationToken);
}

internal sealed class ProductRepository(MongoContext context, ITenantContext tenantContext, MongoSessionAccessor sessions)
    : TenantScopedRepository<Product>(context.Products, tenantContext, sessions), IProductRepository
{
    private static readonly FilterDefinitionBuilder<Product> F = Builders<Product>.Filter;

    private static FilterDefinition<Product> InBrand(string brandId) => F.Eq(p => p.BrandId, brandId);

    public Task AddAsync(Product product, CancellationToken cancellationToken) => InsertAsync(product, cancellationToken);

    public Task<Product?> GetByIdAsync(string brandId, string productId, CancellationToken cancellationToken) =>
        FindOneAsync(InBrand(brandId) & F.Eq(p => p.Id, productId), cancellationToken);

    public async Task<PagedResult<Product>> SearchAsync(ProductQuery query, CancellationToken cancellationToken)
    {
        var filter = InBrand(query.BrandId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            filter &= F.Regex(p => p.Name, new BsonRegularExpression(Regex.Escape(query.Search), "i"));
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            filter &= F.Eq(p => p.Category, query.Category);
        }

        var scoped = Scoped(filter);
        var total = await Collection.CountDocumentsAsync(scoped, cancellationToken: cancellationToken);
        var items = await Collection.Find(scoped)
            .SortBy(p => p.Name)
            .Skip((query.Page - 1) * query.PageSize)
            .Limit(query.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Product>(items, total, query.Page, query.PageSize);
    }

    public async Task<IReadOnlyList<string>> CategoriesAsync(string brandId, CancellationToken cancellationToken)
    {
        var cursor = await Collection.DistinctAsync(p => p.Category, Scoped(InBrand(brandId) & F.Ne(p => p.Category, null)),
            cancellationToken: cancellationToken);
        return (await cursor.ToListAsync(cancellationToken)).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Task<long> CountAsync(CancellationToken cancellationToken) => CountAsync(null, cancellationToken);

    public Task UpdateAsync(Product product, CancellationToken cancellationToken) => ReplaceAsync(product, cancellationToken);

    public Task DeleteAsync(string brandId, string productId, CancellationToken cancellationToken) =>
        DeleteManyAsync(InBrand(brandId) & F.Eq(p => p.Id, productId), cancellationToken);

    public Task DeleteForBrandAsync(string brandId, CancellationToken cancellationToken) =>
        DeleteManyAsync(InBrand(brandId), cancellationToken);

    public async Task RemoveImageAsync(string brandId, string assetId, CancellationToken cancellationToken) =>
        await Collection.UpdateManyAsync(
            Scoped(InBrand(brandId) & F.AnyEq(p => p.ImageAssetIds, assetId)),
            Builders<Product>.Update.Pull(p => p.ImageAssetIds, assetId),
            cancellationToken: cancellationToken);
}

internal sealed class MediaRepository(MongoContext context, ITenantContext tenantContext, MongoSessionAccessor sessions)
    : TenantScopedRepository<MediaAsset>(context.Media, tenantContext, sessions), IMediaRepository
{
    private static readonly FilterDefinitionBuilder<MediaAsset> F = Builders<MediaAsset>.Filter;

    private static FilterDefinition<MediaAsset> InBrand(string brandId) => F.Eq(a => a.BrandId, brandId);

    public Task AddAsync(MediaAsset asset, CancellationToken cancellationToken) => InsertAsync(asset, cancellationToken);

    public Task<MediaAsset?> GetByIdAsync(string brandId, string assetId, CancellationToken cancellationToken) =>
        FindOneAsync(InBrand(brandId) & F.Eq(a => a.Id, assetId), cancellationToken);

    public Task<IReadOnlyList<MediaAsset>> ListAsync(string brandId, string? tag, CancellationToken cancellationToken) =>
        FindManyAsync(tag is null ? InBrand(brandId) : InBrand(brandId) & F.AnyEq(a => a.Tags, tag), cancellationToken);

    public Task<long> CountExistingAsync(string brandId, IEnumerable<string> assetIds, CancellationToken cancellationToken) =>
        CountAsync(InBrand(brandId) & F.In(a => a.Id, assetIds), cancellationToken);

    public Task UpdateAsync(MediaAsset asset, CancellationToken cancellationToken) => ReplaceAsync(asset, cancellationToken);

    public Task DeleteAsync(string brandId, string assetId, CancellationToken cancellationToken) =>
        DeleteManyAsync(InBrand(brandId) & F.Eq(a => a.Id, assetId), cancellationToken);

    public Task DeleteForBrandAsync(string brandId, CancellationToken cancellationToken) =>
        DeleteManyAsync(InBrand(brandId), cancellationToken);

    // The signed token is the capability (see IMediaUrlSigner); no tenant context exists for <img> requests.
    public async Task<MediaAsset?> FindForDeliveryAsync(string assetId, CancellationToken cancellationToken) =>
        await context.Media.Find(a => a.Id == assetId).FirstOrDefaultAsync(cancellationToken);
}
