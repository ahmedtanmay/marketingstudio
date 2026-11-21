using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MarketingStudio.Application.Abstractions;
using MarketingStudio.Application.Knowledge;
using MarketingStudio.Application.Onboarding;
using MarketingStudio.Domain.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace MarketingStudio.IntegrationTests;

public static class KnowledgeApi
{
    public const string Policy = """
        # Returns Policy

        We accept returns within 30 days of delivery for unopened coffee bags.

        ## International orders

        International customers pay return shipping. Refunds for international orders are issued within 14 days
        of us receiving the parcel.

        # Delivery

        We deliver every day between 8am and 8pm in Mumbai and Pune.
        """;

    public static async Task<HttpResponseMessage> UploadRawAsync(HttpClient http, string? token, string brandId, byte[] bytes, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/brands/{brandId}/knowledge/documents") { Content = form };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await http.SendAsync(request);
    }

    public static async Task<KnowledgeDocumentDto> UploadAsync(HttpClient http, Session s, string brandId, byte[] bytes, string fileName)
    {
        var response = await UploadRawAsync(http, s.AccessToken, brandId, bytes, fileName);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<KnowledgeDocumentDto>())!;
    }

    /// <summary>Polls until processing finishes (background worker).</summary>
    public static async Task<KnowledgeDocumentDto> WaitAsync(AuthApi api, Session s, string brandId, string documentId)
    {
        for (var i = 0; i < 100; i++)
        {
            var doc = await api.GetAsync<KnowledgeDocumentDto>($"/api/v1/brands/{brandId}/knowledge/documents/{documentId}", s.AccessToken);
            if (doc.Status is not (nameof(IngestionStatus.Uploaded) or nameof(IngestionStatus.Processing)))
            {
                return doc;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("Document was not processed in time.");
    }

    public static async Task<KnowledgeSearchResult> SearchAsync(AuthApi api, Session s, string brandId, string query, params string[] sourceTypes)
    {
        var response = await api.SendAsync(HttpMethod.Post, $"/api/v1/brands/{brandId}/knowledge/search", s.AccessToken,
            new { query, topK = 5, sourceTypes = sourceTypes.Length == 0 ? null : sourceTypes });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<KnowledgeSearchResult>())!;
    }

    /// <summary>FAQ and product indexing is asynchronous; wait until the search sees (or stops seeing) the source.</summary>
    public static async Task<KnowledgeSearchResult> SearchUntilAsync(
        AuthApi api, Session s, string brandId, string query, Func<KnowledgeSearchResult, bool> done)
    {
        KnowledgeSearchResult result = new([], true);
        for (var i = 0; i < 50; i++)
        {
            result = await SearchAsync(api, s, brandId, query);
            if (done(result))
            {
                return result;
            }

            await Task.Delay(200);
        }

        return result;
    }

    public static byte[] Docx(params (string? Style, string Text)[] paragraphs)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(paragraphs.Select(p =>
            {
                var paragraph = new Paragraph(new Run(new Text(p.Text)));
                if (p.Style is not null)
                {
                    paragraph.PrependChild(new ParagraphProperties(new ParagraphStyleId { Val = p.Style }));
                }

                return (OpenXmlElement)paragraph;
            })));
        }

        return stream.ToArray();
    }

    public static byte[] Pdf(params string[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var y = 780;
        foreach (var line in lines)
        {
            page.AddText(line, 11, new PdfPoint(40, y), font);
            y -= 18;
        }

        return builder.Build();
    }
}

[Collection(ApiTestGroup.Name)]
public class KnowledgeTests(ApiFactory factory)
{
    private readonly HttpClient _http = factory.CreateApiClient();
    private AuthApi Api => new(_http);

    private async Task<(Session Admin, string BrandId)> BrandAsync()
    {
        var admin = await Api.RegisterAndLoginAsync();
        var brand = await BrandApi.CreateAsync(Api, admin);
        return (admin, brand.Id);
    }

    // ---------- KB-1 upload and ingest ----------

    [Fact]
    [Trait("Story", "KB-1")]
    public async Task Markdown_document_is_chunked_by_headings_and_found_by_search()
    {
        var (admin, brandId) = await BrandAsync();

        var doc = await KnowledgeApi.UploadAsync(_http, admin, brandId, Encoding.UTF8.GetBytes(KnowledgeApi.Policy), "returns.md");
        var ready = await KnowledgeApi.WaitAsync(Api, admin, brandId, doc.Id);
        var result = await KnowledgeApi.SearchAsync(Api, admin, brandId, "refunds for international orders");

        doc.Status.Should().Be("Uploaded");
        ready.Status.Should().Be("Ready");
        ready.ChunkCount.Should().BeGreaterThanOrEqualTo(2);
        result.LowConfidence.Should().BeFalse();
        var top = result.Hits[0];
        top.SourceType.Should().Be(SourceTypes.Document);
        top.SourceId.Should().Be(doc.Id);
        top.Title.Should().Be("returns.md");
        top.HeadingPath.Should().Be("Returns Policy > International orders");
        top.Text.Should().Contain("14 days");
    }

