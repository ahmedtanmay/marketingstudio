using MarketingStudio.Domain.Common;

namespace MarketingStudio.Domain.Billing;

public enum SubscriptionStatus
{
    Trialing,
    Active,
    PastDue,
    Suspended,
    Cancelled,
    Expired,
}

/// <summary>
/// A tenant's subscription. In Sprint 1 only the trial is created; Stripe fields arrive in Phase 8
/// (Billing and Subscriptions Design.md).
/// </summary>
public sealed class Subscription : ITenantOwned
{
    public const string TrialPlanId = "trial";
    public const int TrialDays = 14;

    public string Id { get; set; } = IdGenerator.NewId();
    public string TenantId { get; set; } = string.Empty;
    public string PlanId { get; set; } = TrialPlanId;
    public SubscriptionStatus Status { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public DateTime CurrentPeriodStart { get; set; }
    public DateTime CurrentPeriodEnd { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static Subscription StartTrial(string tenantId, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var end = nowUtc.AddDays(TrialDays);
        return new Subscription
        {
            TenantId = tenantId,
            PlanId = TrialPlanId,
            Status = SubscriptionStatus.Trialing,
            TrialEndsAt = end,
            CurrentPeriodStart = nowUtc,
            CurrentPeriodEnd = end,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
    }

    /// <summary>Whole days left in the trial, rounded up; 0 when not trialing or ended.</summary>
    public int TrialDaysRemaining(DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.Trialing || TrialEndsAt is null || TrialEndsAt <= nowUtc)
        {
            return 0;
        }

        return (int)Math.Ceiling((TrialEndsAt.Value - nowUtc).TotalDays);
    }
}
