using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MarketingStudio.Application.Abstractions;
using MarketingStudio.Domain.Knowledge;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketingStudio.Infrastructure.Knowledge;

public sealed class QdrantOptions
{
    public const string Section = "Qdrant";
    public string Url { get; set; } = "http://localhost:6333";
    public string? ApiKey { get; set; }
}

/// <summary>
/// Qdrant over its REST API. One collection for all tenants, named after the embedding model so a
/// model change never mixes vectors; <c>tenantId</c> is the tenant key (is_tenant) and every query
/// filters on tenant and brand (Knowledge Base Design.md → Qdrant Design).
/// </summary>
internal sealed class QdrantVectorIndex(HttpClient http, IEmbeddingService embeddings) : IVectorIndex
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Collection { get; } = "knowledge_" + new string(
        embeddings.ModelId.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_').ToArray()) + "_" + embeddings.Dimensions;

    public async Task EnsureCollectionAsync(CancellationToken ct)
    {
        var existing = await http.GetAsync($"collections/{Collection}", ct);
        if (existing.StatusCode == HttpStatusCode.OK)
        {
            return;
        }

        await SendAsync(HttpMethod.Put, $"collections/{Collection}",
            new { vectors = new { size = embeddings.Dimensions, distance = "Cosine" } }, ct);

        await SendAsync(HttpMethod.Put, $"collections/{Collection}/index",
            new { field_name = "tenantId", field_schema = new { type = "keyword", is_tenant = true } }, ct);
        foreach (var field in new[] { "brandId", "sourceType", "sourceId" })
        {
            await SendAsync(HttpMethod.Put, $"collections/{Collection}/index", new { field_name = field, field_schema = "keyword" }, ct);
        }
    }

    public async Task UpsertAsync(IReadOnlyList<KnowledgeChunk> chunks, IReadOnlyList<float[]> vectors, CancellationToken ct)
    {
        if (chunks.Count != vectors.Count)
        {
            throw new ArgumentException("Each chunk needs exactly one vector.");
        }

        var points = chunks.Select((c, i) => new
        {
            id = PointId(c),
            vector = vectors[i],
            payload = new Dictionary<string, object>
            {
                ["tenantId"] = c.TenantId,
                ["brandId"] = c.BrandId,
                ["sourceType"] = c.SourceType,
                ["sourceId"] = c.SourceId,
                ["title"] = c.Title,
                ["headingPath"] = c.HeadingPath,
                ["chunkIndex"] = c.ChunkIndex,
                ["text"] = c.Text,
            },
        });

        foreach (var batch in points.Chunk(64))
        {
            await SendAsync(HttpMethod.Put, $"collections/{Collection}/points?wait=true", new { points = batch }, ct);
        }
    }

    public async Task<IReadOnlyList<KnowledgeHit>> SearchAsync(
        string tenantId, string brandId, float[] vector, int limit, IReadOnlyList<string>? sourceTypes, double minScore, CancellationToken ct)
    {
        var must = new List<object> { Match("tenantId", RequireValue(tenantId)), Match("brandId", RequireValue(brandId)) };
        if (sourceTypes is { Count: > 0 })
        {
            must.Add(new { key = "sourceType", match = new { any = sourceTypes } });
        }

        var response = await SendAsync(HttpMethod.Post, $"collections/{Collection}/points/query", new
        {
            query = vector,
            filter = new { must },
            limit,
            with_payload = true,
            score_threshold = minScore,
        }, ct);

        var body = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
        var points = body?["result"]?["points"]?.AsArray() ?? [];
        var hits = new List<KnowledgeHit>();
        foreach (var point in points)
        {
            var payload = point!["payload"]!;
            // Defence in depth: never return another tenant's or brand's chunk even if a filter were wrong.
            if ((string?)payload["tenantId"] != tenantId || (string?)payload["brandId"] != brandId)
            {
                continue;
            }

            hits.Add(new KnowledgeHit(
                (string)payload["sourceType"]!,
                (string)payload["sourceId"]!,
                (string?)payload["title"] ?? string.Empty,
                (string?)payload["headingPath"] ?? string.Empty,
                (int?)payload["chunkIndex"] ?? 0,
                (string?)payload["text"] ?? string.Empty,
                (double)point["score"]!));
        }

        return hits;
    }

    public Task DeleteSourceAsync(string tenantId, string brandId, string sourceType, string sourceId, CancellationToken ct) =>
        DeleteAsync([Match("tenantId", RequireValue(tenantId)), Match("brandId", RequireValue(brandId)),
            Match("sourceType", sourceType), Match("sourceId", RequireValue(sourceId))], ct);

    public Task DeleteBrandAsync(string tenantId, string brandId, CancellationToken ct) =>
        DeleteAsync([Match("tenantId", RequireValue(tenantId)), Match("brandId", RequireValue(brandId))], ct);

    private async Task DeleteAsync(object[] must, CancellationToken ct) =>
        await SendAsync(HttpMethod.Post, $"collections/{Collection}/points/delete?wait=true", new { filter = new { must } }, ct);

    private static object Match(string key, string value) => new { key, match = new { value } };

    private static string RequireValue(string value) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Tenant, brand and source ids are required for every vector operation.") : value;

    /// <summary>Deterministic id per chunk, so re-ingesting overwrites instead of duplicating.</summary>
    internal static Guid PointId(KnowledgeChunk c)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{c.TenantId}|{c.BrandId}|{c.SourceType}|{c.SourceId}|{c.ChunkIndex}"));
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5-style (name-based)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes, bigEndian: true);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body, options: Json) };
        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Qdrant {method} {path} failed ({(int)response.StatusCode}): {detail}");
        }

        return response;
    }
}

/// <summary>Creates the collection and payload indexes at start-up; retries while Qdrant starts.</summary>
internal sealed class QdrantInitializer(QdrantVectorIndex index, ILogger<QdrantInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await index.EnsureCollectionAsync(cancellationToken);
                return;
            }
            catch (HttpRequestException ex) when (attempt < 30)
            {
                logger.LogWarning("Vector index not ready (attempt {Attempt}): {Message}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
