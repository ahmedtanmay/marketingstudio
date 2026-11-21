using MarketingStudio.Domain.Common;

namespace MarketingStudio.Domain.Knowledge;

public enum IngestionStatus
{
    Uploaded,
    Processing,
    Ready,
    Failed,
    Rejected,
}

public static class KnowledgeRules
{
    public const int MaxFileNameLength = 200;
    public const int MaxQuestionLength = 500;
    public const int MaxAnswerLength = 4000;
    public const int MaxQueryLength = 500;
    public const int DefaultTopK = 8;
    public const int MaxTopK = 20;

    /// <summary>Accepted by extension and content check (Knowledge Base Design.md → Validate and scan).</summary>
    public static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".html"] = "text/html",
        [".htm"] = "text/html",
    };
}

public static class SourceTypes
{
    public const string Document = "document";
    public const string Faq = "faq";
    public const string Product = "product";

    public static readonly IReadOnlyList<string> All = [Document, Faq, Product];
}

/// <summary>KB-1/KB-2: an uploaded knowledge document. The file lives in object storage; chunks live in the vector index.</summary>
public sealed class KnowledgeDocument : ITenantOwned
{
    public string Id { get; set; } = IdGenerator.NewId();
    public string TenantId { get; set; } = string.Empty;
    public string BrandId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Checksum { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public IngestionStatus Status { get; set; } = IngestionStatus.Uploaded;
    public string? FailureReason { get; set; }
    public int ChunkCount { get; set; }
    public int Attempts { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static string KeyFor(string tenantId, string brandId, string documentId) => $"{tenantId}/{brandId}/kb/{documentId}";
}

/// <summary>KB-3: a question and answer typed in the portal; one searchable chunk each.</summary>
public sealed class Faq : ITenantOwned
{
    public string Id { get; set; } = IdGenerator.NewId();
    public string TenantId { get; set; } = string.Empty;
    public string BrandId { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A piece of text ready to embed, with where it came from.</summary>
public sealed record KnowledgeChunk(
    string TenantId,
    string BrandId,
    string SourceType,
    string SourceId,
    string Title,
    string HeadingPath,
    int ChunkIndex,
    string Text);
