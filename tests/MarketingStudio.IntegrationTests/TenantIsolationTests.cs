using System.Net;
using System.Text;
using MarketingStudio.Domain.Users;
using Microsoft.IdentityModel.Tokens;

namespace MarketingStudio.IntegrationTests;

/// <summary>
/// TEN-3: the two-tenants-with-identical-data fixture from Test Strategy.md. Grows with every new
/// endpoint; Sprint 2 adds the route-coverage check.
/// </summary>
[Collection(ApiTestGroup.Name)]
public class TenantIsolationTests(ApiFactory factory)
{
    private readonly AuthApi _api = new(factory.CreateApiClient());

    [Fact]
    [Trait("Story", "TEN-3")]
    public async Task Two_tenants_with_identical_names_each_see_only_their_own_workspace()
    {
        var a = await _api.RegisterAndLoginAsync(company: "Same Name Ltd");
        var b = await _api.RegisterAndLoginAsync(company: "Same Name Ltd");

        var meA = await _api.MeAsync(a.AccessToken);
        var meB = await _api.MeAsync(b.AccessToken);

        meA.Tenant.Id.Should().NotBe(meB.Tenant.Id);
        meA.Tenant.Id.Should().Be(a.Auth.User.TenantId);
        meB.Tenant.Id.Should().Be(b.Auth.User.TenantId);
        meA.User.Id.Should().Be(a.Auth.User.Id);
    }

    [Fact]
    [Trait("Story", "TEN-3")]
    public async Task Tenant_id_in_the_query_string_is_ignored()
    {
        var a = await _api.RegisterAndLoginAsync();
        var b = await _api.RegisterAndLoginAsync();

        var response = await _api.MeRawAsync(a.AccessToken, $"/api/v1/me?tenantId={b.Auth.User.TenantId}");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain(a.Auth.User.TenantId).And.NotContain(b.Auth.User.TenantId);
    }

