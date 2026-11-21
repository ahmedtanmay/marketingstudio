using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using MarketingStudio.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace MarketingStudio.Infrastructure.Knowledge;

public sealed class EmbeddingOptions
{
    public const string Section = "Embeddings";

    /// <summary>"Hash" (tests/CI, no AI), "Ollama" (local development), later "OpenAI" (production).</summary>
    public string Provider { get; set; } = "Hash";
    public string Model { get; set; } = "nomic-embed-text";
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public int Dimensions { get; set; } = 768;
}

/// <summary>
/// Deterministic keyword embedding (feature hashing of words and word pairs). Not semantic — "cheap"
/// and "inexpensive" don't match — but needs no AI service, so tests and CI are fast and repeatable.
/// Never use it in production.
/// </summary>
public sealed class HashEmbeddingService : IEmbeddingService
{
    public const int Dims = 384;

    public string ModelId => "hash-v1";
    public int Dimensions => Dims;

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Embed).ToList());

    public static float[] Embed(string text)
    {
        var vector = new float[Dims];
        var words = Tokens(text).ToList();
        for (var i = 0; i < words.Count; i++)
        {
            Add(vector, words[i], 1f);
            if (i + 1 < words.Count)
            {
                Add(vector, words[i] + " " + words[i + 1], 0.5f);
            }
        }

        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
        {
            for (var i = 0; i < Dims; i++)
            {
                vector[i] /= norm;
            }
        }

        return vector;
    }

    private static IEnumerable<string> Tokens(string text)
    {
        var word = new StringBuilder();
        foreach (var c in text.ToLower(CultureInfo.InvariantCulture))
        {
            if (char.IsLetterOrDigit(c))
            {
                word.Append(c);
            }
            else if (word.Length > 0)
            {
                if (word.Length > 1)
                {
                    yield return word.ToString();
                }

                word.Clear();
            }
        }

        if (word.Length > 1)
        {
            yield return word.ToString();
        }
    }

    private static void Add(float[] vector, string token, float weight)
    {
        var hash = Fnv1a(token);
        var index = (int)(hash % Dims);
        vector[index] += (hash & 0x8000_0000) == 0 ? weight : -weight;
    }

    private static uint Fnv1a(string value)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash = (hash ^ b) * 16777619u;
        }

        return hash;
    }
}

/// <summary>Local embeddings through Ollama's /api/embed (free; e.g. nomic-embed-text, 768 dimensions).</summary>
public sealed class OllamaEmbeddingService(HttpClient http, IOptions<EmbeddingOptions> options) : IEmbeddingService
{
    private EmbeddingOptions Settings => options.Value;

    public string ModelId => "ollama-" + Settings.Model;
    public int Dimensions => Settings.Dimensions;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        var response = await http.PostAsJsonAsync(
            new Uri(new Uri(Settings.BaseUrl), "/api/embed"),
            new { model = Settings.Model, input = texts },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Empty embedding response.");

        if (body.Embeddings.Count != texts.Count || body.Embeddings.Any(e => e.Length != Dimensions))
        {
            throw new InvalidOperationException(
                $"Embedding model returned {body.Embeddings.FirstOrDefault()?.Length ?? 0} dimensions; configuration says {Dimensions}.");
        }

        return body.Embeddings;
    }

    private sealed record OllamaEmbedResponse([property: JsonPropertyName("embeddings")] List<float[]> Embeddings);
}
