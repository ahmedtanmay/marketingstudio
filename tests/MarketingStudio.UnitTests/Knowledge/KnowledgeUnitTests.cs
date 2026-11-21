using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using MarketingStudio.Application.Abstractions;
using MarketingStudio.Application.Knowledge;
using MarketingStudio.Domain.Knowledge;
using MarketingStudio.Infrastructure.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace MarketingStudio.UnitTests.Knowledge;

public class ChunkerTests
{
    [Fact]
    [Trait("Story", "KB-1")]
    public void Chunks_follow_headings_and_carry_the_heading_path()
    {
        var chunks = Chunker.Chunk("# Returns\n\nThirty days.\n\n## International\n\nBuyer pays shipping.\n\n# Delivery\n\nDaily.");

        chunks.Select(c => c.HeadingPath).Should().Equal("Returns", "Returns > International", "Delivery");
        chunks[1].Text.Should().Be("Buyer pays shipping.");
    }

    [Fact]
    [Trait("Story", "KB-1")]
    public void Long_sections_are_split_near_the_target_size_with_overlap()
    {
        var sentences = Enumerable.Range(1, 300).Select(i => $"Sentence number {i} talks about coffee roasting.");
        var text = "# Guide\n\n" + string.Join("\n\n", sentences.Chunk(5).Select(p => string.Join(' ', p)));

        var chunks = Chunker.Chunk(text);

        chunks.Should().HaveCountGreaterThan(3);
        chunks.Should().OnlyContain(c => c.Text.Length <= Chunker.MaxChars + Chunker.OverlapChars);
        for (var i = 1; i < chunks.Count; i++)
        {
            var previousTail = chunks[i - 1].Text[^60..];
            chunks[i].Text.Should().Contain(previousTail.Split(' ', 2)[1], "consecutive chunks share ~75 tokens of overlap");
        }
    }

    [Fact]
    public void A_single_huge_paragraph_is_still_split()
    {
        var chunks = Chunker.Chunk(new string('x', 10_000));

        chunks.Should().HaveCountGreaterThan(2).And.OnlyContain(c => c.Text.Length <= Chunker.MaxChars + Chunker.OverlapChars);
    }

    [Fact]
    public void Empty_text_gives_no_chunks() => Chunker.Chunk("  \n\n # Only a heading\n").Should().BeEmpty();
}

public class EmbeddingAndSnifferTests
{
    private static double Cosine(float[] a, float[] b) => a.Zip(b, (x, y) => (double)x * y).Sum();

    [Fact]
    public void Hash_embeddings_are_deterministic_normalised_and_closer_for_related_text()
    {
        var policy = HashEmbeddingService.Embed("Refunds for international orders are issued within 14 days");
        var query = HashEmbeddingService.Embed("international refunds");
        var other = HashEmbeddingService.Embed("We deliver every day in Mumbai");

        HashEmbeddingService.Embed("Refunds for international orders are issued within 14 days").Should().Equal(policy);
        Math.Sqrt(policy.Sum(v => v * v)).Should().BeApproximately(1, 1e-4);
        Cosine(policy, query).Should().BeGreaterThan(Cosine(other, query));
    }

    [Theory]
    [Trait("Story", "KB-1")]
    [InlineData(".pdf", "%PDF-1.7 rest", true)]
    [InlineData(".pdf", "plain text", false)]
    [InlineData(".txt", "hello world", true)]
    [InlineData(".md", "# Heading", true)]
    public void Document_type_must_match_the_file_bytes(string extension, string content, bool expected) =>
        DocumentSniffer.Matches(extension, Encoding.UTF8.GetBytes(content)).Should().Be(expected);

    [Fact]
    public void Binary_content_is_not_accepted_as_text()
    {
        DocumentSniffer.Matches(".txt", [0x4D, 0x5A, 0x00, 0x01]).Should().BeFalse();
        DocumentSniffer.Matches(".txt", [0xC3, 0x28, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46]).Should().BeFalse("invalid UTF-8");
        DocumentSniffer.Matches(".docx", [0x50, 0x4B, 0x03, 0x04, 0x14]).Should().BeTrue();
    }
}

public class ClamAvScannerTests
{
    private static ClamAvScanner Scanner(int port) => new(
        Options.Create(new VirusScanOptions { Host = "127.0.0.1", Port = port, TimeoutSeconds = 5 }),
        NullLogger<ClamAvScanner>.Instance);

