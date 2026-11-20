using MarketingStudio.Application.Auth;
using MarketingStudio.Application.Me;
using MediatR;

namespace MarketingStudio.Api.Endpoints;

public sealed record AuthResponse(string AccessToken, DateTime AccessTokenExpiresAt, UserSummary User);

public sealed record MessageResponse(string Message);

internal static class AuthEndpoints
{
    public const string AuthRateLimitPolicy = "auth";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/auth")
            .WithTags("Auth")
            .RequireRateLimiting(AuthRateLimitPolicy);

        auth.MapPost("/register", async (RegisterCommand command, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(command, ct);
            return Results.Accepted(value: new MessageResponse(
                "If this email can be used, your workspace is ready. Check your inbox to verify your email."));
        }).AllowAnonymous();

        auth.MapPost("/login", async (LoginCommand command, ISender sender, HttpResponse response, CancellationToken ct) =>
        {
            var result = await sender.Send(command, ct);
            return SignedIn(result, response);
        }).AllowAnonymous();

        auth.MapPost("/refresh", async (HttpRequest request, HttpResponse response, ISender sender, CancellationToken ct) =>
        {
            try
            {
                var result = await sender.Send(new RefreshCommand(RefreshCookie.Read(request)), ct);
                return SignedIn(result, response);
            }
            catch (Application.Abstractions.UnauthenticatedException)
            {
                RefreshCookie.Clear(response);
                throw;
            }
        }).AllowAnonymous();

        auth.MapPost("/logout", async (HttpRequest request, HttpResponse response, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new LogoutCommand(RefreshCookie.Read(request)), ct);
            RefreshCookie.Clear(response);
            return Results.NoContent();
        }).AllowAnonymous();

        auth.MapPost("/forgot-password", async (ForgotPasswordCommand command, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(command, ct);
            return Results.Accepted(value: new MessageResponse(
                "If an account exists for this email, we've sent a link to reset the password."));
        }).AllowAnonymous();

        auth.MapPost("/reset-password", async (ResetPasswordCommand command, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(command, ct);
            return Results.NoContent();
        }).AllowAnonymous();

        auth.MapPost("/verify-email", async (VerifyEmailCommand command, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(command, ct);
            return Results.NoContent();
        }).AllowAnonymous();

        auth.MapPost("/resend-verification", async (ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ResendVerificationCommand(), ct);
            return Results.Accepted();
        }).RequireAuthorization();

        app.MapGet("/api/v1/me", (ISender sender, CancellationToken ct) => sender.Send(new GetCurrentUserQuery(), ct))
            .WithTags("Me")
            .RequireAuthorization();

        return app;
    }

    private static IResult SignedIn(AuthResult result, HttpResponse response)
    {
        RefreshCookie.Set(response, result.RefreshToken, result.RefreshTokenExpiresAt);
        response.Headers.CacheControl = "no-store";
        return Results.Ok(new AuthResponse(result.AccessToken, result.AccessTokenExpiresAt, result.User));
    }
}
