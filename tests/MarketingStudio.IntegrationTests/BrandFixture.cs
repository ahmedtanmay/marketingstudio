using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MarketingStudio.Application.Brands;

namespace MarketingStudio.IntegrationTests;

/// <summary>Helpers for brand, product and media calls.</summary>
public static class BrandApi
{
    /// <summary>A valid 1×1 PNG.</summary>
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    /// <summary>Starts like a JPEG (the server checks the first bytes, not the extension).</summary>
    public static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00];

    public static object Profile(string name, string timezone = "Asia/Kolkata", params string[] languages) => new
    {
        name,
        industry = "Food & Beverage",
        languages = languages.Length == 0 ? ["en"] : languages,
        timezone,
        description = "Speciality coffee roasters",
        websiteUrl = "https://example.com",
        targetAudience = "Urban professionals 25–40",
    };

    public static async Task<BrandDto> CreateAsync(AuthApi api, Session session, string? name = null)
    {
        var response = await api.SendAsync(HttpMethod.Post, "/api/v1/brands", session.AccessToken, Profile(name ?? $"Brand {Guid.NewGuid():N}"[..20]));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<BrandDto>())!;
    }

    public static object Product(string name, decimal? price = 19.99m, string? category = "Coffee", params string[] imageIds) => new
    {
        name,
        description = "Smooth and chocolatey",
        category,
        price,
        currency = price.HasValue ? "INR" : null,
        features = new[] { "Single origin", "Fresh roasted" },
        url = "https://example.com/products/1",
        imageAssetIds = imageIds,
    };

    public static async Task<ProductDto> CreateProductAsync(AuthApi api, Session session, string brandId, object product)
    {
        var response = await api.SendAsync(HttpMethod.Post, $"/api/v1/brands/{brandId}/products", session.AccessToken, product);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProductDto>())!;
    }

    public static async Task<HttpResponseMessage> UploadRawAsync(
        HttpClient http, string? accessToken, string brandId, byte[] bytes, string fileName, string contentType = "image/png", string? tags = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        if (tags is not null)
        {
            form.Add(new StringContent(tags), "tags");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/brands/{brandId}/media") { Content = form };
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await http.SendAsync(request);
    }

    public static async Task<MediaDto> UploadAsync(HttpClient http, Session session, string brandId, string fileName = "logo.png", string? tags = null)
    {
        var response = await UploadRawAsync(http, session.AccessToken, brandId, Png, fileName, tags: tags);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<MediaDto>())!;
    }
}
