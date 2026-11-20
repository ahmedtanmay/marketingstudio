using System.Net;
using System.Net.Http.Json;
using MarketingStudio.Domain.Audit;
using MarketingStudio.Domain.Users;
using MarketingStudio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MarketingStudio.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public class AuthFlowTests(ApiFactory factory)
{
    private readonly AuthApi _api = new(factory.CreateApiClient());

    private MongoContext Db => factory.Services.GetRequiredService<MongoContext>();

    // ---------- AUTH-1 Sign up ----------

    [Fact]
    [Trait("Story", "AUTH-1")]
    public async Task Sign_up_creates_tenant_admin_and_14_day_trial()
    {
        var email = AuthApi.NewEmail();

        var session = await _api.RegisterAndLoginAsync(email, company: "Acme Coffee");
        var me = await _api.MeAsync(session.AccessToken);

        me.User.Email.Should().Be(email);
        me.User.Roles.Should().Equal(Roles.TenantAdmin);
        me.User.EmailVerified.Should().BeFalse();
        me.Tenant.Name.Should().Be("Acme Coffee");
        me.Subscription!.Status.Should().Be("Trialing");
        me.Subscription.TrialDaysRemaining.Should().Be(14);
        factory.Emails.To(email).Should().ContainSingle(m => m.Subject.Contains("Verify your email"));
    }

    [Fact]
    [Trait("Story", "AUTH-1")]
    public async Task Sign_up_with_existing_email_looks_identical_and_creates_nothing()
    {
        var email = AuthApi.NewEmail();
        var first = await _api.RegisterAsync(email);
        var second = await _api.RegisterAsync(email, password: "another-long-password", company: "Other Co");

        second.StatusCode.Should().Be(first.StatusCode).And.Be(HttpStatusCode.Accepted);
        (await second.Content.ReadAsStringAsync()).Should().Be(await first.Content.ReadAsStringAsync());

        var normalized = email.ToUpperInvariant();
        (await Db.Users.CountDocumentsAsync(u => u.NormalizedEmail == normalized)).Should().Be(1);
        factory.Emails.To(email).Should().Contain(m => m.Subject.Contains("Sign-up attempt"));
    }

    [Theory]
    [Trait("Story", "AUTH-1")]
    [InlineData("short")]
    [InlineData("password123")]
    [InlineData("aaaaaaaaaa")]
    public async Task Weak_passwords_are_rejected_with_field_errors(string password)
    {
        var response = await _api.RegisterAsync(AuthApi.NewEmail(), password);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey("password");
    }

    [Fact]
    [Trait("Story", "AUTH-1")]
    public async Task Email_verification_link_confirms_the_address_once()
    {
        var email = AuthApi.NewEmail();
        (await _api.RegisterAsync(email)).EnsureSuccessStatusCode();
        var userId = factory.Emails.LinkParameter(email, "Verify your email", "userId");
        var token = factory.Emails.LinkParameter(email, "Verify your email", "token");

        var verify = await _api.PostAsync("/api/v1/auth/verify-email", new { userId, token });

        verify.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var session = await _api.LoginAsync(email);
        session.Auth.User.EmailVerified.Should().BeTrue();
    }

    [Fact]
    [Trait("Story", "AUTH-1")]
    public async Task Forged_verification_token_is_rejected()
    {
        var email = AuthApi.NewEmail();
        (await _api.RegisterAsync(email)).EnsureSuccessStatusCode();
        var userId = factory.Emails.LinkParameter(email, "Verify your email", "userId");

        var response = await _api.PostAsync("/api/v1/auth/verify-email", new { userId, token = "forged-token" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------- AUTH-2 Sign in ----------

    [Fact]
    [Trait("Story", "AUTH-2")]
    public async Task Sign_in_sets_a_secure_httponly_strict_refresh_cookie_and_no_token_in_body()
    {
        var email = AuthApi.NewEmail();
        (await _api.RegisterAsync(email)).EnsureSuccessStatusCode();

        var response = await _api.LoginRawAsync(email);
        var body = await response.Content.ReadAsStringAsync();
        var cookie = AuthApi.RawRefreshCookieHeader(response)!.ToLowerInvariant();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        cookie.Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=strict")
            .And.Contain("path=/api/v1/auth");
        body.Should().NotContain(AuthApi.ReadRefreshCookie(response)!);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    [Trait("Story", "AUTH-2")]
    public async Task Wrong_password_and_unknown_email_get_identical_responses()
    {
        var email = AuthApi.NewEmail();
        (await _api.RegisterAsync(email)).EnsureSuccessStatusCode();

        var wrongPassword = await _api.LoginRawAsync(email, "not-the-password");
        var unknownEmail = await _api.LoginRawAsync(AuthApi.NewEmail(), "not-the-password");

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownEmail.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var a = await wrongPassword.Content.ReadFromJsonAsync<ProblemDetails>();
        var b = await unknownEmail.Content.ReadFromJsonAsync<ProblemDetails>();
        a!.Detail.Should().Be(b!.Detail);
    }

    [Fact]
    [Trait("Story", "AUTH-2")]
    public async Task Five_failures_lock_the_account_even_for_the_right_password()
    {
        var email = AuthApi.NewEmail();
        (await _api.RegisterAsync(email)).EnsureSuccessStatusCode();

        for (var i = 0; i < 5; i++)
        {
            (await _api.LoginRawAsync(email, "not-the-password")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var withRightPassword = await _api.LoginRawAsync(email);

        withRightPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var normalized = email.ToUpperInvariant();
        var user = await Db.Users.Find(u => u.NormalizedEmail == normalized).SingleAsync();
        user.LockoutEnd.Should().BeAfter(DateTime.UtcNow.AddMinutes(14));
        (await Db.AuditLog.CountDocumentsAsync(a => a.ActorUserId == user.Id && a.Action == AuditActions.AccountLockedOut))
            .Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Story", "AUTH-2")]
    public async Task Successful_sign_in_is_audited()
    {
        var session = await _api.RegisterAndLoginAsync();

        (await Db.AuditLog.CountDocumentsAsync(a =>
                a.ActorUserId == session.Auth.User.Id && a.Action == AuditActions.SignInSucceeded))
            .Should().Be(1);
    }

    // ---------- AUTH-3 Refresh and sign out ----------

    [Fact]
    [Trait("Story", "AUTH-3")]
    public async Task Refresh_rotates_the_cookie_and_issues_a_working_access_token()
    {
        var session = await _api.RegisterAndLoginAsync();

        var response = await _api.RefreshRawAsync(session.RefreshCookie);
        var refreshed = await AuthApi.ReadSessionAsync(response);

        refreshed.RefreshCookie.Should().NotBe(session.RefreshCookie);
        (await _api.MeRawAsync(refreshed.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Story", "AUTH-3")]
    public async Task Reusing_a_rotated_refresh_token_revokes_the_whole_family()
    {
        var session = await _api.RegisterAndLoginAsync();
        var rotated = await AuthApi.ReadSessionAsync(await _api.RefreshRawAsync(session.RefreshCookie));

        var replay = await _api.RefreshRawAsync(session.RefreshCookie);
        var newestAfterReplay = await _api.RefreshRawAsync(rotated.RefreshCookie);

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        newestAfterReplay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Db.AuditLog.CountDocumentsAsync(a =>
                a.ActorUserId == session.Auth.User.Id && a.Action == AuditActions.RefreshTokenReuse))
            .Should().Be(1);
    }

    [Fact]
    [Trait("Story", "AUTH-3")]
    public async Task Sign_out_revokes_the_refresh_token_and_clears_the_cookie()
    {
        var session = await _api.RegisterAndLoginAsync();

        var logout = await _api.LogoutAsync(session.RefreshCookie);
        var refresh = await _api.RefreshRawAsync(session.RefreshCookie);

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        AuthApi.RawRefreshCookieHeader(logout).Should().Contain("expires=Thu, 01 Jan 1970");
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Story", "AUTH-3")]
    public async Task Refresh_without_a_cookie_is_unauthorized() =>
        (await _api.RefreshRawAsync(null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    // ---------- AUTH-4 Password reset ----------

    [Fact]
    [Trait("Story", "AUTH-4")]
    public async Task Forgot_password_responds_the_same_for_unknown_emails()
    {
        var known = AuthApi.NewEmail();
        (await _api.RegisterAsync(known)).EnsureSuccessStatusCode();

        var a = await _api.PostAsync("/api/v1/auth/forgot-password", new { email = known });
        var b = await _api.PostAsync("/api/v1/auth/forgot-password", new { email = AuthApi.NewEmail() });

        a.StatusCode.Should().Be(HttpStatusCode.Accepted);
        b.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await a.Content.ReadAsStringAsync()).Should().Be(await b.Content.ReadAsStringAsync());
    }

    [Fact]
    [Trait("Story", "AUTH-4")]
    public async Task Reset_link_works_once_and_signs_out_existing_sessions()
    {
        var email = AuthApi.NewEmail();
        var session = await _api.RegisterAndLoginAsync(email);
        (await _api.PostAsync("/api/v1/auth/forgot-password", new { email })).EnsureSuccessStatusCode();
        var token = factory.Emails.LinkParameter(email, "Reset your", "token");
        const string newPassword = "copper-meadow-whistle";

        var reset = await _api.PostAsync("/api/v1/auth/reset-password", new { email, token, newPassword });
        var reuse = await _api.PostAsync("/api/v1/auth/reset-password", new { email, token, newPassword = "another-new-password" });

        reset.StatusCode.Should().Be(HttpStatusCode.NoContent);
        reuse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _api.RefreshRawAsync(session.RefreshCookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _api.LoginRawAsync(email)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _api.LoginRawAsync(email, newPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------- Platform ----------

    [Fact]
    public async Task Readiness_check_reports_mongodb_healthy()
    {
        var response = await factory.CreateApiClient().GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
