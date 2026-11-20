namespace MarketingStudio.Api.Endpoints;

/// <summary>
/// The refresh token lives only in an HttpOnly, Secure, SameSite=Strict cookie scoped to the auth
/// endpoints; it is never returned in a response body (Security Architecture.md → Authentication).
/// </summary>
internal static class RefreshCookie
{
    public const string Name = "ms_refresh";
    public const string Path = "/api/v1/auth";

    public static void Set(HttpResponse response, string token, DateTime expiresAtUtc) =>
        response.Cookies.Append(Name, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = Path,
            Expires = new DateTimeOffset(DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc)),
            IsEssential = true,
        });

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) ? value : null;

    public static void Clear(HttpResponse response) =>
        response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = Path,
        });
}
