namespace MarketingStudio.Application.Abstractions;

/// <summary>
/// Who is calling, resolved from the access token. Tenant ids supplied in request bodies or query
/// strings are never used (Multi-Tenant Design.md → Tenant Resolution).
/// </summary>
public interface ITenantContext
{
    bool IsAuthenticated { get; }
    string? UserId { get; }
    string? TenantId { get; }
    IReadOnlyList<string> Roles { get; }

    string RequireTenantId() =>
        TenantId ?? throw new UnauthenticatedException();

    string RequireUserId() =>
        UserId ?? throw new UnauthenticatedException();
}

/// <summary>Information about the current client connection, for audit records.</summary>
public interface IClientInfo
{
    string? IpAddress { get; }
}

/// <summary>Runs a unit of work in a database transaction.</summary>
public interface IUnitOfWork
{
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken);
}
