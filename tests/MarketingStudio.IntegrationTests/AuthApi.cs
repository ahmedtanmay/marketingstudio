using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MarketingStudio.Api.Endpoints;
using MarketingStudio.Application.Me;

namespace MarketingStudio.IntegrationTests;

/// <summary>Small client for the auth endpoints that handles the refresh cookie by hand.</summary>
public sealed class AuthApi(HttpClient http)
{
    public const string Password = "violet-harbour-lantern";

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    public Task<HttpResponseMessage> RegisterAsync(string email, string password = Password, string company = "Acme Coffee") =>
        http.PostAsJsonAsync("/api/v1/auth/register", new { name = "Ana Test", email, password, companyName = company });

    public Task<HttpResponseMessage> LoginRawAsync(string email, string password = Password) =>
        http.PostAsJsonAsync("/api/v1/auth/login", new { email, password });

    public async Task<Session> LoginAsync(string email, string password = Password)
    {
        var response = await LoginRawAsync(email, password);
        return await ReadSessionAsync(response);
    }

    public async Task<Session> RegisterAndLoginAsync(string? email = null, string company = "Acme Coffee")
    {
        email ??= NewEmail();
        (await RegisterAsync(email, company: company)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        return await LoginAsync(email);
    }

    public Task<HttpResponseMessage> RefreshRawAsync(string? refreshCookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        if (refreshCookie is not null)
        {
            request.Headers.Add("Cookie", $"{RefreshCookie.Name}={refreshCookie}");
        }

        return http.SendAsync(request);
    }

    public Task<HttpResponseMessage> LogoutAsync(string refreshCookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        request.Headers.Add("Cookie", $"{RefreshCookie.Name}={refreshCookie}");
        return http.SendAsync(request);
    }

    public Task<HttpResponseMessage> MeRawAsync(string? accessToken, string path = "/api/v1/me")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return http.SendAsync(request);
    }

    public async Task<MeResponse> MeAsync(string accessToken)
    {
        var response = await MeRawAsync(accessToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<MeResponse>())!;
    }

    public Task<HttpResponseMessage> PostAsync(string path, object body) => http.PostAsJsonAsync(path, body);

    public static async Task<Session> ReadSessionAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        return new Session(body, ReadRefreshCookie(response) ?? throw new InvalidOperationException("No refresh cookie"));
    }

    public static string? ReadRefreshCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return null;
        }

        var cookie = cookies.FirstOrDefault(c => c.StartsWith(RefreshCookie.Name + "=", StringComparison.Ordinal));
        var value = cookie?.Split(';')[0][(RefreshCookie.Name.Length + 1)..];
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public static string? RawRefreshCookieHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.FirstOrDefault(c => c.StartsWith(RefreshCookie.Name + "=", StringComparison.Ordinal))
            : null;
}

public sealed record Session(AuthResponse Auth, string RefreshCookie)
{
    public string AccessToken => Auth.AccessToken;
}
