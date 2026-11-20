using FluentValidation;
using FluentValidation.Results;
using MarketingStudio.Application.Abstractions;
using MarketingStudio.Domain.Brands;
using MediatR;

namespace MarketingStudio.Application.Brands;

public sealed record ProductDto(
    string Id,
    string BrandId,
    string Name,
    string? Description,
    string? Category,
    decimal? Price,
    string? Currency,
    IReadOnlyList<string> Features,
    string? Url,
    IReadOnlyList<string> ImageAssetIds,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ProductInput(
    string Name,
    string? Description,
    string? Category,
    decimal? Price,
    string? Currency,
    IReadOnlyList<string>? Features,
    string? Url,
    IReadOnlyList<string>? ImageAssetIds);

public sealed class ProductInputValidator : AbstractValidator<ProductInput>
{
    public ProductInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ProductRules.MaxNameLength);
        RuleFor(x => x.Description).MaximumLength(ProductRules.MaxDescriptionLength);
        RuleFor(x => x.Category).MaximumLength(ProductRules.MaxCategoryLength);
        RuleFor(x => x.Price).InclusiveBetween(0, ProductRules.MaxPrice).When(x => x.Price.HasValue);
        RuleFor(x => x.Currency).NotEmpty().WithMessage("Choose a currency for the price.")
            .Matches("^[A-Z]{3}$").WithMessage("Use a 3-letter currency code such as INR or USD.")
            .When(x => x.Price.HasValue);
        RuleFor(x => x.Features).Must(f => f is null || f.Count <= ProductRules.MaxFeatures)
            .WithMessage($"Add at most {ProductRules.MaxFeatures} key features.");
        RuleForEach(x => x.Features).NotEmpty().MaximumLength(ProductRules.MaxFeatureLength);
        RuleFor(x => x.Url).MaximumLength(ProductRules.MaxUrlLength)
            .Must(u => string.IsNullOrWhiteSpace(u) || (Uri.TryCreate(u.Trim(), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)))
            .WithMessage("Enter a full web address starting with https://");
        RuleFor(x => x.ImageAssetIds).Must(i => i is null || i.Count <= ProductRules.MaxImages)
            .WithMessage($"Add at most {ProductRules.MaxImages} images.");
    }
}

internal static class ProductMapping
{
    public static ProductDto ToDto(this Product p) => new(
        p.Id, p.BrandId, p.Name, p.Description, p.Category, p.Price, p.Currency, p.Features, p.Url, p.ImageAssetIds,
        p.CreatedAt, p.UpdatedAt);

    public static async Task ApplyAsync(this Product product, ProductInput input, IMediaRepository media, CancellationToken ct)
    {
        var images = (input.ImageAssetIds ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (images.Count > 0 && await media.CountExistingAsync(product.BrandId, images, ct) != images.Count)
        {
            throw new ValidationException([new ValidationFailure("imageAssetIds", "Pick images from this brand's media library.")]);
        }

        product.Name = input.Name.Trim();
        product.Description = BrandMapping.Clean(input.Description);
        product.Category = BrandMapping.Clean(input.Category);
        product.Price = input.Price;
        product.Currency = input.Price.HasValue ? input.Currency?.Trim().ToUpperInvariant() : null;
        product.Features = (input.Features ?? []).Select(f => f.Trim()).Where(f => f.Length > 0).ToList();
        product.Url = BrandMapping.Clean(input.Url);
        product.ImageAssetIds = images;
    }
}

public sealed record SearchProductsQuery(string BrandId, string? Search, string? Category, int Page = 1, int PageSize = 25)
    : IRequest<PagedResult<ProductDto>>;

public sealed class SearchProductsQueryValidator : AbstractValidator<SearchProductsQuery>
{
    public SearchProductsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Category).MaximumLength(ProductRules.MaxCategoryLength);
    }
}

