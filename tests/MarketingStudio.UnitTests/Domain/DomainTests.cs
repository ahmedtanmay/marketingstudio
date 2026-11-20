using FluentAssertions;
using MarketingStudio.Domain.Auth;
using MarketingStudio.Domain.Billing;
using MarketingStudio.Domain.Tenants;
using MarketingStudio.Domain.Users;

namespace MarketingStudio.UnitTests.Domain;

public class SubscriptionTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Story", "BIL-1")]
    public void StartTrial_creates_a_14_day_trial()
    {
        var subscription = Subscription.StartTrial("tenant-1", Now);

        subscription.Status.Should().Be(SubscriptionStatus.Trialing);
        subscription.PlanId.Should().Be(Subscription.TrialPlanId);
        subscription.TrialEndsAt.Should().Be(Now.AddDays(14));
        subscription.TrialDaysRemaining(Now).Should().Be(14);
    }

    [Theory]
    [Trait("Story", "BIL-1")]
    [InlineData(0.5, 14)]
    [InlineData(13.1, 1)]
    [InlineData(14, 0)]
    [InlineData(20, 0)]
    public void TrialDaysRemaining_rounds_up_and_stops_at_zero(double daysLater, int expected)
    {
        var subscription = Subscription.StartTrial("tenant-1", Now);

        subscription.TrialDaysRemaining(Now.AddDays(daysLater)).Should().Be(expected);
    }

    [Fact]
    public void StartTrial_requires_a_tenant() =>
        FluentActions.Invoking(() => Subscription.StartTrial(" ", Now)).Should().Throw<ArgumentException>();
}

public class TenantAndUserTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Tenant_name_is_trimmed() =>
        Tenant.Create("  Acme Coffee  ", Now).Name.Should().Be("Acme Coffee");

    [Fact]
    public void New_user_gets_the_given_role_and_unconfirmed_email()
    {
        var user = User.Create("t1", " ana@example.com ", "Ana", Roles.TenantAdmin, Now);

        user.Email.Should().Be("ana@example.com");
        user.Roles.Should().Equal(Roles.TenantAdmin);
        user.EmailConfirmed.Should().BeFalse();
        user.LockoutEnabled.Should().BeTrue();
    }

    [Fact]
    public void Ids_are_unique_and_time_ordered()
    {
        var first = Tenant.Create("A", Now).Id;
        var second = Tenant.Create("B", Now).Id;

        first.Should().NotBe(second);
        string.CompareOrdinal(first, second).Should().BeNegative();
    }
}

public class RefreshTokenTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Absolute = TimeSpan.FromDays(30);

    private static RefreshToken Token(DateTime familyCreated, DateTime expires, DateTime? revoked = null) => new()
    {
        FamilyCreatedAt = familyCreated,
        ExpiresAt = expires,
        RevokedAt = revoked,
    };

    [Fact]
    [Trait("Story", "AUTH-3")]
    public void Fresh_token_is_usable() =>
        Token(Now, Now.AddDays(7)).IsUsable(Now, Absolute).Should().BeTrue();

    [Fact]
    [Trait("Story", "AUTH-3")]
    public void Revoked_token_is_not_usable() =>
        Token(Now, Now.AddDays(7), revoked: Now).IsUsable(Now, Absolute).Should().BeFalse();

    [Fact]
    [Trait("Story", "AUTH-3")]
    public void Expired_token_is_not_usable() =>
        Token(Now.AddDays(-8), Now.AddDays(-1)).IsUsable(Now, Absolute).Should().BeFalse();

    [Fact]
    [Trait("Story", "AUTH-3")]
    public void Session_older_than_30_days_is_not_usable_even_if_token_is_fresh() =>
        Token(Now.AddDays(-31), Now.AddDays(5)).IsUsable(Now, Absolute).Should().BeFalse();
}
