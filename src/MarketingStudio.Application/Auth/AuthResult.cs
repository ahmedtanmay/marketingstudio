namespace MarketingStudio.Application.Auth;

public sealed record UserSummary(
    string Id,
    string TenantId,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    bool EmailVerified);

/// <summary>
/// Result of sign-in or refresh. The API puts <see cref="RefreshToken"/> in an HttpOnly cookie
/// and never returns it in the response body.
/// </summary>
public sealed record AuthResult(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserSummary User);

internal static class UserSummaryMapper
{
    public static UserSummary ToSummary(this Domain.Users.User user) =>
        new(user.Id, user.TenantId, user.Email, user.DisplayName, user.Roles, user.EmailConfirmed);
}
