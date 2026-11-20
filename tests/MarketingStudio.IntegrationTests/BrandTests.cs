using System.Net;
using System.Net.Http.Json;
using MarketingStudio.Application.Abstractions;
using MarketingStudio.Application.Brands;
using MarketingStudio.Application.Onboarding;
using MarketingStudio.Domain.Users;

namespace MarketingStudio.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public class BrandTests(ApiFactory factory)
{
    private readonly HttpClient _http = factory.CreateApiClient();
    private AuthApi Api => new(_http);

    // ---------- BRD-1 Create a brand ----------

    [Fact]
    [Trait("Story", "BRD-1")]
    public async Task Brand_is_created_listed_and_readable()
    {
        var admin = await Api.RegisterAndLoginAsync();

        var created = await BrandApi.CreateAsync(Api, admin, "Acme Coffee");
        var list = await Api.GetAsync<List<BrandSummary>>("/api/v1/brands", admin.AccessToken);
        var detail = await Api.GetAsync<BrandDto>($"/api/v1/brands/{created.Id}", admin.AccessToken);

        list.Should().ContainSingle().Which.Name.Should().Be("Acme Coffee");
        detail.Timezone.Should().Be("Asia/Kolkata");
        detail.Languages.Should().Equal("en");
        detail.Voice.TonePreset.Should().Be("Friendly");
    }

