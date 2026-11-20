namespace MarketingStudio.Application.Abstractions;

/// <summary>Base for expected application errors that map to an HTTP status in the API.</summary>
public abstract class AppException(string message) : Exception(message);

/// <summary>401 — caller is not signed in or the credentials/token are invalid. Messages are deliberately generic.</summary>
public sealed class UnauthenticatedException(string message = "Authentication is required.")
    : AppException(message);

/// <summary>404 — resource does not exist for this tenant (also used for other tenants' resources).</summary>
public sealed class NotFoundException(string message = "The requested resource was not found.")
    : AppException(message);

/// <summary>
/// 402 — a plan limit blocks the action (Billing and Subscriptions Design.md → Entitlement Service).
/// The portal turns this into an upgrade prompt.
/// </summary>
public sealed class PlanLimitException(string limitKey, long limit, string message)
    : AppException(message)
{
    public const string Code = "PLAN_LIMIT_REACHED";
    public string LimitKey { get; } = limitKey;
    public long Limit { get; } = limit;
}
