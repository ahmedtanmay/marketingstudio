using MarketingStudio.Application.Brands;
using MarketingStudio.Application.Onboarding;
using MarketingStudio.Domain.Brands;
using MarketingStudio.Domain.Users;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace MarketingStudio.Api.Endpoints;

internal static class BrandEndpoints
{
    public static IEndpointRouteBuilder MapBrandEndpoints(this IEndpointRouteBuilder app)
    {
        // ---- BRD-1..5 brands ----
        var brands = app.MapGroup("/api/v1/brands").WithTags("Brands");

        brands.MapGet("/", (ISender s, CancellationToken ct) => s.Send(new ListBrandsQuery(), ct))
            .RequireAuthorization(Permissions.BrandsRead);

        brands.MapGet("/{brandId}", (string brandId, ISender s, CancellationToken ct) => s.Send(new GetBrandQuery(brandId), ct))
            .RequireAuthorization(Permissions.BrandsRead);

        brands.MapPost("/", (BrandProfileInput body, ISender s, CancellationToken ct) => s.Send(new CreateBrandCommand(body), ct))
            .RequireAuthorization(Permissions.BrandsWrite);

        brands.MapPut("/{brandId}", (string brandId, BrandProfileInput body, ISender s, CancellationToken ct) =>
            s.Send(new UpdateBrandProfileCommand(brandId, body), ct))
            .RequireAuthorization(Permissions.BrandsWrite);

        brands.MapPut("/{brandId}/voice", (string brandId, BrandVoiceDto body, ISender s, CancellationToken ct) =>
            s.Send(new UpdateBrandVoiceCommand(brandId, body), ct))
            .RequireAuthorization(Permissions.BrandContentWrite);

        brands.MapDelete("/{brandId}", async (string brandId, [FromQuery] string? confirmName, ISender s, CancellationToken ct) =>
        {
            await s.Send(new DeleteBrandCommand(brandId, confirmName ?? string.Empty), ct);
            return Results.NoContent();
        }).RequireAuthorization(Permissions.BrandsDelete);

        // ---- PRD-1 products ----
        var products = brands.MapGroup("/{brandId}/products").WithTags("Products");

        products.MapGet("/", (string brandId, string? search, string? category, int? page, int? pageSize, ISender s, CancellationToken ct) =>
            s.Send(new SearchProductsQuery(brandId, search, category, page ?? 1, pageSize ?? 25), ct))
            .RequireAuthorization(Permissions.ProductsRead);

        products.MapGet("/categories", (string brandId, ISender s, CancellationToken ct) =>
            s.Send(new ListProductCategoriesQuery(brandId), ct))
            .RequireAuthorization(Permissions.ProductsRead);

        products.MapGet("/{productId}", (string brandId, string productId, ISender s, CancellationToken ct) =>
            s.Send(new GetProductQuery(brandId, productId), ct))
            .RequireAuthorization(Permissions.ProductsRead);

        products.MapPost("/", (string brandId, ProductInput body, ISender s, CancellationToken ct) =>
            s.Send(new CreateProductCommand(brandId, body), ct))
            .RequireAuthorization(Permissions.ProductsWrite);

        products.MapPut("/{productId}", (string brandId, string productId, ProductInput body, ISender s, CancellationToken ct) =>
            s.Send(new UpdateProductCommand(brandId, productId, body), ct))
            .RequireAuthorization(Permissions.ProductsWrite);

        products.MapDelete("/{productId}", async (string brandId, string productId, ISender s, CancellationToken ct) =>
        {
            await s.Send(new DeleteProductCommand(brandId, productId), ct);
            return Results.NoContent();
        }).RequireAuthorization(Permissions.ProductsWrite);

        // ---- BRD-3 media library ----
        var media = brands.MapGroup("/{brandId}/media").WithTags("Media");

        media.MapGet("/", (string brandId, string? tag, ISender s, CancellationToken ct) => s.Send(new ListMediaQuery(brandId, tag), ct))
            .RequireAuthorization(Permissions.BrandsRead);

        media.MapPost("/", async (string brandId, IFormFile file, [FromForm] string? tags, ISender s, CancellationToken ct) =>
        {
            await using var stream = file.OpenReadStream();
            var tagList = (tags ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return await s.Send(new UploadMediaCommand(brandId, file.FileName, file.Length, stream, tagList), ct);
        })
            .RequireAuthorization(Permissions.BrandContentWrite)
            // Bearer tokens, not cookies, authenticate this call, so antiforgery tokens add nothing here.
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MediaRules.MaxFileBytes + (512 * 1024)));

        media.MapPut("/{assetId}/tags", (string brandId, string assetId, TagsRequest body, ISender s, CancellationToken ct) =>
            s.Send(new UpdateMediaTagsCommand(brandId, assetId, body.Tags), ct))
            .RequireAuthorization(Permissions.BrandContentWrite);

        media.MapDelete("/{assetId}", async (string brandId, string assetId, ISender s, CancellationToken ct) =>
        {
            await s.Send(new DeleteMediaCommand(brandId, assetId), ct);
            return Results.NoContent();
        }).RequireAuthorization(Permissions.BrandContentWrite);

        // Signed, expiring image links (no access token: <img> tags can't send one). Invalid = 404.
        app.MapGet("/api/v1/media/files/{token}", async (string token, ISender s, HttpResponse response, CancellationToken ct) =>
        {
            var file = await s.Send(new OpenMediaFileQuery(token), ct);
            response.Headers.CacheControl = "private, max-age=3600";
            return Results.Stream(file.Content, file.ContentType, enableRangeProcessing: false);
        }).WithTags("Media").AllowAnonymous();

        // ---- TEN-1 onboarding checklist ----
        app.MapGet("/api/v1/onboarding", (ISender s, CancellationToken ct) => s.Send(new GetOnboardingQuery(), ct))
            .WithTags("Onboarding")
            .RequireAuthorization();

        return app;
    }
}

public sealed record TagsRequest(IReadOnlyList<string> Tags);