    /// <summary>A minimal clamd stand-in: reads the INSTREAM request, then sends a fixed reply.</summary>
    private static async Task<int> FakeClamdAsync(string reply, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            var stream = client.GetStream();
            var command = new byte[10];
            await stream.ReadExactlyAsync(command, ct);
            var size = new byte[4];
            while (true)
            {
                await stream.ReadExactlyAsync(size, ct);
                var length = (size[0] << 24) | (size[1] << 16) | (size[2] << 8) | size[3];
                if (length == 0)
                {
                    break;
                }

                await stream.ReadExactlyAsync(new byte[length], ct);
            }

            await stream.WriteAsync(Encoding.ASCII.GetBytes(reply + "\0"), ct);
            client.Close();
            listener.Stop();
        }, ct);
        return port;
    }

    [Theory]
    [Trait("Story", "KB-1")]
    [InlineData("stream: OK", ScanOutcome.Clean, null)]
    [InlineData("stream: Eicar-Signature FOUND", ScanOutcome.Infected, "Eicar-Signature")]
    [InlineData("INSTREAM size limit exceeded. ERROR", ScanOutcome.Unavailable, null)]
    public async Task Clamd_replies_are_interpreted(string reply, ScanOutcome outcome, string? signature)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var port = await FakeClamdAsync(reply, cts.Token);

        var result = await Scanner(port).ScanAsync(new MemoryStream(new byte[150_000]), cts.Token);

        result.Should().Be(new ScanResult(outcome, signature));
    }

    [Fact]
    [Trait("Story", "KB-1")]
    public async Task An_unreachable_scanner_is_unavailable_never_clean()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var result = await Scanner(port).ScanAsync(new MemoryStream([1, 2, 3]), default);

        result.Outcome.Should().Be(ScanOutcome.Unavailable);
    }
}

public class KnowledgeProcessorTests
{
    [Fact]
    [Trait("Story", "KB-1")]
    public async Task Scanner_outage_retries_then_fails_with_a_clear_reason()
    {
        var doc = new KnowledgeDocument { Id = "d1", TenantId = "t1", BrandId = "b1", FileName = "a.txt", StorageKey = "t1/b1/kb/d1" };
        var documents = Substitute.For<IKnowledgeDocumentRepository>();
        documents.GetByIdAsync("b1", "d1", Arg.Any<CancellationToken>()).Returns(doc);
        var storage = Substitute.For<IFileStorage>();
        storage.OpenReadAsync(doc.StorageKey, Arg.Any<CancellationToken>()).Returns(_ => new MemoryStream(Encoding.UTF8.GetBytes("text")));
        var scanner = Substitute.For<IVirusScanner>();
        scanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(new ScanResult(ScanOutcome.Unavailable));
        var index = Substitute.For<IVectorIndex>();

        var processor = new KnowledgeProcessor(documents, Substitute.For<IFaqRepository>(), Substitute.For<IProductRepository>(),
            storage, scanner, Substitute.For<ITextExtractor>(), new HashEmbeddingService(), index, TimeProvider.System,
            NullLogger<KnowledgeProcessor>.Instance);
        var job = new KnowledgeJob(KnowledgeJobKind.IngestDocument, "t1", "b1", "d1");

        var outcomes = new List<JobOutcome>();
        for (var i = 0; i < KnowledgeProcessor.MaxScanAttempts; i++)
        {
            outcomes.Add(await processor.RunAsync(job, default));
        }

        outcomes.Should().Equal(JobOutcome.RetryLater, JobOutcome.RetryLater, JobOutcome.Done);
        doc.Status.Should().Be(IngestionStatus.Failed);
        doc.FailureReason.Should().Be(KnowledgeProcessor.ScannerDown);
        await index.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default!, default);
    }

    [Fact]
    public void Product_chunks_state_only_the_facts_on_record()
    {
        var chunk = KnowledgeProcessor.ProductChunk(new MarketingStudio.Domain.Brands.Product
        {
            TenantId = "t1", BrandId = "b1", Name = "Cold Brew", Category = "Coffee", Price = 349.5m, Currency = "INR",
            Features = ["Smooth", "Low acid"],
        });

        chunk.Text.Should().Be("Product: Cold Brew\nCategory: Coffee\nPrice: 349.5 INR\nKey features: Smooth; Low acid");
        chunk.SourceType.Should().Be(SourceTypes.Product);
    }
}
