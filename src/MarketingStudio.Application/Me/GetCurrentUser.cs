using MarketingStudio.Application.Abstractions;
using MarketingStudio.Application.Auth;
using MediatR;

namespace MarketingStudio.Application.Me;

public sealed record TenantSummary(string Id, string Name, string Status);

public sealed record SubscriptionSummary(string PlanId, string Status, DateTime? TrialEndsAt, int TrialDaysRemaining);

public sealed record MeResponse(UserSummary User, TenantSummary Tenant, SubscriptionSummary? Subscription);

/// <summary>Returns the signed-in user with their tenant and subscription. All reads are tenant-scoped.</summary>
public sealed record GetCurrentUserQuery : IRequest<MeResponse>;

public sealed class GetCurrentUserQueryHandler(
    ITenantContext context,
    IUserRepository users,
    ITenantRepository tenants,
    ISubscriptionRepository subscriptions,
    TimeProvider clock) : IRequestHandler<GetCurrentUserQuery, MeResponse>
{
    public async Task<MeResponse> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(context.RequireUserId(), cancellationToken)
            ?? throw new UnauthenticatedException();
        var tenant = await tenants.GetCurrentAsync(cancellationToken)
            ?? throw new UnauthenticatedException();
        var subscription = await subscriptions.GetForCurrentTenantAsync(cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        return new MeResponse(
            user.ToSummary(),
            new TenantSummary(tenant.Id, tenant.Name, tenant.Status.ToString()),
            subscription is null
                ? null
                : new SubscriptionSummary(
                    subscription.PlanId,
                    subscription.Status.ToString(),
                    subscription.TrialEndsAt,
                    subscription.TrialDaysRemaining(now)));
    }
}
