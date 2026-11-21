using MarketingStudio.Application.Abstractions;
using MarketingStudio.Domain.Brands;
using MarketingStudio.Domain.Knowledge;
using Microsoft.Extensions.Logging;

namespace MarketingStudio.Application.Knowledge;

public enum JobOutcome
{
    Done,
    RetryLater,
}

/// <summary>
/// Runs knowledge jobs in the background with the job's tenant already set (BackgroundTenantScope),
/// so every repository call stays tenant-scoped.
/// </summary>
public sealed class KnowledgeProcessor(
    IKnowledgeDocumentRepository documents,
    IFaqRepository faqs,
    IProductRepository products,
    IFileStorage storage,
    IVirusScanner scanner,
    ITextExtractor extractor,
    IEmbeddingService embeddings,
    IVectorIndex index,
    TimeProvider clock,
    ILogger<KnowledgeProcessor> logger)
{
    public const int MaxScanAttempts = 3;
    public const string NoText = "No extractable text found. Scanned PDFs (images of text) are not supported yet.";
    public const string Infected = "The file was rejected by the virus scanner and has been deleted.";
    public const string ScannerDown = "The virus scanner is unavailable. Retry in a few minutes.";
    public const string GenericFailure = "Processing failed. Retry, or upload the file again.";
    private const int EmbedBatchSize = 32;

    public async Task<JobOutcome> RunAsync(KnowledgeJob job, CancellationToken ct) => job.Kind switch
    {
        KnowledgeJobKind.IngestDocument => await IngestAsync(job, ct),
        KnowledgeJobKind.IndexFaq => await IndexFaqAsync(job, ct),
        KnowledgeJobKind.IndexProduct => await IndexProductAsync(job, ct),
        _ => JobOutcome.Done,
    };

    // ------------------------------------------------------------------ documents (KB-1)

    private async Task<JobOutcome> IngestAsync(KnowledgeJob job, CancellationToken ct)
    {
        var doc = await documents.GetByIdAsync(job.BrandId, job.SourceId, ct);
        if (doc is null || doc.Status is IngestionStatus.Ready or IngestionStatus.Rejected or IngestionStatus.Failed)
        {
            return JobOutcome.Done;
        }

        doc.Status = IngestionStatus.Processing;
        doc.Attempts++;
        await SaveAsync(doc, ct);

        try
        {
            using var buffer = new MemoryStream();
            await using (var file = await storage.OpenReadAsync(doc.StorageKey, ct))
            {
                await file.CopyToAsync(buffer, ct);
            }

            buffer.Position = 0;
            var scan = await scanner.ScanAsync(buffer, ct);
            if (scan.Outcome == ScanOutcome.Infected)
            {
                logger.LogWarning("Infected knowledge upload {DocumentId} rejected ({Signature})", doc.Id, scan.Signature);
                await storage.DeleteAsync(doc.StorageKey, ct);
                return await FinishAsync(doc, IngestionStatus.Rejected, Infected, ct);
            }

            if (scan.Outcome == ScanOutcome.Unavailable)
            {
                if (doc.Attempts < MaxScanAttempts)
                {
                    doc.Status = IngestionStatus.Uploaded;
                    await SaveAsync(doc, ct);
                    return JobOutcome.RetryLater;
                }

                return await FinishAsync(doc, IngestionStatus.Failed, ScannerDown, ct);
            }

            buffer.Position = 0;
            var extension = Path.GetExtension(doc.FileName).ToLowerInvariant();
            string text;
            try
            {
                text = extractor.Extract(buffer, extension);
            }
            catch (TextExtractionException ex)
            {
                return await FinishAsync(doc, IngestionStatus.Failed, ex.Message, ct);
            }

            var pieces = Chunker.Chunk(text);
            if (pieces.Count == 0)
            {
                return await FinishAsync(doc, IngestionStatus.Failed, NoText, ct);
            }

            var chunks = pieces
                .Select((p, i) => new KnowledgeChunk(doc.TenantId, doc.BrandId, SourceTypes.Document, doc.Id, doc.FileName, p.HeadingPath, i, p.Text))
                .ToList();
            await ReplaceSourceAsync(chunks, ct);

            doc.ChunkCount = chunks.Count;
            return await FinishAsync(doc, IngestionStatus.Ready, null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Knowledge ingestion failed for document {DocumentId}", doc.Id);
            return await FinishAsync(doc, IngestionStatus.Failed, GenericFailure, ct);
        }
    }

    private async Task<JobOutcome> FinishAsync(KnowledgeDocument doc, IngestionStatus status, string? reason, CancellationToken ct)
    {
        doc.Status = status;
        doc.FailureReason = reason;
        await SaveAsync(doc, ct);
        return JobOutcome.Done;
    }

    private Task SaveAsync(KnowledgeDocument doc, CancellationToken ct)
    {
        doc.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        return documents.UpdateAsync(doc, ct);
    }

    // ------------------------------------------------------------------ FAQs (KB-3) and products

    private async Task<JobOutcome> IndexFaqAsync(KnowledgeJob job, CancellationToken ct)
    {
        var faq = await faqs.GetByIdAsync(job.BrandId, job.SourceId, ct);
        if (faq is null)
        {
            await index.DeleteSourceAsync(job.TenantId, job.BrandId, SourceTypes.Faq, job.SourceId, ct);
            return JobOutcome.Done;
        }

        var text = $"Q: {faq.Question}\nA: {faq.Answer}";
        await ReplaceSourceAsync([new KnowledgeChunk(faq.TenantId, faq.BrandId, SourceTypes.Faq, faq.Id, faq.Question, string.Empty, 0, text)], ct);
        return JobOutcome.Done;
    }

    private async Task<JobOutcome> IndexProductAsync(KnowledgeJob job, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(job.BrandId, job.SourceId, ct);
        if (product is null)
        {
            await index.DeleteSourceAsync(job.TenantId, job.BrandId, SourceTypes.Product, job.SourceId, ct);
            return JobOutcome.Done;
        }

        await ReplaceSourceAsync([ProductChunk(product)], ct);
        return JobOutcome.Done;
    }

    /// <summary>One chunk per product: the facts agents may state (Knowledge Base Design.md → Chunk).</summary>
    public static KnowledgeChunk ProductChunk(Product product)
    {
        var lines = new List<string> { $"Product: {product.Name}" };
        if (product.Category is not null)
        {
            lines.Add($"Category: {product.Category}");
        }

        if (product.Price is not null)
        {
            lines.Add($"Price: {product.Price.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} {product.Currency}");
        }

        if (product.Description is not null)
        {
            lines.Add(product.Description);
        }

        if (product.Features.Count > 0)
        {
            lines.Add("Key features: " + string.Join("; ", product.Features));
        }

        if (product.Url is not null)
        {
            lines.Add($"More: {product.Url}");
        }

        return new KnowledgeChunk(product.TenantId, product.BrandId, SourceTypes.Product, product.Id, product.Name, string.Empty, 0, string.Join('\n', lines));
    }

    /// <summary>Removes a source's old chunks, then writes the new ones (re-ingestion never duplicates).</summary>
    private async Task ReplaceSourceAsync(List<KnowledgeChunk> chunks, CancellationToken ct)
    {
        var first = chunks[0];
        var vectors = new List<float[]>(chunks.Count);
        foreach (var batch in chunks.Chunk(EmbedBatchSize))
        {
            vectors.AddRange(await embeddings.EmbedAsync(batch.Select(EmbeddingText).ToList(), ct));
        }

        await index.DeleteSourceAsync(first.TenantId, first.BrandId, first.SourceType, first.SourceId, ct);
        await index.UpsertAsync(chunks, vectors, ct);
    }

    /// <summary>Each chunk is embedded with its title and heading path so it makes sense on its own.</summary>
    public static string EmbeddingText(KnowledgeChunk chunk) =>
        string.IsNullOrEmpty(chunk.HeadingPath)
            ? $"{chunk.Title}\n{chunk.Text}"
            : $"{chunk.Title} > {chunk.HeadingPath}\n{chunk.Text}";
}
