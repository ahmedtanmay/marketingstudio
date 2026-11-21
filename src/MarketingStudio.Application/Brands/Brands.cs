using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Results;
using MarketingStudio.Application.Abstractions;
using MarketingStudio.Application.Common;
using MarketingStudio.Domain.Audit;
using MarketingStudio.Domain.Billing;
using MarketingStudio.Domain.Brands;
using MediatR;

namespace MarketingStudio.Application.Brands;

public sealed record BrandVoiceDto(string TonePreset, string Guidelines, IReadOnlyList<string> ExamplePosts, IReadOnlyList<string> BannedTerms);

public sealed record BrandDto(
    string Id,
    string Name,
    string Industry,
    string? Description,
    string? WebsiteUrl,
    string? TargetAudience,
    IReadOnlyList<string> Languages,
    string Timezone,
    BrandVoiceDto Voice,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record BrandSummary(string Id, string Name, string Industry);

/// <summary>Profile fields shared by create and update (BRD-1).</summary>
public sealed record BrandProfileInput(
    string Name,
    string Industry,
    IReadOnlyList<string> Languages,
    string Timezone,
    string? Description,
    string? WebsiteUrl,
    string? TargetAudience);

internal static partial class BrandMapping
{
    public static BrandDto ToDto(this Brand b) => new(
        b.Id, b.Name, b.Industry, b.Description, b.WebsiteUrl, b.TargetAudience, b.Languages, b.Timezone,
        new BrandVoiceDto(b.Voice.TonePreset, b.Voice.Guidelines, b.Voice.ExamplePosts, b.Voice.BannedTerms),
        b.CreatedAt, b.UpdatedAt);

    public static void Apply(this Brand brand, BrandProfileInput input)
    {
        brand.Rename(input.Name);
        brand.Industry = input.Industry.Trim();
        brand.Languages = input.Languages.Select(l => l.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        brand.Timezone = input.Timezone.Trim();
        brand.Description = Clean(input.Description);
        brand.WebsiteUrl = Clean(input.WebsiteUrl);
        brand.TargetAudience = Clean(input.TargetAudience);
    }

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$")]
    public static partial Regex LanguageTag();
}

public sealed class BrandProfileInputValidator : AbstractValidator<BrandProfileInput>
{
    public BrandProfileInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(BrandRules.MaxNameLength);
        RuleFor(x => x.Industry).NotEmpty().MaximumLength(BrandRules.MaxIndustryLength);
        RuleFor(x => x.Languages).NotEmpty().WithMessage("Choose at least one language.")
            .Must(l => l.Count <= BrandRules.MaxLanguages).WithMessage($"Choose at most {BrandRules.MaxLanguages} languages.");
        RuleForEach(x => x.Languages).Must(l => BrandMapping.LanguageTag().IsMatch(l.Trim()))
            .WithMessage("Use language codes such as en, hi or en-GB.");
        RuleFor(x => x.Timezone).NotEmpty()
            .Must(tz => TimeZoneInfo.TryFindSystemTimeZoneById(tz.Trim(), out _))
            .WithMessage("Choose a valid time zone, e.g. Asia/Kolkata.");
        RuleFor(x => x.Description).MaximumLength(BrandRules.MaxDescriptionLength);
        RuleFor(x => x.TargetAudience).MaximumLength(BrandRules.MaxAudienceLength);
        RuleFor(x => x.WebsiteUrl).MaximumLength(500)
            .Must(u => string.IsNullOrWhiteSpace(u) || (Uri.TryCreate(u.Trim(), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)))
            .WithMessage("Enter a full web address starting with https://");
    }
}

internal static class BrandGuards
{
    public const string DuplicateName = "You already have a brand with this name.";

    public static async Task<Brand> RequireAsync(this IBrandRepository brands, string brandId, CancellationToken ct) =>
        await brands.GetByIdAsync(brandId, ct) ?? throw new NotFoundException("Brand not found.");

    public static async Task EnsureUniqueNameAsync(this IBrandRepository brands, string name, string? exceptId, CancellationToken ct)
    {
        if (await brands.NameExistsAsync(Brand.NormalizeName(name), exceptId, ct))
        {
            throw new ValidationException([new ValidationFailure("name", DuplicateName)]);
        }
    }
}

// ---------------------------------------------------------------- queries

public sealed record ListBrandsQuery : IRequest<IReadOnlyList<BrandSummary>>;

public sealed class ListBrandsQueryHandler(IBrandRepository brands) : IRequestHandler<ListBrandsQuery, IReadOnlyList<BrandSummary>>
{
    public async Task<IReadOnlyList<BrandSummary>> Handle(ListBrandsQuery request, CancellationToken cancellationToken) =>
        (await brands.ListAsync(cancellationToken))
            .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .Select(b => new BrandSummary(b.Id, b.Name, b.Industry))
            .ToList();
}

public sealed record GetBrandQuery(string BrandId) : IRequest<BrandDto>;

public sealed class GetBrandQueryHandler(IBrandRepository brands) : IRequestHandler<GetBrandQuery, BrandDto>
{
    public async Task<BrandDto> Handle(GetBrandQuery request, CancellationToken cancellationToken) =>
        (await brands.RequireAsync(request.BrandId, cancellationToken)).ToDto();
}

// ---------------------------------------------------------------- BRD-1 create

public sealed record CreateBrandCommand(BrandProfileInput Profile) : IRequest<BrandDto>;

public sealed class CreateBrandCommandValidator : AbstractValidator<CreateBrandCommand>
{
    public CreateBrandCommandValidator() => RuleFor(x => x.Profile).NotNull().SetValidator(new BrandProfileInputValidator());
}

public sealed class CreateBrandCommandHandler(
    IBrandRepository brands,
    ISubscriptionRepository subscriptions,
    IPlanLimits limits,
    ITenantContext context,
    IAuditLog audit,
    IClientInfo client,
    TimeProvider clock) : IRequestHandler<CreateBrandCommand, BrandDto>
{
    public const string LimitKey = "brands.max";

    public async Task<BrandDto> Handle(CreateBrandCommand request, CancellationToken cancellationToken)
    {
        var plan = (await subscriptions.GetForCurrentTenantAsync(cancellationToken))?.PlanId ?? Subscription.TrialPlanId;
        var max = limits.MaxBrands(plan);
        if (await brands.CountAsync(cancellationToken) >= max)
        {
            throw new PlanLimitException(LimitKey, max, $"Your plan includes {max} brand{(max == 1 ? "" : "s")}. Upgrade to add more.");
        }

        await brands.EnsureUniqueNameAsync(request.Profile.Name, null, cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        var brand = Brand.Create(context.RequireTenantId(), request.Profile.Name, context.RequireUserId(), now);
        brand.Apply(request.Profile);
        await brands.AddAsync(brand, cancellationToken);

        await audit.RecordAsync(AuditActions.BrandCreated, brand.TenantId, context.UserId, "brand", brand.Id, client.IpAddress, now, cancellationToken);
        return brand.ToDto();
    }
}

// ---------------------------------------------------------------- BRD-1 update profile

public sealed record UpdateBrandProfileCommand(string BrandId, BrandProfileInput Profile) : IRequest<BrandDto>;

public sealed class UpdateBrandProfileCommandValidator : AbstractValidator<UpdateBrandProfileCommand>
{
    public UpdateBrandProfileCommandValidator() => RuleFor(x => x.Profile).NotNull().SetValidator(new BrandProfileInputValidator());
}

public sealed class UpdateBrandProfileCommandHandler(
    IBrandRepository brands,
    ITenantContext context,
    IAuditLog audit,
    IClientInfo client,
    TimeProvider clock) : IRequestHandler<UpdateBrandProfileCommand, BrandDto>
{
    public async Task<BrandDto> Handle(UpdateBrandProfileCommand request, CancellationToken cancellationToken)
    {
        var brand = await brands.RequireAsync(request.BrandId, cancellationToken);
        await brands.EnsureUniqueNameAsync(request.Profile.Name, brand.Id, cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        brand.Apply(request.Profile);
        brand.UpdatedAt = now;
        await brands.UpdateAsync(brand, cancellationToken);

        await audit.RecordAsync(AuditActions.BrandUpdated, brand.TenantId, context.UserId, "brand", brand.Id, client.IpAddress, now,
            cancellationToken, new Dictionary<string, string> { ["section"] = "profile" });
        return brand.ToDto();
    }
}

// ---------------------------------------------------------------- BRD-2 voice

public sealed record UpdateBrandVoiceCommand(string BrandId, BrandVoiceDto Voice) : IRequest<BrandDto>;

public sealed class UpdateBrandVoiceCommandValidator : AbstractValidator<UpdateBrandVoiceCommand>
{
    public UpdateBrandVoiceCommandValidator()
    {
        RuleFor(x => x.Voice).NotNull();
        RuleFor(x => x.Voice.TonePreset).Must(t => TonePresets.All.Contains(t))
            .WithMessage($"Tone must be one of: {string.Join(", ", TonePresets.All)}.").When(x => x.Voice is not null);
        RuleFor(x => x.Voice.Guidelines).MaximumLength(BrandRules.MaxGuidelinesLength).When(x => x.Voice is not null);
        RuleFor(x => x.Voice.ExamplePosts)
            .Must(p => p.Count <= BrandRules.MaxExamplePosts).WithMessage($"Add at most {BrandRules.MaxExamplePosts} example posts.")
            .When(x => x.Voice is not null);
        RuleForEach(x => x.Voice.ExamplePosts).NotEmpty().MaximumLength(BrandRules.MaxExamplePostLength).When(x => x.Voice is not null);
        RuleFor(x => x.Voice.BannedTerms)
            .Must(t => t.Count <= BrandRules.MaxBannedTerms).WithMessage($"Add at most {BrandRules.MaxBannedTerms} banned words or topics.")
            .When(x => x.Voice is not null);
        RuleForEach(x => x.Voice.BannedTerms).NotEmpty().MaximumLength(BrandRules.MaxBannedTermLength).When(x => x.Voice is not null);
    }
}

public sealed class UpdateBrandVoiceCommandHandler(
    IBrandRepository brands,
    ITenantContext context,
    IAuditLog audit,
    IClientInfo client,
    TimeProvider clock) : IRequestHandler<UpdateBrandVoiceCommand, BrandDto>
{
    public async Task<BrandDto> Handle(UpdateBrandVoiceCommand request, CancellationToken cancellationToken)
    {
        var brand = await brands.RequireAsync(request.BrandId, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;

        brand.Voice = new BrandVoice
        {
            TonePreset = request.Voice.TonePreset,
            Guidelines = request.Voice.Guidelines.Trim(),
            ExamplePosts = request.Voice.ExamplePosts.Select(p => p.Trim()).ToList(),
            BannedTerms = request.Voice.BannedTerms.Select(t => t.Trim()).Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
        brand.UpdatedAt = now;
        await brands.UpdateAsync(brand, cancellationToken);

        await audit.RecordAsync(AuditActions.BrandUpdated, brand.TenantId, context.UserId, "brand", brand.Id, client.IpAddress, now,
            cancellationToken, new Dictionary<string, string> { ["section"] = "voice" });
        return brand.ToDto();
    }
}

// ---------------------------------------------------------------- BRD-5 delete

/// <summary>
/// BRD-5: delete a brand and everything under it (products, media files). The caller must type the
/// brand's name. Removes products, media, knowledge documents, FAQs and their search index entries.
/// Scheduled posts and channels are added to this clean-up when they exist.
/// </summary>
public sealed record DeleteBrandCommand(string BrandId, string ConfirmName) : IRequest;

public sealed class DeleteBrandCommandHandler(
    IBrandRepository brands,
    IProductRepository products,
    IMediaRepository media,
    IKnowledgeDocumentRepository knowledgeDocuments,
    IFaqRepository faqs,
    IVectorIndex knowledgeIndex,
    IFileStorage storage,
    ITenantContext context,
    IAuditLog audit,
    IClientInfo client,
    TimeProvider clock) : IRequestHandler<DeleteBrandCommand>
{
    public const string ConfirmMismatch = "Type the brand's name exactly to confirm.";

    public async Task Handle(DeleteBrandCommand request, CancellationToken cancellationToken)
    {
        var brand = await brands.RequireAsync(request.BrandId, cancellationToken);
        if (!string.Equals(request.ConfirmName?.Trim(), brand.Name, StringComparison.Ordinal))
        {
            throw new ValidationException([new ValidationFailure("confirmName", ConfirmMismatch)]);
        }

        // Search index first (nothing findable), then files, then records.
        await knowledgeIndex.DeleteBrandAsync(brand.TenantId, brand.Id, cancellationToken);
        await storage.DeletePrefixAsync(MediaAsset.BrandPrefix(brand.TenantId, brand.Id), cancellationToken);
        await knowledgeDocuments.DeleteForBrandAsync(brand.Id, cancellationToken);
        await faqs.DeleteForBrandAsync(brand.Id, cancellationToken);
        await media.DeleteForBrandAsync(brand.Id, cancellationToken);
        await products.DeleteForBrandAsync(brand.Id, cancellationToken);
        await brands.DeleteAsync(brand.Id, cancellationToken);

        await audit.RecordAsync(AuditActions.BrandDeleted, brand.TenantId, context.UserId, "brand", brand.Id, client.IpAddress,
            clock.GetUtcNow().UtcDateTime, cancellationToken, new Dictionary<string, string> { ["name"] = brand.Name });
    }
}
