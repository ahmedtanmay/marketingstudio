using System.Net;
using MarketingStudio.Application.Users;
using MarketingStudio.Domain.Audit;
using MarketingStudio.Domain.Users;
using MarketingStudio.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MarketingStudio.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public class TeamManagementTests(ApiFactory factory)
{
    private readonly AuthApi _api = new(factory.CreateApiClient());

    private MongoContext Db => factory.Services.GetRequiredService<MongoContext>();

    // ---------- AUTH-5 Invites ----------

    [Fact]
    [Trait("Story", "AUTH-5")]
    public async Task Invitee_accepts_and_joins_the_inviting_tenant_with_the_chosen_role()
    {
        var admin = await _api.RegisterAndLoginAsync(company: "Invite Co");
        var email = AuthApi.NewEmail();
        var token = await TeamBuilder.InviteAsync(_api, factory, admin, email, Roles.ContentManager);

        var details = await _api.GetAsync<InviteDetails>($"/api/v1/invites/lookup?token={token}", admin.AccessToken);
        var member = await TeamBuilder.AcceptAsync(_api, token, "Cora Content");
        var me = await _api.MeAsync(member.AccessToken);

        details.WorkspaceName.Should().Be("Invite Co");
        details.Email.Should().Be(email);
        me.Tenant.Id.Should().Be(admin.Auth.User.TenantId);
        me.User.Roles.Should().Equal(Roles.ContentManager);
        me.User.EmailVerified.Should().BeTrue("they proved ownership by clicking the emailed link");
        me.Permissions.Should().Contain(Permissions.ContentSubmit).And.NotContain(Permissions.ContentApprove)
            .And.NotContain(Permissions.UsersManage);
        (await TeamBuilder.ListInvitesAsync(_api, admin)).Should().NotContain(i => i.Email == email);
    }

    [Fact]
    [Trait("Story", "AUTH-5")]
    public async Task Invite_link_works_only_once()
    {
        var admin = await _api.RegisterAndLoginAsync();
        var token = await TeamBuilder.InviteAsync(_api, factory, admin, AuthApi.NewEmail(), Roles.Viewer);
        await TeamBuilder.AcceptAsync(_api, token);

        var again = await _api.PostAsync("/api/v1/invites/accept", new { token, name = "Again", password = AuthApi.Password });

        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Story", "AUTH-5")]
    public async Task Re_inviting_renews_the_same_invite_and_kills_the_old_link()
    {
        var admin = await _api.RegisterAndLoginAsync();
        var email = AuthApi.NewEmail();
        var first = await TeamBuilder.InviteAsync(_api, factory, admin, email, Roles.Viewer);
        var second = await TeamBuilder.InviteAsync(_api, factory, admin, email, Roles.MarketingManager);

        var invites = await TeamBuilder.ListInvitesAsync(_api, admin);
        var oldLink = await _api.SendAsync(HttpMethod.Get, $"/api/v1/invites/lookup?token={first}", null);
        var member = await TeamBuilder.AcceptAsync(_api, second);

        invites.Should().ContainSingle(i => i.Email == email).Which.Role.Should().Be(Roles.MarketingManager);
        oldLink.StatusCode.Should().Be(HttpStatusCode.NotFound);
        member.Auth.User.Roles.Should().Equal(Roles.MarketingManager);
    }

    [Fact]
    [Trait("Story", "AUTH-5")]
    public async Task Revoked_invite_link_stops_working()
    {
        var admin = await _api.RegisterAndLoginAsync();
        var email = AuthApi.NewEmail();
        var token = await TeamBuilder.InviteAsync(_api, factory, admin, email, Roles.Viewer);
        var inviteId = (await TeamBuilder.ListInvitesAsync(_api, admin)).Single(i => i.Email == email).Id;

        var revoke = await _api.SendAsync(HttpMethod.Delete, $"/api/v1/invites/{inviteId}", admin.AccessToken);
        var accept = await _api.PostAsync("/api/v1/invites/accept", new { token, name = "Late", password = AuthApi.Password });

        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);
        accept.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Story", "AUTH-5")]
    public async Task Inviting_beyond_the_seat_limit_returns_plan_limit_reached()
    {
        var admin = await _api.RegisterAndLoginAsync();

        // Admin uses 1 seat; pending invites reserve the rest.
        for (var i = 1; i < ApiFactory.TrialSeats; i++)
        {
            await TeamBuilder.InviteAsync(_api, factory, admin, AuthApi.NewEmail(), Roles.Viewer);
        }

        var overLimit = await _api.SendAsync(
            HttpMethod.Post, "/api/v1/invites", admin.AccessToken, new { email = AuthApi.NewEmail(), role = Roles.Viewer });

        overLimit.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        (await TeamBuilder.ProblemCodeAsync(overLimit)).Should().Be("PLAN_LIMIT_REACHED");
    }

    [Fact]
    [Trait("Story", "AUTH-5")]
    public async Task Existing_members_and_existing_accounts_cannot_be_invited_into_a_second_seat()
    {
        var admin = await _api.RegisterAndLoginAsync();
        var otherTenantUser = await _api.RegisterAndLoginAsync();

        var self = await _api.SendAsync(
            HttpMethod.Post, "/api/v1/invites", admin.AccessToken, new { email = admin.Auth.User.Email, role = Roles.Viewer });
        var token = await TeamBuilder.InviteAsync(_api, factory, admin, otherTenantUser.Auth.User.Email, Roles.Viewer);
        var accept = await _api.PostAsync("/api/v1/invites/accept", new { token, name = "Dup", password = AuthApi.Password });

        self.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        accept.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------- AUTH-6 Manage users and roles ----------

    [Fact]
    [Trait("Story", "AUTH-6")]
    public async Task Admin_changes_a_members_role_and_it_is_audited()
    {
        var team = await TeamBuilder.BuildAsync(_api, factory);
        var viewer = team.ByRole[Roles.Viewer];

        var change = await _api.SendAsync(
            HttpMethod.Put, $"/api/v1/users/{viewer.Auth.User.Id}/role", team.Admin.AccessToken, new { role = Roles.MarketingManager });
        var users = await TeamBuilder.ListUsersAsync(_api, team.Admin);

        change.StatusCode.Should().Be(HttpStatusCode.NoContent);
        users.Single(u => u.Id == viewer.Auth.User.Id).Role.Should().Be(Roles.MarketingManager);
        (await Db.AuditLog.CountDocumentsAsync(a => a.TargetId == viewer.Auth.User.Id && a.Action == AuditActions.UserRoleChanged))
            .Should().Be(1);
    }

    [Fact]
    [Trait("Story", "AUTH-6")]
    public async Task The_last_tenant_admin_cannot_be_downgraded()
    {
        var admin = await _api.RegisterAndLoginAsync();

        var response = await _api.SendAsync(
            HttpMethod.Put, $"/api/v1/users/{admin.Auth.User.Id}/role", admin.AccessToken, new { role = Roles.Viewer });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _api.MeAsync(admin.AccessToken)).User.Roles.Should().Equal(Roles.TenantAdmin);
    }

    [Fact]
    [Trait("Story", "AUTH-6")]
    public async Task Super_admin_role_cannot_be_granted_by_a_tenant_admin()
    {
        var team = await TeamBuilder.BuildAsync(_api, factory);

        var response = await _api.SendAsync(
            HttpMethod.Put, $"/api/v1/users/{team.ByRole[Roles.Viewer].Auth.User.Id}/role", team.Admin.AccessToken,
            new { role = Roles.SuperAdmin });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Story", "AUTH-6")]
    public async Task Deactivated_member_cannot_sign_in_or_refresh_and_can_be_reactivated()
    {
        var team = await TeamBuilder.BuildAsync(_api, factory);
        var member = team.ByRole[Roles.ContentManager];

        var deactivate = await _api.SendAsync(
            HttpMethod.Post, $"/api/v1/users/{member.Auth.User.Id}/deactivate", team.Admin.AccessToken);

        deactivate.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _api.RefreshRawAsync(member.RefreshCookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _api.LoginRawAsync(member.Auth.User.Email)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var reactivate = await _api.SendAsync(
            HttpMethod.Post, $"/api/v1/users/{member.Auth.User.Id}/reactivate", team.Admin.AccessToken);

        reactivate.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _api.LoginRawAsync(member.Auth.User.Email)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Story", "AUTH-6")]
    public async Task Admin_cannot_deactivate_themselves()
    {
        var admin = await _api.RegisterAndLoginAsync();

        var response = await _api.SendAsync(
            HttpMethod.Post, $"/api/v1/users/{admin.Auth.User.Id}/deactivate", admin.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
