using MarketingStudio.Domain.Common;

namespace MarketingStudio.Domain.Audit;

public static class AuditActions
{
    public const string SignInSucceeded = "auth.sign_in_succeeded";
    public const string SignInFailed = "auth.sign_in_failed";
    public const string AccountLockedOut = "auth.locked_out";
    public const string SignedOut = "auth.signed_out";
    public const string RefreshTokenReuse = "auth.refresh_token_reuse";
    public const string PasswordResetRequested = "auth.password_reset_requested";
    public const string PasswordReset = "auth.password_reset";
    public const string EmailVerified = "auth.email_verified";
    public const string TenantCreated = "tenant.created";
    public const string UserInvited = "user.invited";
    public const string InviteResent = "user.invite_resent";
    public const string InviteRevoked = "user.invite_revoked";
    public const string InviteAccepted = "user.invite_accepted";
    public const string UserRoleChanged = "user.role_changed";
    public const string UserDeactivated = "user.deactivated";
    public const string UserReactivated = "user.reactivated";
}

/// <summary>Append-only audit record (Security Architecture.md → Audit Logging).</summary>
public sealed class AuditEntry
{
    public string Id { get; set; } = IdGenerator.NewId();
    public string? TenantId { get; set; }
    public string? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
    public string? Ip { get; set; }
    public DateTime CreatedAt { get; set; }
}