/// <summary>PRD-1: list with search by name, category filter and paging (25 per page).</summary>
public sealed class SearchProductsQueryHandler(IBrandRepository brands, IProductRepository products)
    : IRequestHandler<SearchProductsQuery, PagedResult<ProductDto>>
{
    public async Task<PagedResult<ProductDto>> Handle(SearchProductsQuery request, CancellationToken cancellationToken)
    {
        await brands.RequireAsync(request.BrandId, cancellationToken);
        var page = await products.SearchAsync(
            new ProductQuery(request.BrandId, request.Search?.Trim(), request.Category?.Trim(), request.Page, request.PageSize),
            cancellationToken);
        return new PagedResult<ProductDto>(page.Items.Select(p => p.ToDto()).ToList(), page.Total, page.Page, page.PageSize);
    }
}

public sealed record ListProductCategoriesQuery(string BrandId) : IRequest<IReadOnlyList<string>>;

public sealed class ListProductCategoriesQueryHandler(IBrandRepository brands, IProductRepository products)
    : IRequestHandler<ListProductCategoriesQuery, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(ListProductCategoriesQuery request, CancellationToken cancellationToken)
    {
        await brands.RequireAsync(request.BrandId, cancellationToken);
        return await products.CategoriesAsync(request.BrandId, cancellationToken);
    }
}

public sealed record GetProductQuery(string BrandId, string ProductId) : IRequest<ProductDto>;

public sealed class GetProductQueryHandler(IProductRepository products) : IRequestHandler<GetProductQuery, ProductDto>
{
    public async Task<ProductDto> Handle(GetProductQuery request, CancellationToken cancellationToken) =>
        (await products.GetByIdAsync(request.BrandId, request.ProductId, cancellationToken)
            ?? throw new NotFoundException("Product not found.")).ToDto();
}

public sealed record CreateProductCommand(string BrandId, ProductInput Product) : IRequest<ProductDto>;

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator() => RuleFor(x => x.Product).NotNull().SetValidator(new ProductInputValidator());
}

public sealed class CreateProductCommandHandler(
    IBrandRepository brands,
    IProductRepository products,
    IMediaRepository media,
    ITenantContext context,
    TimeProvider clock) : IRequestHandler<CreateProductCommand, ProductDto>
{
    public async Task<ProductDto> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var brand = await brands.RequireAsync(request.BrandId, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var product = new Product { TenantId = context.RequireTenantId(), BrandId = brand.Id, CreatedAt = now, UpdatedAt = now };
        await product.ApplyAsync(request.Product, media, cancellationToken);
        await products.AddAsync(product, cancellationToken);
        return product.ToDto();
    }
}

public sealed record UpdateProductCommand(string BrandId, string ProductId, ProductInput Product) : IRequest<ProductDto>;

public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator() => RuleFor(x => x.Product).NotNull().SetValidator(new ProductInputValidator());
}

public sealed class UpdateProductCommandHandler(IProductRepository products, IMediaRepository media, TimeProvider clock)
    : IRequestHandler<UpdateProductCommand, ProductDto>
{
    public async Task<ProductDto> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetByIdAsync(request.BrandId, request.ProductId, cancellationToken)
            ?? throw new NotFoundException("Product not found.");
        await product.ApplyAsync(request.Product, media, cancellationToken);
        product.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await products.UpdateAsync(product, cancellationToken);
        return product.ToDto();
    }
}

public sealed record DeleteProductCommand(string BrandId, string ProductId) : IRequest;

public sealed class DeleteProductCommandHandler(IProductRepository products) : IRequestHandler<DeleteProductCommand>
{
    public async Task Handle(DeleteProductCommand request, CancellationToken cancellationToken)
    {
        _ = await products.GetByIdAsync(request.BrandId, request.ProductId, cancellationToken)
            ?? throw new NotFoundException("Product not found.");
        await products.DeleteAsync(request.BrandId, request.ProductId, cancellationToken);
    }
}
