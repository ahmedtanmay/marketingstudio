using System.Net;
using System.Text;
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
