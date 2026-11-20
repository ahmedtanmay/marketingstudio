using MarketingStudio.Application.Abstractions;
using MarketingStudio.Domain.Billing;

namespace MarketingStudio.Application.Users;

/// <summary>
/// Seats in use = active users + open invites (an invite reserves a seat, so accepting it can never
/// exceed the limit). Replaced by IEntitlementService in Phase 5.
/// </summary>
public sealed class SeatGuard(
    IUserRepository users,
    IInviteRepository invites,
    ISubscriptionRepository subscriptions,
    IPlanLimits limits,
    TimeProvider clock)
{
    public const string LimitKey = "seats.max";

    public async Task EnsureSeatAvailableAsync(CancellationToken cancellationToken)
    {
        var subscription = await subscriptions.GetForCurrentTenantAsync(cancellationToken);
        var max = limits.MaxSeats(subscription?.PlanId ?? Subscription.TrialPlanId);
        var now = clock.GetUtcNow().UtcDateTime;
        var used = await users.CountActiveAsync(cancellationToken) + await invites.CountOpenAsync(now, cancellationToken);

        if (used >= max)
        {
            throw new PlanLimitException(
                LimitKey,
                max,
                $"Your plan includes {max} seats and all are in use (including pending invites). "
                + "Upgrade your plan or remove a pending invite.");
        }
    }
}
