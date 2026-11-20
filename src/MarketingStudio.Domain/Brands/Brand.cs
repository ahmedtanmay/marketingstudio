using MarketingStudio.Domain.Common;

namespace MarketingStudio.Domain.Brands;

/// <summary>Limits from User Stories and Acceptance Criteria.md (BRD-1, BRD-2).</summary>
public static class BrandRules
{
    public const int MaxNameLength = 80;
    public const int MaxIndustryLength = 80;
    public const int MaxDescriptionLength = 1000;
    public const int MaxAudienceLength = 1000;
    public const int MaxLanguages = 10;
    public const int MaxGuidelinesLength = 2000;
    public const int MaxExamplePosts = 3;
    public const int MaxExamplePostLength = 2200;
    public const int MaxBannedTerms = 200;
    public const int MaxBannedTermLength = 60;
}

public static class TonePresets
{
    public const string Friendly = "Friendly";
    public const string Professional = "Professional";
    public const string Playful = "Playful";
    public const string Custom = "Custom";

    public static readonly IReadOnlyList<string> All = [Friendly, Professional, Playful, Custom];
}

/// <summary>BRD-2: how the brand sounds. Banned terms are enforced later by the AI output guardrails.</summary>
public sealed class BrandVoice
{
    public string TonePreset { get; set; } = TonePresets.Friendly;
    public string Guidelines { get; set; } = string.Empty;
    public List<string> ExamplePosts { get; set; } = [];
    public List<string> BannedTerms { get; set; } = [];
}

/// <summary>BRD-1: a brand profile within a tenant. Names are unique per tenant (case-insensitive).</summary>
public sealed class Brand : ITenantOwned
{
    public string Id { get; set; } = IdGenerator.NewId();
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? TargetAudience { get; set; }
    public List<string> Languages { get; set; } = [];
    public string Timezone { get; set; } = "UTC";
    public BrandVoice Voice { get; set; } = new();
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static Brand Create(string tenantId, string name, string createdBy, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var brand = new Brand { TenantId = tenantId, CreatedBy = createdBy, CreatedAt = nowUtc, UpdatedAt = nowUtc };
        brand.Rename(name);
        return brand;
    }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        NormalizedName = NormalizeName(Name);
    }

    public static string NormalizeName(string name) => name.Trim().ToUpperInvariant();
}
