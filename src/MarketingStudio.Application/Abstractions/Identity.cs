using MarketingStudio.Domain.Users;

namespace MarketingStudio.Application.Abstractions;

public enum PasswordSignInResult
{
    Succeeded,
    Failed,
    LockedOut,
    NotAllowed,
}

public sealed record ResetPasswordOutcome(bool Succeeded, bool InvalidToken, IReadOnlyList<string> PasswordErrors)
{
    public static ResetPasswordOutcome Success { get; } = new(true, false, []);
    public static ResetPasswordOutcome BadToken { get; } = new(false, true, []);
}

/// <summary>User credential operations, implemented with ASP.NET Core Identity.</summary>
public interface IIdentityService
{
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken);
    Task<User?> FindByIdAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Returns password policy violations; empty when the password is acceptable.</summary>
    Task<IReadOnlyList<string>> ValidatePasswordAsync(string password);

    /// <summary>Creates the user with a hashed password. Throws if creation fails.</summary>
    Task CreateUserAsync(User user, string password, CancellationToken cancellationToken);

    /// <summary>Saves changes to a user (optimistic concurrency). Throws on conflict.</summary>
    Task UpdateUserAsync(User user, CancellationToken cancellationToken);

    /// <summary>Checks the password and applies lockout rules.</summary>
    Task<PasswordSignInResult> CheckPasswordSignInAsync(User user, string password, CancellationToken cancellationToken);

    Task RecordSuccessfulSignInAsync(User user, CancellationToken cancellationToken);

    Task<string> GenerateEmailConfirmationTokenAsync(User user);
    Task<bool> ConfirmEmailAsync(string userId, string token, CancellationToken cancellationToken);

    Task<string> GeneratePasswordResetTokenAsync(User user);
    Task<ResetPasswordOutcome> ResetPasswordAsync(User user, string token, string newPassword);
}

public sealed record AccessToken(string Token, DateTime ExpiresAt);

public interface IAccessTokenService
{
    AccessToken CreateAccessToken(User user);
}

public sealed record IssuedRefreshToken(string Token, DateTime ExpiresAt);

public enum RefreshFailure
{
    Invalid,
    Expired,
    ReuseDetected,
}

public sealed record RefreshRotation(
    bool Succeeded,
    string? UserId,
    string? TenantId,
    IssuedRefreshToken? NewToken,
    RefreshFailure? Failure)
{
    public static RefreshRotation Fail(RefreshFailure failure, string? userId = null, string? tenantId = null) =>
        new(false, userId, tenantId, null, failure);
}

public interface IRefreshTokenService
{
    /// <summary>Starts a new token family for a fresh sign-in.</summary>
    Task<IssuedRefreshToken> IssueAsync(User user, CancellationToken cancellationToken);

    /// <summary>Exchanges a refresh token for a new one; reuse of a rotated token revokes the family.</summary>
    Task<RefreshRotation> RotateAsync(string rawToken, CancellationToken cancellationToken);

    /// <summary>Revokes the family the token belongs to (sign-out).</summary>
    Task RevokeFamilyAsync(string rawToken, CancellationToken cancellationToken);

    Task RevokeAllForUserAsync(string userId, CancellationToken cancellationToken);
}