    [Fact]
    [Trait("Story", "TEN-3")]
    public async Task Protected_endpoints_reject_missing_and_forged_tokens()
    {
        var a = await _api.RegisterAndLoginAsync();

        (await _api.MeRawAsync(null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _api.MeRawAsync("not.a.jwt")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _api.MeRawAsync(TamperTenant(a.AccessToken, "someone-else"))).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Story", "TEN-3")]
    public async Task Admins_cannot_see_or_change_another_tenants_members_or_invites()
    {
        var a = await TeamBuilder.BuildAsync(_api, factory);
        var b = await _api.RegisterAndLoginAsync();
        var aViewer = a.ByRole[Roles.Viewer].Auth.User.Id;
        var aInviteEmail = AuthApi.NewEmail();
        await TeamBuilder.InviteAsync(_api, factory, a.Admin, aInviteEmail, Roles.Viewer);
        var aInviteId = (await TeamBuilder.ListInvitesAsync(_api, a.Admin)).Single(i => i.Email == aInviteEmail).Id;

        var bUsers = await TeamBuilder.ListUsersAsync(_api, b);
        var bInvites = await TeamBuilder.ListInvitesAsync(_api, b);
        var changeRole = await _api.SendAsync(HttpMethod.Put, $"/api/v1/users/{aViewer}/role", b.AccessToken, new { role = Roles.TenantAdmin });
        var deactivate = await _api.SendAsync(HttpMethod.Post, $"/api/v1/users/{aViewer}/deactivate", b.AccessToken);
        var revoke = await _api.SendAsync(HttpMethod.Delete, $"/api/v1/invites/{aInviteId}", b.AccessToken);
        var resend = await _api.SendAsync(HttpMethod.Post, $"/api/v1/invites/{aInviteId}/resend", b.AccessToken);

        bUsers.Should().ContainSingle().Which.Id.Should().Be(b.Auth.User.Id);
        bInvites.Should().BeEmpty();
        changeRole.StatusCode.Should().Be(HttpStatusCode.NotFound);
        deactivate.StatusCode.Should().Be(HttpStatusCode.NotFound);
        revoke.StatusCode.Should().Be(HttpStatusCode.NotFound);
        resend.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await TeamBuilder.ListInvitesAsync(_api, a.Admin)).Should().Contain(i => i.Id == aInviteId);
        (await TeamBuilder.ListUsersAsync(_api, a.Admin)).Single(u => u.Id == aViewer).Role.Should().Be(Roles.Viewer);
    }

    [Fact]
    [Trait("Story", "TEN-3")]
    public async Task Tenants_with_identically_named_brands_cannot_reach_each_others_brands_products_or_media()
    {
        var http = factory.CreateApiClient();
        var a = await _api.RegisterAndLoginAsync();
        var b = await _api.RegisterAndLoginAsync();
        var aBrand = await BrandApi.CreateAsync(_api, a, "Same Brand");
        var bBrand = await BrandApi.CreateAsync(_api, b, "Same Brand");
        var aImage = await BrandApi.UploadAsync(http, a, aBrand.Id);
        var aProduct = await BrandApi.CreateProductAsync(_api, a, aBrand.Id, BrandApi.Product("Secret Blend", imageIds: aImage.Id));

        var bList = await _api.GetAsync<List<MarketingStudio.Application.Brands.BrandSummary>>("/api/v1/brands", b.AccessToken);
        var attempts = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Get, $"/api/v1/brands/{aBrand.Id}", null),
            (HttpMethod.Put, $"/api/v1/brands/{aBrand.Id}", BrandApi.Profile("Hijacked")),
            (HttpMethod.Put, $"/api/v1/brands/{aBrand.Id}/voice", new { tonePreset = "Playful", guidelines = "", examplePosts = Array.Empty<string>(), bannedTerms = Array.Empty<string>() }),
            (HttpMethod.Delete, $"/api/v1/brands/{aBrand.Id}?confirmName=Same%20Brand", null),
            (HttpMethod.Get, $"/api/v1/brands/{aBrand.Id}/products", null),
            (HttpMethod.Get, $"/api/v1/brands/{aBrand.Id}/products/{aProduct.Id}", null),
            (HttpMethod.Put, $"/api/v1/brands/{aBrand.Id}/products/{aProduct.Id}", BrandApi.Product("Hijacked")),
            (HttpMethod.Delete, $"/api/v1/brands/{aBrand.Id}/products/{aProduct.Id}", null),
            (HttpMethod.Get, $"/api/v1/brands/{bBrand.Id}/products/{aProduct.Id}", null),
            (HttpMethod.Get, $"/api/v1/brands/{aBrand.Id}/media", null),
            (HttpMethod.Delete, $"/api/v1/brands/{aBrand.Id}/media/{aImage.Id}", null),
            (HttpMethod.Delete, $"/api/v1/brands/{bBrand.Id}/media/{aImage.Id}", null),
        };

        bList.Should().ContainSingle().Which.Id.Should().Be(bBrand.Id);
        foreach (var (method, path, body) in attempts)
        {
            var response = await _api.SendAsync(method, path, b.AccessToken, body);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{method} {path}");
        }

        var upload = await BrandApi.UploadRawAsync(http, b.AccessToken, aBrand.Id, BrandApi.Png, "x.png");
        upload.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Tenant A's data is untouched.
        (await _api.GetAsync<MarketingStudio.Application.Brands.BrandDto>($"/api/v1/brands/{aBrand.Id}", a.AccessToken)).Name.Should().Be("Same Brand");
        (await _api.GetAsync<MarketingStudio.Application.Brands.ProductDto>($"/api/v1/brands/{aBrand.Id}/products/{aProduct.Id}", a.AccessToken))
            .ImageAssetIds.Should().Equal(aImage.Id);
    }

    /// <summary>Swaps the tid claim in the payload but keeps the original signature.</summary>
    private static string TamperTenant(string jwt, string tenantId)
    {
        var parts = jwt.Split('.');
        var payload = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[1]));
        var tampered = System.Text.RegularExpressions.Regex.Replace(payload, "\"tid\":\"[^\"]+\"", $"\"tid\":\"{tenantId}\"");
        parts[1] = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(tampered));
        return string.Join('.', parts);
    }
}
