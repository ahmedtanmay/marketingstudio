using MarketingStudio.Application.Knowledge;
using MarketingStudio.Domain.Users;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace MarketingStudio.Api.Endpoints;

public sealed record KnowledgeSearchRequest(string Query, int? TopK, IReadOnlyList<string>? SourceTypes);

/// <summary>KB-1..4 (Knowledge Base Design.md → API Endpoints), nested under the brand like products and media.</summary>
internal static class KnowledgeEndpoints
{
    /// <summary>Above the largest per-file plan limit; the handler applies the plan's own limit.</summary>
    private const long MaxRequestBytes = 51 * 1024 * 1024;

    public static IEndpointRouteBuilder MapKnowledgeEndpoints(this IEndpointRouteBuilder app)
    {
        var kb = app.MapGroup("/api/v1/brands/{brandId}/knowledge").WithTags("Knowledge");

        // ---- KB-1/KB-2 documents ----
        kb.MapGet("/documents", (string brandId, ISender s, CancellationToken ct) => s.Send(new ListKnowledgeDocumentsQuery(brandId), ct))
            .RequireAuthorization(Permissions.BrandsRead);

        kb.MapGet("/documents/{documentId}", (string brandId, string documentId, ISender s, CancellationToken ct) =>
            s.Send(new GetKnowledgeDocumentQuery(brandId, documentId), ct))
            .RequireAuthorization(Permissions.BrandsRead);

        kb.MapPost("/documents", async (string brandId, IFormFile file, ISender s, CancellationToken ct) =>
        {
            await using var stream = file.OpenReadStream();
            return await s.Send(new UploadKnowledgeDocumentCommand(brandId, file.FileName, file.Length, stream), ct);
        })
            .RequireAuthorization(Permissions.KnowledgeWrite)
            .DisableAntiforgery() // bearer-token auth, not cookies
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes));

        kb.MapPost("/documents/{documentId}/retry", (string brandId, string documentId, ISender s, CancellationToken ct) =>
            s.Send(new RetryKnowledgeDocumentCommand(brandId, documentId), ct))
            .RequireAuthorization(Permissions.KnowledgeWrite);

        kb.MapDelete("/documents/{documentId}", async (string brandId, string documentId, ISender s, CancellationToken ct) =>
        {
            await s.Send(new DeleteKnowledgeDocumentCommand(brandId, documentId), ct);
            return Results.NoContent();
        }).RequireAuthorization(Permissions.KnowledgeWrite);

        kb.MapGet("/usage", (string brandId, ISender s, CancellationToken ct) => s.Send(new GetKnowledgeUsageQuery(brandId), ct))
            .RequireAuthorization(Permissions.BrandsRead);

        // ---- KB-3 FAQs ----
        kb.MapGet("/faqs", (string brandId, ISender s, CancellationToken ct) => s.Send(new ListFaqsQuery(brandId), ct))
            .RequireAuthorization(Permissions.BrandsRead);

        kb.MapPost("/faqs", (string brandId, FaqInput body, ISender s, CancellationToken ct) => s.Send(new CreateFaqCommand(brandId, body), ct))
            .RequireAuthorization(Permissions.KnowledgeWrite);

        kb.MapPut("/faqs/{faqId}", (string brandId, string faqId, FaqInput body, ISender s, CancellationToken ct) =>
            s.Send(new UpdateFaqCommand(brandId, faqId, body), ct))
            .RequireAuthorization(Permissions.KnowledgeWrite);

        kb.MapDelete("/faqs/{faqId}", async (string brandId, string faqId, ISender s, CancellationToken ct) =>
        {
            await s.Send(new DeleteFaqCommand(brandId, faqId), ct);
            return Results.NoContent();
        }).RequireAuthorization(Permissions.KnowledgeWrite);

        // ---- KB-4 test search (same path as the agents' search_knowledge_base tool) ----
        kb.MapPost("/search", (string brandId, KnowledgeSearchRequest body, ISender s, CancellationToken ct) =>
            s.Send(new SearchKnowledgeQuery(brandId, body.Query, body.TopK, body.SourceTypes), ct))
            .RequireAuthorization(Permissions.BrandsRead);

        return app;
    }
}
