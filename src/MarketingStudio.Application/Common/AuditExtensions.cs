using MarketingStudio.Application.Abstractions;
using MarketingStudio.Domain.Audit;

namespace MarketingStudio.Application.Common;

internal static class AuditExtensions
{
    public static Task RecordAsync(
        this IAuditLog audit,
        string action,
        string? tenantId,
        string? actorUserId,
        string targetType,
        string? targetId,
        string? ip,
        DateTime nowUtc,
        CancellationToken cancellationToken,
        Dictionary<string, string>? metadata = null) =>
        audit.WriteAsync(
            new AuditEntry
            {
                TenantId = tenantId,
                ActorUserId = actorUserId,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                Ip = ip,
                CreatedAt = nowUtc,
                Metadata = metadata ?? [],
            },
            cancellationToken);
}
