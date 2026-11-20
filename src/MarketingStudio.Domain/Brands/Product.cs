using MarketingStudio.Domain.Common;

namespace MarketingStudio.Domain.Brands;

public static class ProductRules
{
    public const int MaxNameLength = 120;
    public const int MaxDescriptionLength = 4000;
    public const int MaxCategoryLength = 60;
    public const int MaxFeatures = 20;
    public const int MaxFeatureLength = 200;
    public const int MaxUrlLength = 500;
    public const int MaxImages = 10;
    public const decimal MaxPrice = 1_000_000_000m;
}

/// <summary>PRD-1: a product of a brand. Agents read these facts; claims about price or features must match them.</summary>
public sealed class Product : ITenantOwned
{
    public string Id { get; set; } = IdGenerator.NewId();
    public string TenantId { get; set; } = string.Empty;
    public string BrandId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public List<string> Features { get; set; } = [];
    public string? Url { get; set; }
    public List<string> ImageAssetIds { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public static class MediaRules
{
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public const int MaxTags = 20;
    public const int MaxTagLength = 40;
    public const int MaxFileNameLength = 200;

    public static readonly IReadOnlyList<string> AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];
}

/// <summary>BRD-3: an image in a brand's media library. The file lives in object storage under the tenant/brand prefix.</summary>
public sealed class MediaAsset : ITenantOwned
{
    public string Id { get; set; } = IdGenerator.NewId();
    public string TenantId { get; set; } = string.Empty;
    public string BrandId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public string UploadedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    /// <summary>Object storage layout: {tenantId}/{brandId}/media/{assetId} (Knowledge Base Design.md uses the same prefix scheme).</summary>
    public static string KeyFor(string tenantId, string brandId, string assetId) => $"{tenantId}/{brandId}/media/{assetId}";

    public static string BrandPrefix(string tenantId, string brandId) => $"{tenantId}/{brandId}/";
}
