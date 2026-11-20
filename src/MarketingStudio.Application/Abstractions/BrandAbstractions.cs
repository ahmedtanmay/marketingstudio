using MarketingStudio.Domain.Brands;

namespace MarketingStudio.Application.Abstractions;

/// <summary>Tenant-scoped (caller's tenant).</summary>
public interface IBrandRepository
{
    Task AddAsync(Brand brand, CancellationToken cancellationToken);
    Task<Brand?> GetByIdAsync(string brandId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Brand>> ListAsync(CancellationToken cancellationToken);
    Task<long> CountAsync(CancellationToken cancellationToken);
    Task<bool> NameExistsAsync(string normalizedName, string? exceptBrandId, CancellationToken cancellationToken);
    Task UpdateAsync(Brand brand, CancellationToken cancellationToken);
    Task DeleteAsync(string brandId, CancellationToken cancellationToken);
}

public sealed record ProductQuery(string BrandId, string? Search, string? Category, int Page, int PageSize);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, long Total, int Page, int PageSize);

/// <summary>Tenant-scoped (caller's tenant).</summary>
public interface IProductRepository
{
    Task AddAsync(Product product, CancellationToken cancellationToken);
    Task<Product?> GetByIdAsync(string brandId, string productId, CancellationToken cancellationToken);
    Task<PagedResult<Product>> SearchAsync(ProductQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> CategoriesAsync(string brandId, CancellationToken cancellationToken);
    Task<long> CountAsync(CancellationToken cancellationToken);
    Task UpdateAsync(Product product, CancellationToken cancellationToken);
    Task DeleteAsync(string brandId, string productId, CancellationToken cancellationToken);
    Task DeleteForBrandAsync(string brandId, CancellationToken cancellationToken);

    /// <summary>Removes an image reference from every product of the brand (when the image is deleted).</summary>
    Task RemoveImageAsync(string brandId, string assetId, CancellationToken cancellationToken);
}

/// <summary>Tenant-scoped (caller's tenant), except <see cref="FindForDeliveryAsync"/>.</summary>
public interface IMediaRepository
{
    Task AddAsync(MediaAsset asset, CancellationToken cancellationToken);
    Task<MediaAsset?> GetByIdAsync(string brandId, string assetId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MediaAsset>> ListAsync(string brandId, string? tag, CancellationToken cancellationToken);
    Task<long> CountExistingAsync(string brandId, IEnumerable<string> assetIds, CancellationToken cancellationToken);
    Task UpdateAsync(MediaAsset asset, CancellationToken cancellationToken);
    Task DeleteAsync(string brandId, string assetId, CancellationToken cancellationToken);
    Task DeleteForBrandAsync(string brandId, CancellationToken cancellationToken);

    /// <summary>Lookup by id for signed-link delivery. The signed token is the capability, so this is not tenant-scoped.</summary>
    Task<MediaAsset?> FindForDeliveryAsync(string assetId, CancellationToken cancellationToken);
}

/// <summary>Object storage (MinIO in self-hosted deployments; any S3-compatible store).</summary>
public interface IFileStorage
{
    Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
    Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken);
}

/// <summary>
/// Short-lived, unguessable links for images, so &lt;img&gt; tags (and later Instagram) can fetch them
/// without an access token while storage stays private.
/// </summary>
public interface IMediaUrlSigner
{
    string CreateUrl(string assetId, TimeSpan lifetime);

    /// <summary>Returns the asset id if the token is valid and unexpired; otherwise null.</summary>
    string? ReadToken(string token);
}
