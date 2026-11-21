using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using FluentValidation.Results;
using MarketingStudio.Application.Abstractions;
using MarketingStudio.Application.Brands;
using MarketingStudio.Domain.Billing;
using MarketingStudio.Domain.Knowledge;
using MediatR;

namespace MarketingStudio.Application.Knowledge;

public sealed record KnowledgeDocumentDto(
    string Id,
    string BrandId,
    string FileName,
    long SizeBytes,
    string Status,
    string? FailureReason,
    int ChunkCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record KnowledgeUsage(long Documents, long MaxDocuments, long StorageBytes, long MaxStorageBytes, long MaxFileBytes);

public sealed record FaqDto(string Id, string BrandId, string Question, string Answer, DateTime UpdatedAt);

public sealed record SearchHitDto(string SourceType, string SourceId, string Title, string HeadingPath, string Text, double Score);

public sealed record KnowledgeSearchResult(IReadOnlyList<SearchHitDto> Hits, bool LowConfidence);

/// <summary>Minimum similarity for a hit to count; tuned per embedding model (Knowledge Base Design.md → Retrieval).</summary>
public sealed class KnowledgeSearchOptions
{
    public const string Section = "Knowledge";
    public double MinScore { get; set; } = 0.35;
}

internal static class KnowledgeMapping
{
    public static KnowledgeDocumentDto ToDto(this KnowledgeDocument d) =>
        new(d.Id, d.BrandId, d.FileName, d.SizeBytes, d.Status.ToString(), d.FailureReason, d.ChunkCount, d.CreatedAt, d.UpdatedAt);

    public static FaqDto ToDto(this Faq f) => new(f.Id, f.BrandId, f.Question, f.Answer, f.UpdatedAt);
}

/// <summary>Checks the first bytes match the claimed kind of document (never trust the extension alone).</summary>
public static class DocumentSniffer
{
    public static bool Matches(string extension, ReadOnlySpan<byte> head) => extension switch
    {
        ".pdf" => head.StartsWith("%PDF-"u8),
        ".docx" => head.StartsWith((byte[])[0x50, 0x4B, 0x03, 0x04]),
        ".txt" or ".md" or ".html" or ".htm" => IsText(head),
        _ => false,
    };

    private static bool IsText(ReadOnlySpan<byte> head)
    {
        if (head.IndexOf((byte)0) >= 0)
        {
            return false;
        }

        try
        {
            // Throws on invalid UTF-8 (a truncated multi-byte character at the very end is tolerated).
            new UTF8Encoding(false, true).GetString(head.Length > 4 ? head[..^4] : head);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}

// ================================================================ KB-1 upload

public sealed record UploadKnowledgeDocumentCommand(string BrandId, string? FileName, long Length, Stream Content)
    : IRequest<KnowledgeDocumentDto>;

public sealed class UploadKnowledgeDocumentCommandHandler(
    IBrandRepository brands,
    IKnowledgeDocumentRepository documents,
    ISubscriptionRepository subscriptions,
    IPlanLimits limits,
    IFileStorage storage,
    IKnowledgeJobScheduler queue,
    ITenantContext context,
    TimeProvider clock) : IRequestHandler<UploadKnowledgeDocumentCommand, KnowledgeDocumentDto>
{
    public const string WrongType = "Upload a PDF, Word (.docx), text, Markdown or HTML file.";
    public const string Duplicate = "This file is already in the knowledge base.";

    public async Task<KnowledgeDocumentDto> Handle(UploadKnowledgeDocumentCommand request, CancellationToken cancellationToken)
    {
        var brand = await brands.RequireAsync(request.BrandId, cancellationToken);
        var fileName = ImageSniffer.SafeFileName(request.FileName);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!KnowledgeRules.ContentTypes.TryGetValue(extension, out var contentType))
        {
            throw Invalid(WrongType);
        }

        var plan = (await subscriptions.GetForCurrentTenantAsync(cancellationToken))?.PlanId ?? Subscription.TrialPlanId;
        var maxFile = limits.MaxKnowledgeFileBytes(plan);
        if (request.Length <= 0 || request.Length > maxFile)
        {
            throw Invalid(request.Length <= 0 ? "The file is empty." : $"Files can be at most {maxFile / (1024 * 1024)} MB on your plan.");
        }

        var maxDocs = limits.MaxKnowledgeDocumentsPerBrand(plan);
        if (await documents.CountForBrandAsync(brand.Id, cancellationToken) >= maxDocs)
        {
            throw new PlanLimitException("kb.docsPerBrand", maxDocs, $"Your plan allows {maxDocs} documents per brand. Delete some or upgrade.");
        }

        var maxBytes = limits.MaxKnowledgeStorageBytes(plan);
        if (await documents.TotalBytesAsync(cancellationToken) + request.Length > maxBytes)
        {
            throw new PlanLimitException("kb.storageBytes", maxBytes, $"Your plan includes {maxBytes / (1024 * 1024)} MB of knowledge storage. Delete some documents or upgrade.");
        }

        using var buffer = new MemoryStream();
        await request.Content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > maxFile)
        {
            throw Invalid($"Files can be at most {maxFile / (1024 * 1024)} MB on your plan.");
        }

        if (!DocumentSniffer.Matches(extension, buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 4096))))
        {
            throw Invalid($"The file's contents don't match its {extension} extension.");
        }

        var checksum = Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)));
        if (await documents.ChecksumExistsAsync(brand.Id, checksum, cancellationToken))
        {
            throw Invalid(Duplicate);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var doc = new KnowledgeDocument
        {
            TenantId = context.RequireTenantId(),
            BrandId = brand.Id,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = buffer.Length,
            Checksum = checksum,
            UploadedBy = context.RequireUserId(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        doc.StorageKey = KnowledgeDocument.KeyFor(doc.TenantId, doc.BrandId, doc.Id);

        buffer.Position = 0;
        await storage.PutAsync(doc.StorageKey, buffer, contentType, cancellationToken);
        await documents.AddAsync(doc, cancellationToken);
        queue.Enqueue(new KnowledgeJob(KnowledgeJobKind.IngestDocument, doc.TenantId, doc.BrandId, doc.Id));
        return doc.ToDto();
    }

    private static ValidationException Invalid(string message) => new([new ValidationFailure("file", message)]);
}

// ================================================================ KB-2 manage

public sealed record ListKnowledgeDocumentsQuery(string BrandId) : IRequest<IReadOnlyList<KnowledgeDocumentDto>>;

public sealed class ListKnowledgeDocumentsQueryHandler(IBrandRepository brands, IKnowledgeDocumentRepository documents)
    : IRequestHandler<ListKnowledgeDocumentsQuery, IReadOnlyList<KnowledgeDocumentDto>>
{
    public async Task<IReadOnlyList<KnowledgeDocumentDto>> Handle(ListKnowledgeDocumentsQuery request, CancellationToken cancellationToken)
    {
        await brands.RequireAsync(request.BrandId, cancellationToken);
        return (await documents.ListAsync(request.BrandId, cancellationToken))
            .OrderByDescending(d => d.CreatedAt).Select(d => d.ToDto()).ToList();
    }
}

public sealed record GetKnowledgeDocumentQuery(string BrandId, string DocumentId) : IRequest<KnowledgeDocumentDto>;

public sealed class GetKnowledgeDocumentQueryHandler(IKnowledgeDocumentRepository documents)
    : IRequestHandler<GetKnowledgeDocumentQuery, KnowledgeDocumentDto>
{
    public async Task<KnowledgeDocumentDto> Handle(GetKnowledgeDocumentQuery request, CancellationToken cancellationToken) =>
        (await documents.GetByIdAsync(request.BrandId, request.DocumentId, cancellationToken)
            ?? throw new NotFoundException("Document not found.")).ToDto();
}

public sealed record GetKnowledgeUsageQuery(string BrandId) : IRequest<KnowledgeUsage>;

public sealed class GetKnowledgeUsageQueryHandler(
    IBrandRepository brands,
    IKnowledgeDocumentRepository documents,
    ISubscriptionRepository subscriptions,
    IPlanLimits limits) : IRequestHandler<GetKnowledgeUsageQuery, KnowledgeUsage>
{
    public async Task<KnowledgeUsage> Handle(GetKnowledgeUsageQuery request, CancellationToken cancellationToken)
    {
        await brands.RequireAsync(request.BrandId, cancellationToken);
        var plan = (await subscriptions.GetForCurrentTenantAsync(cancellationToken))?.PlanId ?? Subscription.TrialPlanId;
        return new KnowledgeUsage(
            await documents.CountForBrandAsync(request.BrandId, cancellationToken),
            limits.MaxKnowledgeDocumentsPerBrand(plan),
            await documents.TotalBytesAsync(cancellationToken),
            limits.MaxKnowledgeStorageBytes(plan),
            limits.MaxKnowledgeFileBytes(plan));
    }
}

public sealed record RetryKnowledgeDocumentCommand(string BrandId, string DocumentId) : IRequest<KnowledgeDocumentDto>;

public sealed class RetryKnowledgeDocumentCommandHandler(
    IKnowledgeDocumentRepository documents,
    IKnowledgeJobScheduler queue,
    TimeProvider clock) : IRequestHandler<RetryKnowledgeDocumentCommand, KnowledgeDocumentDto>
{
    public async Task<KnowledgeDocumentDto> Handle(RetryKnowledgeDocumentCommand request, CancellationToken cancellationToken)
    {
        var doc = await documents.GetByIdAsync(request.BrandId, request.DocumentId, cancellationToken)
            ?? throw new NotFoundException("Document not found.");
        if (doc.Status != IngestionStatus.Failed)
        {
            throw new ValidationException([new ValidationFailure("status", "Only failed documents can be retried.")]);
        }

        doc.Status = IngestionStatus.Uploaded;
        doc.FailureReason = null;
        doc.Attempts = 0;
        doc.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await documents.UpdateAsync(doc, cancellationToken);
        queue.Enqueue(new KnowledgeJob(KnowledgeJobKind.IngestDocument, doc.TenantId, doc.BrandId, doc.Id));
        return doc.ToDto();
    }
}

public sealed record DeleteKnowledgeDocumentCommand(string BrandId, string DocumentId) : IRequest;

public sealed class DeleteKnowledgeDocumentCommandHandler(
    IKnowledgeDocumentRepository documents,
    IVectorIndex index,
    IFileStorage storage) : IRequestHandler<DeleteKnowledgeDocumentCommand>
{
    public async Task Handle(DeleteKnowledgeDocumentCommand request, CancellationToken cancellationToken)
    {
        var doc = await documents.GetByIdAsync(request.BrandId, request.DocumentId, cancellationToken)
            ?? throw new NotFoundException("Document not found.");

        // Search first, so a partially failed delete never leaves findable chunks behind.
        await index.DeleteSourceAsync(doc.TenantId, doc.BrandId, SourceTypes.Document, doc.Id, cancellationToken);
        await storage.DeleteAsync(doc.StorageKey, cancellationToken);
        await documents.DeleteAsync(doc.BrandId, doc.Id, cancellationToken);
    }
}

// ================================================================ KB-3 FAQs

public sealed record FaqInput(string Question, string Answer);

public sealed class FaqInputValidator : AbstractValidator<FaqInput>
{
    public FaqInputValidator()
    {
        RuleFor(x => x.Question).NotEmpty().MaximumLength(KnowledgeRules.MaxQuestionLength);
        RuleFor(x => x.Answer).NotEmpty().MaximumLength(KnowledgeRules.MaxAnswerLength);
    }
}

public sealed record ListFaqsQuery(string BrandId) : IRequest<IReadOnlyList<FaqDto>>;

public sealed class ListFaqsQueryHandler(IBrandRepository brands, IFaqRepository faqs) : IRequestHandler<ListFaqsQuery, IReadOnlyList<FaqDto>>
{
    public async Task<IReadOnlyList<FaqDto>> Handle(ListFaqsQuery request, CancellationToken cancellationToken)
    {
        await brands.RequireAsync(request.BrandId, cancellationToken);
        return (await faqs.ListAsync(request.BrandId, cancellationToken)).OrderBy(f => f.Question, StringComparer.OrdinalIgnoreCase)
            .Select(f => f.ToDto()).ToList();
    }
}

public sealed record CreateFaqCommand(string BrandId, FaqInput Faq) : IRequest<FaqDto>;

public sealed class CreateFaqCommandValidator : AbstractValidator<CreateFaqCommand>
{
    public CreateFaqCommandValidator() => RuleFor(x => x.Faq).NotNull().SetValidator(new FaqInputValidator());
}

public sealed class CreateFaqCommandHandler(
    IBrandRepository brands,
    IFaqRepository faqs,
    IKnowledgeJobScheduler queue,
    ITenantContext context,
    TimeProvider clock) : IRequestHandler<CreateFaqCommand, FaqDto>
{
    public async Task<FaqDto> Handle(CreateFaqCommand request, CancellationToken cancellationToken)
    {
        var brand = await brands.RequireAsync(request.BrandId, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var faq = new Faq
        {
            TenantId = context.RequireTenantId(),
            BrandId = brand.Id,
            Question = request.Faq.Question.Trim(),
            Answer = request.Faq.Answer.Trim(),
            CreatedBy = context.RequireUserId(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        await faqs.AddAsync(faq, cancellationToken);
        queue.Enqueue(new KnowledgeJob(KnowledgeJobKind.IndexFaq, faq.TenantId, faq.BrandId, faq.Id));
        return faq.ToDto();
    }
}

public sealed record UpdateFaqCommand(string BrandId, string FaqId, FaqInput Faq) : IRequest<FaqDto>;

public sealed class UpdateFaqCommandValidator : AbstractValidator<UpdateFaqCommand>
{
    public UpdateFaqCommandValidator() => RuleFor(x => x.Faq).NotNull().SetValidator(new FaqInputValidator());
}

public sealed class UpdateFaqCommandHandler(IFaqRepository faqs, IKnowledgeJobScheduler queue, TimeProvider clock)
    : IRequestHandler<UpdateFaqCommand, FaqDto>
{
    public async Task<FaqDto> Handle(UpdateFaqCommand request, CancellationToken cancellationToken)
    {
        var faq = await faqs.GetByIdAsync(request.BrandId, request.FaqId, cancellationToken) ?? throw new NotFoundException("FAQ not found.");
        faq.Question = request.Faq.Question.Trim();
        faq.Answer = request.Faq.Answer.Trim();
        faq.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await faqs.UpdateAsync(faq, cancellationToken);
        queue.Enqueue(new KnowledgeJob(KnowledgeJobKind.IndexFaq, faq.TenantId, faq.BrandId, faq.Id));
        return faq.ToDto();
    }
}

public sealed record DeleteFaqCommand(string BrandId, string FaqId) : IRequest;

public sealed class DeleteFaqCommandHandler(IFaqRepository faqs, IVectorIndex index) : IRequestHandler<DeleteFaqCommand>
{
    public async Task Handle(DeleteFaqCommand request, CancellationToken cancellationToken)
    {
        var faq = await faqs.GetByIdAsync(request.BrandId, request.FaqId, cancellationToken) ?? throw new NotFoundException("FAQ not found.");
        await index.DeleteSourceAsync(faq.TenantId, faq.BrandId, SourceTypes.Faq, faq.Id, cancellationToken);
        await faqs.DeleteAsync(faq.BrandId, faq.Id, cancellationToken);
    }
}

// ================================================================ KB-4 search (also the agents' search_knowledge_base tool)

public sealed record SearchKnowledgeQuery(string BrandId, string Query, int? TopK, IReadOnlyList<string>? SourceTypes)
    : IRequest<KnowledgeSearchResult>;

public sealed class SearchKnowledgeQueryValidator : AbstractValidator<SearchKnowledgeQuery>
{
    public SearchKnowledgeQueryValidator()
    {
        RuleFor(x => x.Query).NotEmpty().MaximumLength(KnowledgeRules.MaxQueryLength);
        RuleFor(x => x.TopK).InclusiveBetween(1, KnowledgeRules.MaxTopK).When(x => x.TopK.HasValue);
        RuleForEach(x => x.SourceTypes).Must(t => Domain.Knowledge.SourceTypes.All.Contains(t))
            .WithMessage($"Source type must be one of: {string.Join(", ", Domain.Knowledge.SourceTypes.All)}.");
    }
}

public sealed class SearchKnowledgeQueryHandler(
    IBrandRepository brands,
    IEmbeddingService embeddings,
    IVectorIndex index,
    ITenantContext context,
    Microsoft.Extensions.Options.IOptions<KnowledgeSearchOptions> options) : IRequestHandler<SearchKnowledgeQuery, KnowledgeSearchResult>
{
    public async Task<KnowledgeSearchResult> Handle(SearchKnowledgeQuery request, CancellationToken cancellationToken)
    {
        var brand = await brands.RequireAsync(request.BrandId, cancellationToken);
        var topK = request.TopK ?? KnowledgeRules.DefaultTopK;
        var vector = (await embeddings.EmbedAsync([request.Query.Trim()], cancellationToken))[0];

        // Ask for extra results, then drop overlapping neighbours from the same source.
        var hits = await index.SearchAsync(
            context.RequireTenantId(), brand.Id, vector, topK * 2,
            request.SourceTypes is { Count: > 0 } ? request.SourceTypes : null,
            options.Value.MinScore, cancellationToken);

        var kept = new List<KnowledgeHit>();
        foreach (var hit in hits.OrderByDescending(h => h.Score))
        {
            var neighbour = kept.Any(k => k.SourceType == hit.SourceType && k.SourceId == hit.SourceId && Math.Abs(k.ChunkIndex - hit.ChunkIndex) <= 1);
            if (!neighbour)
            {
                kept.Add(hit);
            }

            if (kept.Count == topK)
            {
                break;
            }
        }

        return new KnowledgeSearchResult(
            kept.Select(h => new SearchHitDto(h.SourceType, h.SourceId, h.Title, h.HeadingPath, h.Text, Math.Round(h.Score, 3))).ToList(),
            LowConfidence: kept.Count == 0);
    }
}
