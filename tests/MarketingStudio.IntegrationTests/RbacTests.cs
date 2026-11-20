using System.Net;
using MarketingStudio.Domain.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MarketingStudio.IntegrationTests;

/// <summary>
/// RBAC Matrix.md encoded as test data (Test Strategy.md → Mandatory Cross-Cutting Suites → RBAC).
/// Every protected endpoint is called with every role: roles without the permission must get 403,
/// roles with it must not. Mutating calls use non-existent ids or invalid bodies, so allowed roles get
/// 404/400 instead of changing data — what matters is that authorization let them through.
/// </summary>
[Collection(ApiTestGroup.Name)]
public class RbacTests(ApiFactory factory)
{
    private const string Missing = "00000000000000000000000000000000";

    public sealed record ProtectedEndpoint(string Method, string Route, string Path, object? Body, string? Permission);

    /// <summary>
    /// Every non-anonymous endpoint must appear here. Permission null = any signed-in user.
    /// <see cref="Every_protected_endpoint_has_an_rbac_entry"/> fails when a new endpoint is missing.
    /// </summary>
    public static readonly ProtectedEndpoint[] Endpoints =
    [
        new("GET", "/api/v1/me", "/api/v1/me", null, null),
        new("POST", "/api/v1/auth/resend-verification", "/api/v1/auth/resend-verification", null, null),
        new("GET", "/api/v1/users/", "/api/v1/users", null, Permissions.UsersManage),
        new("PUT", "/api/v1/users/{userId}/role", $"/api/v1/users/{Missing}/role", new { role = Roles.Viewer }, Permissions.UsersManage),
        new("POST", "/api/v1/users/{userId}/deactivate", $"/api/v1/users/{Missing}/deactivate", null, Permissions.UsersManage),
        new("POST", "/api/v1/users/{userId}/reactivate", $"/api/v1/users/{Missing}/reactivate", null, Permissions.UsersManage),
        new("GET", "/api/v1/invites/", "/api/v1/invites", null, Permissions.UsersManage),
        new("POST", "/api/v1/invites/", "/api/v1/invites", new { email = "", role = Roles.Viewer }, Permissions.UsersManage),
        new("POST", "/api/v1/invites/{inviteId}/resend", $"/api/v1/invites/{Missing}/resend", null, Permissions.UsersManage),
        new("DELETE", "/api/v1/invites/{inviteId}", $"/api/v1/invites/{Missing}", null, Permissions.UsersManage),
    ];

    public static TheoryData<string> RoleNames => new(Roles.TenantRoles);

    [Theory]
    [Trait("Story", "AUTH-6")]
    [MemberData(nameof(RoleNames))]
    public async Task Each_role_can_reach_exactly_the_endpoints_its_permissions_allow(string role)
    {
        var api = new AuthApi(factory.CreateApiClient());
        var team = await TeamBuilder.BuildAsync(api, factory);
        var session = team.ByRole[role];
        var failures = new List<string>();

        foreach (var endpoint in Endpoints)
        {
            var response = await api.SendAsync(new HttpMethod(endpoint.Method), endpoint.Path, session.AccessToken, endpoint.Body);
            var allowed = endpoint.Permission is null || Permissions.RolesHave([role], endpoint.Permission);
            var forbidden = response.StatusCode == HttpStatusCode.Forbidden;

            if (allowed == forbidden || response.StatusCode == HttpStatusCode.Unauthorized)
            {
                failures.Add($"{endpoint.Method} {endpoint.Path} as {role}: expected {(allowed ? "access" : "403")}, got {(int)response.StatusCode}");
            }
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public async Task Protected_endpoints_reject_anonymous_callers()
    {
        var api = new AuthApi(factory.CreateApiClient());

        foreach (var endpoint in Endpoints)
        {
            var response = await api.SendAsync(new HttpMethod(endpoint.Method), endpoint.Path, null, endpoint.Body);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{endpoint.Method} {endpoint.Path}");
        }
    }

    [Fact]
    public void Every_protected_endpoint_has_an_rbac_entry()
    {
        var registered = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Where(e => !e.RoutePattern.RawText!.StartsWith("/health", StringComparison.Ordinal))
            .SelectMany(e => (e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(m => $"{m} {e.RoutePattern.RawText}"))
            .ToHashSet();

        var covered = Endpoints.Select(e => $"{e.Method} {e.Route}").ToHashSet();

        registered.Except(covered).Should().BeEmpty("every protected endpoint needs an entry in RbacTests.Endpoints");
    }
}