    [Theory]
    [Trait("Story", "KB-1")]
    [InlineData("docx")]
    [InlineData("pdf")]
    [InlineData("html")]
    [InlineData("txt")]
    public async Task Every_supported_format_is_extracted(string format)
    {
        var (admin, brandId) = await BrandAsync();
        var marker = $"Our signature blend uses beans from Coorg estate number {Random.Shared.Next(1000, 9999)}";
        var (bytes, name) = format switch
        {
            "docx" => (KnowledgeApi.Docx(("Heading1", "Origins"), (null, marker)), "origins.docx"),
            "pdf" => (KnowledgeApi.Pdf("Origins", marker), "origins.pdf"),
            "html" => (Encoding.UTF8.GetBytes($"<html><head><script>var secret='do-not-index';</script></head><body><h1>Origins</h1><p>{marker}</p></body></html>"), "origins.html"),
            _ => (Encoding.UTF8.GetBytes($"Origins\n\n{marker}"), "origins.txt"),
        };

        var doc = await KnowledgeApi.UploadAsync(_http, admin, brandId, bytes, name);
        var ready = await KnowledgeApi.WaitAsync(Api, admin, brandId, doc.Id);
        var result = await KnowledgeApi.SearchAsync(Api, admin, brandId, "signature blend beans Coorg estate");

        ready.Status.Should().Be("Ready", ready.FailureReason);
        result.Hits.Should().Contain(h => h.SourceId == doc.Id && h.Text.Contains("Coorg"));
        result.Hits.Should().NotContain(h => h.Text.Contains("do-not-index"), "scripts are stripped from HTML");
    }

    [Theory]
    [Trait("Story", "KB-1")]
    [InlineData("tool.exe", "MZ binary")]
    [InlineData("fake.pdf", "just some text, not a PDF")]
    [InlineData("fake.docx", "not a zip either")]
    public async Task Unsupported_or_mismatched_files_are_rejected_at_upload(string fileName, string content)
    {
        var (admin, brandId) = await BrandAsync();

        var response = await KnowledgeApi.UploadRawAsync(_http, admin.AccessToken, brandId, Encoding.UTF8.GetBytes(content), fileName);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Story", "KB-1")]
    public async Task The_same_file_cannot_be_uploaded_twice_to_a_brand()
    {
        var (admin, brandId) = await BrandAsync();
        var bytes = Encoding.UTF8.GetBytes(KnowledgeApi.Policy);
        await KnowledgeApi.UploadAsync(_http, admin, brandId, bytes, "a.md");

        var again = await KnowledgeApi.UploadRawAsync(_http, admin.AccessToken, brandId, bytes, "b.md");

        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await again.Content.ReadAsStringAsync()).Should().Contain("already in the knowledge base");
    }

    [Fact]
    [Trait("Story", "KB-1")]
    public async Task Infected_files_are_rejected_and_never_indexed()
    {
        var (admin, brandId) = await BrandAsync();

        var doc = await KnowledgeApi.UploadAsync(_http, admin, brandId, Encoding.ASCII.GetBytes(EicarScanner.Eicar + "\nrefund policy text here"), "bad.txt");
        var done = await KnowledgeApi.WaitAsync(Api, admin, brandId, doc.Id);
        var result = await KnowledgeApi.SearchAsync(Api, admin, brandId, "refund policy text");

        done.Status.Should().Be("Rejected");
        done.FailureReason.Should().Be(KnowledgeProcessor.Infected);
        result.Hits.Should().NotContain(h => h.SourceId == doc.Id);
    }

    [Fact]
    [Trait("Story", "KB-2")]
    public async Task Pdf_without_text_fails_with_a_clear_reason_and_can_be_retried()
    {
        var (admin, brandId) = await BrandAsync();

        var doc = await KnowledgeApi.UploadAsync(_http, admin, brandId, KnowledgeApi.Pdf(), "scan.pdf");
        var failed = await KnowledgeApi.WaitAsync(Api, admin, brandId, doc.Id);
        var retry = await Api.SendAsync(HttpMethod.Post, $"/api/v1/brands/{brandId}/knowledge/documents/{doc.Id}/retry", admin.AccessToken);
        var failedAgain = await KnowledgeApi.WaitAsync(Api, admin, brandId, doc.Id);
        var retryReady = await Api.SendAsync(HttpMethod.Post, $"/api/v1/brands/{brandId}/knowledge/documents/{doc.Id}/retry", admin.AccessToken);

        failed.Status.Should().Be("Failed");
        failed.FailureReason.Should().Contain("No extractable text");
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        failedAgain.Status.Should().Be("Failed");
        retryReady.StatusCode.Should().Be(HttpStatusCode.OK, "a failed document can be retried again");
    }

