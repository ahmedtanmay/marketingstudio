using System.Security.Claims;
using MarketingStudio.Application.Abstractions;
using MarketingStudio.Infrastructure.Tokens;

namespace MarketingStudio.Api.Security;

/// <summary>
/// Tenant context taken only from the validated access token. Tenant ids in request bodies,
/// routes or query strings are never read (Multi-Tenant Design.md → Tenant Resolution).
/// </summary>
internal sealed class HttpTenantContext(IHttpContextAccessor accessor, BackgroundTenantScope background) : ITenantContext
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    private bool InRequest => accessor.HttpContext is not null;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string? UserId => IsAuthenticated ? Principal!.FindFirstValue(ClaimNames.Subject) : InRequest ? null : background.UserId;

    /// <summary>From the token in requests; from the job (BackgroundTenantScope) in background work.</summary>
    public string? TenantId => IsAuthenticated ? Principal!.FindFirstValue(ClaimNames.TenantId) : InRequest ? null : background.TenantId;

    public IReadOnlyList<string> Roles =>
        IsAuthenticated ? Principal!.FindAll(ClaimNames.Role).Select(c => c.Value).ToList() : [];
}

internal sealed class HttpClientInfo(IHttpContextAccessor accessor) : IClientInfo
{
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
