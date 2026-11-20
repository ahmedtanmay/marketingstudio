using System.Net;
using System.Net.Http.Json;
using MarketingStudio.Api.Endpoints;
using MarketingStudio.Application.Users;
using MarketingStudio.Domain.Users;

namespace MarketingStudio.IntegrationTests;

/// <summary>A tenant with one signed-in member per tenant role, built through the real invite flow.</summary>
public sealed record Team(string TenantId, IReadOnlyDictionary<string, Session> ByRole)
{
    public Session Admin => ByRole[Roles.TenantAdmin];
}

public static class TeamBuilder
{
    public static async Task<string> InviteAsync(AuthApi api, ApiFactory factory, Session admin, string email, string role)
    {
        var response = await api.SendAsync(HttpMethod.Post, "/api/v1/invites", admin.AccessToken, new { email, role });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return factory.Emails.LinkParameter(email, "invited", "token");
    }

    public static async Task<Session> AcceptAsync(AuthApi api, string token, string name = "Invited Person")
    {
        var response = await api.PostAsync("/api/v1/invites/accept", new { token, name, password = AuthApi.Password });
        return await AuthApi.ReadSessionAsync(response);
    }

    public static async Task<Team> BuildAsync(AuthApi api, ApiFactory factory)
    {
        var admin = await api.RegisterAndLoginAsync();
        var members = new Dictionary<string, Session> { [Roles.TenantAdmin] = admin };

        foreach (var role in Roles.TenantRoles.Where(r => r != Roles.TenantAdmin))
        {
            var email = AuthApi.NewEmail();
            var token = await InviteAsync(api, factory, admin, email, role);
            members[role] = await AcceptAsync(api, token, role);
        }

        return new Team(admin.Auth.User.TenantId, members);
    }

    public static async Task<IReadOnlyList<UserListItem>> ListUsersAsync(AuthApi api, Session admin) =>
        await api.GetAsync<List<UserListItem>>("/api/v1/users", admin.AccessToken);

    public static async Task<IReadOnlyList<InviteListItem>> ListInvitesAsync(AuthApi api, Session admin) =>
        await api.GetAsync<List<InviteListItem>>("/api/v1/invites", admin.AccessToken);

    public static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    internal static AuthResponse Body(Session session) => session.Auth;
}