    // ---------- KB-2 manage ----------

    [Fact]
    [Trait("Story", "KB-2")]
    public async Task Deleted_documents_disappear_from_search_and_the_list()
    {
        var (admin, brandId) = await BrandAsync();
        var doc = await KnowledgeApi.UploadAsync(_http, admin, brandId, Encoding.UTF8.GetBytes(KnowledgeApi.Policy), "returns.md");
        await KnowledgeApi.WaitAsync(Api, admin, brandId, doc.Id);

        var delete = await Api.SendAsync(HttpMethod.Delete, $"/api/v1/brands/{brandId}/knowledge/documents/{doc.Id}", admin.AccessToken);
        var result = await KnowledgeApi.SearchAsync(Api, admin, brandId, "refunds for international orders");
        var list = await Api.GetAsync<List<KnowledgeDocumentDto>>($"/api/v1/brands/{brandId}/knowledge/documents", admin.AccessToken);

        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        result.Hits.Should().BeEmpty();
        result.LowConfidence.Should().BeTrue();
        list.Should().BeEmpty();
    }

    [Fact]
    [Trait("Story", "KB-1")]
    public async Task Document_limit_per_brand_returns_plan_limit_reached()
    {
        var (admin, brandId) = await BrandAsync();
        for (var i = 0; i < 4; i++)
        {
            await KnowledgeApi.UploadAsync(_http, admin, brandId, Encoding.UTF8.GetBytes($"Document number {i} about coffee roasting"), $"doc{i}.txt");
        }

        var fifth = await KnowledgeApi.UploadRawAsync(_http, admin.AccessToken, brandId, Encoding.UTF8.GetBytes("One more about tea"), "doc5.txt");
        var usage = await Api.GetAsync<KnowledgeUsage>($"/api/v1/brands/{brandId}/knowledge/usage", admin.AccessToken);

        fifth.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        usage.Documents.Should().Be(4);
        usage.MaxDocuments.Should().Be(4);
    }

    // ---------- KB-3 FAQs and product indexing ----------

    [Fact]
    [Trait("Story", "KB-3")]
    public async Task Faqs_become_searchable_and_edits_and_deletes_follow()
    {
        var (admin, brandId) = await BrandAsync();

        var created = await Api.SendAsync(HttpMethod.Post, $"/api/v1/brands/{brandId}/knowledge/faqs", admin.AccessToken,
            new { question = "Do you deliver on Sundays?", answer = "Yes, every day from 8am to 8pm." });
        var faq = (await created.Content.ReadFromJsonAsync<FaqDto>())!;
        var found = await KnowledgeApi.SearchUntilAsync(Api, admin, brandId, "deliver on Sundays", r => r.Hits.Any(h => h.SourceId == faq.Id));

        await Api.SendAsync(HttpMethod.Put, $"/api/v1/brands/{brandId}/knowledge/faqs/{faq.Id}", admin.AccessToken,
            new { question = "Do you deliver on Sundays?", answer = "No, Monday to Saturday only." });
        var edited = await KnowledgeApi.SearchUntilAsync(Api, admin, brandId, "deliver on Sundays", r => r.Hits.Any(h => h.Text.Contains("Monday")));

        await Api.SendAsync(HttpMethod.Delete, $"/api/v1/brands/{brandId}/knowledge/faqs/{faq.Id}", admin.AccessToken);
        var gone = await KnowledgeApi.SearchAsync(Api, admin, brandId, "deliver on Sundays");

        found.Hits[0].SourceType.Should().Be(SourceTypes.Faq);
        edited.Hits.Should().ContainSingle(h => h.SourceId == faq.Id).Which.Text.Should().Contain("Monday");
        gone.Hits.Should().NotContain(h => h.SourceId == faq.Id);
    }

    [Fact]
    [Trait("Story", "PRD-1")]
    public async Task Products_are_indexed_for_agents_and_removed_when_deleted()
    {
        var (admin, brandId) = await BrandAsync();
        var product = await BrandApi.CreateProductAsync(Api, admin, brandId, BrandApi.Product("Hazelnut Cold Brew", 299m));

        var found = await KnowledgeApi.SearchUntilAsync(Api, admin, brandId, "hazelnut cold brew price", r => r.Hits.Any(h => h.SourceId == product.Id));
        await Api.SendAsync(HttpMethod.Delete, $"/api/v1/brands/{brandId}/products/{product.Id}", admin.AccessToken);
        var gone = await KnowledgeApi.SearchAsync(Api, admin, brandId, "hazelnut cold brew price");

        var hit = found.Hits.First(h => h.SourceId == product.Id);
        hit.SourceType.Should().Be(SourceTypes.Product);
        hit.Text.Should().Contain("Price: 299 INR");
        gone.Hits.Should().NotContain(h => h.SourceId == product.Id);
    }

