using MarketingStudio.Domain.Knowledge;

namespace MarketingStudio.Application.Abstractions;

/// <summary>Tenant-scoped (caller's tenant), except the members marked "system".</summary>
public interface IKnowledgeDocumentRepository
{
    Task AddAsync(KnowledgeDocument document, CancellationToken cancellationToken);
    Task<KnowledgeDocument?> GetByIdAsync(string brandId, string documentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<KnowledgeDocument>> ListAsync(string brandId, CancellationToken cancellationToken);
    Task<long> CountForBrandAsync(string brandId, CancellationToken cancellationToken);
    Task<long> TotalBytesAsync(CancellationToken cancellationToken);
    Task<bool> ChecksumExistsAsync(string brandId, string checksum, CancellationToken cancellationToken);
    Task<long> CountReadyAsync(CancellationToken cancellationToken);
    Task UpdateAsync(KnowledgeDocument document, CancellationToken cancellationToken);
    Task DeleteAsync(string brandId, string documentId, CancellationToken cancellationToken);
    Task DeleteForBrandAsync(string brandId, CancellationToken cancellationToken);

    /// <summary>System: documents left unfinished by a restart, across tenants (recovery only).</summary>
    Task<IReadOnlyList<KnowledgeDocument>> ListUnfinishedAsync(CancellationToken cancellationToken);
}

public interface IFaqRepository
{
    Task AddAsync(Faq faq, CancellationToken cancellationToken);
    Task<Faq?> GetByIdAsync(string brandId, string faqId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Faq>> ListAsync(string brandId, CancellationToken cancellationToken);
    Task<long> CountAsync(CancellationToken cancellationToken);
    Task UpdateAsync(Faq faq, CancellationToken cancellationToken);
    Task DeleteAsync(string brandId, string faqId, CancellationToken cancellationToken);
    Task DeleteForBrandAsync(string brandId, CancellationToken cancellationToken);
}

/// <summary>Turns text into vectors. Providers: local hash (tests/CI), Ollama (local dev), OpenAI (production; later).</summary>
public interface IEmbeddingService
{
    /// <summary>Identifies model and dimensions; the vector collection is named after it so models never mix.</summary>
    string ModelId { get; }
    int Dimensions { get; }
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}

public sealed record KnowledgeHit(
    string SourceType,
    string SourceId,
    string Title,
    string HeadingPath,
    int ChunkIndex,
    string Text,
    double Score);

/// <summary>
/// Vector store. Every method takes the tenant and brand explicitly and always filters by both;
/// there is no unfiltered search (Knowledge Base Design.md → Isolation enforcement).
/// </summary>
public interface IVectorIndex
{
    Task UpsertAsync(IReadOnlyList<KnowledgeChunk> chunks, IReadOnlyList<float[]> vectors, CancellationToken cancellationToken);
    Task<IReadOnlyList<KnowledgeHit>> SearchAsync(
        string tenantId, string brandId, float[] vector, int limit, IReadOnlyList<string>? sourceTypes, double minScore,
        CancellationToken cancellationToken);
    Task DeleteSourceAsync(string tenantId, string brandId, string sourceType, string sourceId, CancellationToken cancellationToken);
    Task DeleteBrandAsync(string tenantId, string brandId, CancellationToken cancellationToken);
}

public enum ScanOutcome
{
    Clean,
    Infected,
    Unavailable,
}

public sealed record ScanResult(ScanOutcome Outcome, string? Signature = null);

public interface IVirusScanner
{
    Task<ScanResult> ScanAsync(Stream content, CancellationToken cancellationToken);
}

public sealed class TextExtractionException(string message) : Exception(message);

/// <summary>Extracts text as lightweight Markdown (headings as "#") so chunking can follow structure.</summary>
public interface ITextExtractor
{
    /// <param name="extension">Lower-case file extension including the dot, e.g. ".pdf".</param>
    string Extract(Stream content, string extension);
}

public enum KnowledgeJobKind
{
    IngestDocument,
    IndexFaq,
    IndexProduct,
}

/// <summary>Background work for the knowledge base. Jobs carry their tenant and brand.</summary>
public sealed record KnowledgeJob(KnowledgeJobKind Kind, string TenantId, string BrandId, string SourceId);

public interface IKnowledgeJobScheduler
{
    void Enqueue(KnowledgeJob job);
}

/// <summary>
/// Tenant context for background work (no HTTP request). The worker sets it from the job before
/// running application code, so tenant-scoped repositories work the same way as in requests.
/// </summary>
public sealed class BackgroundTenantScope
{
    public string? TenantId { get; private set; }
    public string? UserId { get; private set; }

    public void Set(string tenantId, string? userId = null)
    {
        TenantId = tenantId;
        UserId = userId;
    }
}