    [Fact]
    [Trait("Story", "BRD-1")]
    public async Task Brand_names_are_unique_within_a_tenant_case_insensitively()
    {
        var admin = await Api.RegisterAndLoginAsync();
        await BrandApi.CreateAsync(Api, admin, "Acme Coffee");

        var duplicate = await Api.SendAsync(HttpMethod.Post, "/api/v1/brands", admin.AccessToken, BrandApi.Profile("acme coffee"));

        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await duplicate.Content.ReadAsStringAsync()).Should().Contain("already have a brand");
    }

    [Theory]
    [Trait("Story", "BRD-1")]
    [InlineData("Not/AZone", "en", "timezone")]
    [InlineData("Asia/Kolkata", "English", "languages[0]")]
    public async Task Invalid_time_zones_and_language_codes_are_rejected(string timezone, string language, string field)
    {
        var admin = await Api.RegisterAndLoginAsync();

        var response = await Api.SendAsync(HttpMethod.Post, "/api/v1/brands", admin.AccessToken, BrandApi.Profile("X Co", timezone, language));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(field.Split('[')[0]);
    }

    [Fact]
    [Trait("Story", "BRD-1")]
    public async Task Creating_beyond_the_plan_brand_limit_returns_plan_limit_reached()
    {
        var admin = await Api.RegisterAndLoginAsync();
        for (var i = 0; i < 3; i++)
        {
            await BrandApi.CreateAsync(Api, admin, $"Brand {i}");
        }

        var fourth = await Api.SendAsync(HttpMethod.Post, "/api/v1/brands", admin.AccessToken, BrandApi.Profile("Brand 4"));

        fourth.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        (await TeamBuilder.ProblemCodeAsync(fourth)).Should().Be(PlanLimitException.Code);
    }

    // ---------- BRD-2 Voice ----------

    [Fact]
    [Trait("Story", "BRD-2")]
    public async Task Voice_is_saved_with_banned_terms_deduplicated()
    {
        var admin = await Api.RegisterAndLoginAsync();
        var brand = await BrandApi.CreateAsync(Api, admin);

        var response = await Api.SendAsync(HttpMethod.Put, $"/api/v1/brands/{brand.Id}/voice", admin.AccessToken, new
        {
            tonePreset = "Playful",
            guidelines = "Warm, short sentences.",
            examplePosts = new[] { "Cold brew, done right." },
            bannedTerms = new[] { "cheap", "Cheap", " politics " },
        });
        var voice = (await response.Content.ReadFromJsonAsync<BrandDto>())!.Voice;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        voice.TonePreset.Should().Be("Playful");
        voice.BannedTerms.Should().Equal("cheap", "politics");
    }

    [Fact]
    [Trait("Story", "BRD-2")]
    public async Task Voice_limits_are_enforced()
    {
        var admin = await Api.RegisterAndLoginAsync();
        var brand = await BrandApi.CreateAsync(Api, admin);

        var response = await Api.SendAsync(HttpMethod.Put, $"/api/v1/brands/{brand.Id}/voice", admin.AccessToken, new
        {
            tonePreset = "Friendly",
            guidelines = new string('x', 2001),
            examplePosts = new[] { "1", "2", "3", "4" },
            bannedTerms = Array.Empty<string>(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Story", "BRD-2")]
    public async Task Content_manager_can_edit_voice_but_not_the_brand_profile()
    {
        var team = await TeamBuilder.BuildAsync(Api, factory);
        var brand = await BrandApi.CreateAsync(Api, team.Admin);
        var contentManager = team.ByRole[Roles.ContentManager];

        var voice = await Api.SendAsync(HttpMethod.Put, $"/api/v1/brands/{brand.Id}/voice", contentManager.AccessToken,
            new { tonePreset = "Professional", guidelines = "", examplePosts = Array.Empty<string>(), bannedTerms = Array.Empty<string>() });
        var profile = await Api.SendAsync(HttpMethod.Put, $"/api/v1/brands/{brand.Id}", contentManager.AccessToken, BrandApi.Profile("Renamed"));

        voice.StatusCode.Should().Be(HttpStatusCode.OK);
        profile.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------- BRD-5 Delete ----------

    [Fact]
    [Trait("Story", "BRD-5")]
    public async Task Deleting_needs_the_exact_name_and_removes_products_and_media()
    {
        var admin = await Api.RegisterAndLoginAsync();
        var brand = await BrandApi.CreateAsync(Api, admin, "Doomed Beans");
        var image = await BrandApi.UploadAsync(_http, admin, brand.Id);
        await BrandApi.CreateProductAsync(Api, admin, brand.Id, BrandApi.Product("Espresso", imageIds: image.Id));

        var wrong = await Api.SendAsync(HttpMethod.Delete, $"/api/v1/brands/{brand.Id}?confirmName=doomed%20beans", admin.AccessToken);
        var right = await Api.SendAsync(HttpMethod.Delete, $"/api/v1/brands/{brand.Id}?confirmName=Doomed%20Beans", admin.AccessToken);

        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        right.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Api.SendAsync(HttpMethod.Get, $"/api/v1/brands/{brand.Id}", admin.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _http.GetAsync(image.Url)).StatusCode.Should().Be(HttpStatusCode.NotFound, "the file record is gone");
        (await Api.SendAsync(HttpMethod.Get, $"/api/v1/brands/{brand.Id}/products", admin.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Story", "BRD-5")]
    public async Task Only_tenant_admins_can_delete_brands()
    {
        var team = await TeamBuilder.BuildAsync(Api, factory);
        var brand = await BrandApi.CreateAsync(Api, team.Admin, "Keep Me");

        var response = await Api.SendAsync(HttpMethod.Delete, $"/api/v1/brands/{brand.Id}?confirmName=Keep%20Me",
            team.ByRole[Roles.MarketingManager].AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------- TEN-1 Onboarding ----------

    [Fact]
    [Trait("Story", "TEN-1")]
    public async Task Onboarding_steps_tick_themselves_as_data_appears()
    {
        var admin = await Api.RegisterAndLoginAsync();
        var before = await Api.GetAsync<OnboardingStatus>("/api/v1/onboarding", admin.AccessToken);

        var brand = await BrandApi.CreateAsync(Api, admin);
        await BrandApi.CreateProductAsync(Api, admin, brand.Id, BrandApi.Product("Latte"));
        await TeamBuilder.InviteAsync(Api, factory, admin, AuthApi.NewEmail(), Roles.Viewer);
        var after = await Api.GetAsync<OnboardingStatus>("/api/v1/onboarding", admin.AccessToken);

        before.Should().BeEquivalentTo(new { WorkspaceCreated = true, BrandCreated = false, ProductAdded = false, TeamInvited = false });
        after.Should().BeEquivalentTo(new { BrandCreated = true, ProductAdded = true, TeamInvited = true });
    }
}

[Collection(ApiTestGroup.Name)]
public class ProductTests(ApiFactory factory)
{
    private readonly HttpClient _http = factory.CreateApiClient();
    private AuthApi Api => new(_http);

    [Fact]
    [Trait("Story", "PRD-1")]
    public async Task Product_crud_keeps_exact_prices()
    {
        var admin = await Api.RegisterAndLoginAsync();
        var brand = await BrandApi.CreateAsync(Api, admin);

        var created = await BrandApi.CreateProductAsync(Api, admin, brand.Id, BrandApi.Product("Cold Brew", 199.99m));
        var update = await Api.SendAsync(HttpMethod.Put, $"/api/v1/brands/{brand.Id}/products/{created.Id}", admin.AccessToken,
            BrandApi.Product("Cold Brew XL", 249.50m));
        var read = await Api.GetAsync<ProductDto>($"/api/v1/brands/{brand.Id}/products/{created.Id}", admin.AccessToken);
        var delete = await Api.SendAsync(HttpMethod.Delete, $"/api/v1/brands/{brand.Id}/products/{created.Id}", admin.AccessToken);

        created.Price.Should().Be(199.99m);
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        read.Name.Should().Be("Cold Brew XL");
        read.Price.Should().Be(249.50m);
        read.Currency.Should().Be("INR");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Api.SendAsync(HttpMethod.Get, $"/api/v1/brands/{brand.Id}/products/{created.Id}", admin.AccessToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Story", "PRD-1")]
    public async Task Products_are_searchable_filterable_and_paged()
    {
        var admin = await Api.RegisterAndLoginAsync();
        var brand = await BrandApi.CreateAsync(Api, admin);
        foreach (var (name, category) in new[] { ("Espresso", "Coffee"), ("Iced Espresso", "Coffee"), ("Green Tea", "Tea"), ("Mug", "Merch") })
        {
            await BrandApi.CreateProductAsync(Api, admin, brand.Id, BrandApi.Product(name, category: category));
        }

        var search = await Api.GetAsync<PagedResult<ProductDto>>($"/api/v1/brands/{brand.Id}/products?search=espresso", admin.AccessToken);
        var tea = await Api.GetAsync<PagedResult<ProductDto>>($"/api/v1/brands/{brand.Id}/products?category=Tea", admin.AccessToken);
        var page2 = await Api.GetAsync<PagedResult<ProductDto>>($"/api/v1/brands/{brand.Id}/products?page=2&pageSize=3", admin.AccessToken);
        var categories = await Api.GetAsync<List<string>>($"/api/v1/brands/{brand.Id}/products/categories", admin.AccessToken);
        var regexSafe = await Api.GetAsync<PagedResult<ProductDto>>($"/api/v1/brands/{brand.Id}/products?search=.*", admin.AccessToken);

        search.Items.Select(p => p.Name).Should().BeEquivalentTo("Espresso", "Iced Espresso");
        tea.Items.Should().ContainSingle().Which.Name.Should().Be("Green Tea");
        page2.Total.Should().Be(4);
        page2.Items.Should().ContainSingle();
        categories.Should().Equal("Coffee", "Merch", "Tea");
        regexSafe.Total.Should().Be(0, "search text is matched literally, not as a pattern");
    }

    [Theory]
    [Trait("Story", "PRD-1")]
    [InlineData("price-without-currency")]
    [InlineData("foreign-image")]
    public async Task Invalid_products_are_rejected(string scenario)
    {
        var admin = await Api.RegisterAndLoginAsync();
        var brand = await BrandApi.CreateAsync(Api, admin);
        var other = await BrandApi.CreateAsync(Api, admin);
        var otherImage = await BrandApi.UploadAsync(_http, admin, other.Id);

        object body = scenario == "price-without-currency"
            ? new { name = "X", price = 10m, currency = (string?)null }
            : BrandApi.Product("X", imageIds: otherImage.Id);
        var response = await Api.SendAsync(HttpMethod.Post, $"/api/v1/brands/{brand.Id}/products", admin.AccessToken, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

[Collection(ApiTestGroup.Name)]
public class MediaTests(ApiFactory factory)
{
    private readonly HttpClient _http = factory.CreateApiClient();
    private AuthApi Api => new(_http);

    [Fact]
    [Trait("Story", "BRD-3")]
    public async Task Uploaded_images_are_served_through_signed_links()
    {
        var admin = await Api.RegisterAndLoginAsync();
        var brand = await BrandApi.CreateAsync(Api, admin);

        var media = await BrandApi.UploadAsync(_http, admin, brand.Id, @"../../evil\name.png", tags: "Logo, hero ,logo");
        var file = await _http.GetAsync(media.Url);

        media.ContentType.Should().Be("image/png");
        media.FileName.Should().Be("evilname.png");
        media.Tags.Should().Equal("logo", "hero");
        file.StatusCode.Should().Be(HttpStatusCode.OK);
        file.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await file.Content.ReadAsByteArrayAsync()).Should().Equal(BrandApi.Png);
    }

    [Fact]
    [Trait("Story", "BRD-3")]
    public async Task Tampered_or_made_up_links_are_not_found()
    {
        var admin = await Api.RegisterAndLoginAsync();
        var brand = await BrandApi.CreateAsync(Api, admin);
        var media = await BrandApi.UploadAsync(_http, admin, brand.Id);

        (await _http.GetAsync(media.Url[..^2] + "xx")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _http.GetAsync("/api/v1/media/files/" + media.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [Trait("Story", "BRD-3")]
    [InlineData("not-an-image")]
    [InlineData("too-large")]
    public async Task Only_real_images_up_to_10_MB_are_accepted(string scenario)
    {
        var admin = await Api.RegisterAndLoginAsync();
        var brand = await BrandApi.CreateAsync(Api, admin);
        var bytes = scenario == "not-an-image"
            ? "<script>alert(1)</script>"u8.ToArray()
            : [.. BrandApi.Png, .. new byte[(10 * 1024 * 1024) + 1 - BrandApi.Png.Length]];

        var response = await BrandApi.UploadRawAsync(_http, admin.AccessToken, brand.Id, bytes, "photo.png");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Story", "BRD-3")]
    public async Task Jpeg_is_detected_from_its_bytes_not_the_name()
    {
        var admin = await Api.RegisterAndLoginAsync();
        var brand = await BrandApi.CreateAsync(Api, admin);

        var response = await BrandApi.UploadRawAsync(_http, admin.AccessToken, brand.Id, BrandApi.Jpeg, "photo.png", "image/png");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<MediaDto>())!.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    [Trait("Story", "BRD-3")]
    public async Task Tags_filter_the_library_and_deleting_an_image_detaches_it_from_products()
    {
        var admin = await Api.RegisterAndLoginAsync();
        var brand = await BrandApi.CreateAsync(Api, admin);
        var logo = await BrandApi.UploadAsync(_http, admin, brand.Id, "logo.png", "logo");
        await BrandApi.UploadAsync(_http, admin, brand.Id, "shop.png", "store");
        var product = await BrandApi.CreateProductAsync(Api, admin, brand.Id, BrandApi.Product("Espresso", imageIds: logo.Id));

        var logos = await Api.GetAsync<List<MediaDto>>($"/api/v1/brands/{brand.Id}/media?tag=LOGO", admin.AccessToken);
        var delete = await Api.SendAsync(HttpMethod.Delete, $"/api/v1/brands/{brand.Id}/media/{logo.Id}", admin.AccessToken);
        var after = await Api.GetAsync<ProductDto>($"/api/v1/brands/{brand.Id}/products/{product.Id}", admin.AccessToken);

        logos.Should().ContainSingle().Which.FileName.Should().Be("logo.png");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        after.ImageAssetIds.Should().BeEmpty();
        (await _http.GetAsync(logo.Url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