    [Fact]
    [Trait("Story", "TEN-1")]
    public async Task Onboarding_knowledge_step_ticks_when_a_document_is_ready()
    {
        var (admin, brandId) = await BrandAsync();
        var doc = await KnowledgeApi.UploadAsync(_http, admin, brandId, Encoding.UTF8.GetBytes(KnowledgeApi.Policy), "returns.md");
        await KnowledgeApi.WaitAsync(Api, admin, brandId, doc.Id);

        (await Api.GetAsync<OnboardingStatus>("/api/v1/onboarding", admin.AccessToken)).KnowledgeUploaded.Should().BeTrue();
    }

    // ---------- Isolation (Knowledge Base Design.md → Isolation test, required gate) ----------

    [Fact]
    [Trait("Story", "TEN-3")]
    public async Task Two_tenants_with_identical_documents_only_ever_see_their_own()
    {
        var a = await Api.RegisterAndLoginAsync();
        var b = await Api.RegisterAndLoginAsync();
        var aBrand = (await BrandApi.CreateAsync(Api, a, "Same Brand")).Id;
        var bBrand = (await BrandApi.CreateAsync(Api, b, "Same Brand")).Id;
        var bytes = Encoding.UTF8.GetBytes(KnowledgeApi.Policy);
        var aDoc = await KnowledgeApi.UploadAsync(_http, a, aBrand, bytes, "returns.md");
        var bDoc = await KnowledgeApi.UploadAsync(_http, b, bBrand, bytes, "returns.md");
        await KnowledgeApi.WaitAsync(Api, a, aBrand, aDoc.Id);
        await KnowledgeApi.WaitAsync(Api, b, bBrand, bDoc.Id);

        var aResult = await KnowledgeApi.SearchAsync(Api, a, aBrand, "refunds for international orders");
        var bResult = await KnowledgeApi.SearchAsync(Api, b, bBrand, "refunds for international orders");

        aResult.Hits.Should().NotBeEmpty().And.OnlyContain(h => h.SourceId == aDoc.Id);
        bResult.Hits.Should().NotBeEmpty().And.OnlyContain(h => h.SourceId == bDoc.Id);

        foreach (var (method, path) in new[]
                 {
                     (HttpMethod.Post, $"/api/v1/brands/{aBrand}/knowledge/search"),
                     (HttpMethod.Get, $"/api/v1/brands/{aBrand}/knowledge/documents"),
                     (HttpMethod.Get, $"/api/v1/brands/{aBrand}/knowledge/documents/{aDoc.Id}"),
                     (HttpMethod.Get, $"/api/v1/brands/{bBrand}/knowledge/documents/{aDoc.Id}"),
                     (HttpMethod.Delete, $"/api/v1/brands/{bBrand}/knowledge/documents/{aDoc.Id}"),
                     (HttpMethod.Post, $"/api/v1/brands/{bBrand}/knowledge/documents/{aDoc.Id}/retry"),
                     (HttpMethod.Get, $"/api/v1/brands/{aBrand}/knowledge/faqs"),
                 })
        {
            var response = await Api.SendAsync(method, path, b.AccessToken, method == HttpMethod.Post ? new { query = "refunds" } : null);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{method} {path}");
        }

        (await KnowledgeApi.SearchAsync(Api, a, aBrand, "refunds for international orders")).Hits.Should().NotBeEmpty("A's data is untouched");
    }

    [Fact]
    [Trait("Story", "BRD-5")]
    public async Task Deleting_a_brand_removes_its_vectors()
    {
        var (admin, brandId) = await BrandAsync();
        var brandName = (await Api.GetAsync<MarketingStudio.Application.Brands.BrandDto>($"/api/v1/brands/{brandId}", admin.AccessToken)).Name;
        var doc = await KnowledgeApi.UploadAsync(_http, admin, brandId, Encoding.UTF8.GetBytes(KnowledgeApi.Policy), "returns.md");
        await KnowledgeApi.WaitAsync(Api, admin, brandId, doc.Id);

        await Api.SendAsync(HttpMethod.Delete, $"/api/v1/brands/{brandId}?confirmName={Uri.EscapeDataString(brandName)}", admin.AccessToken);

        var index = factory.Services.GetRequiredService<IVectorIndex>();
        var vector = (await factory.Services.GetRequiredService<IEmbeddingService>().EmbedAsync(["refunds"], default))[0];
        (await index.SearchAsync(admin.Auth.User.TenantId, brandId, vector, 10, null, 0, default)).Should().BeEmpty();
    }
}
