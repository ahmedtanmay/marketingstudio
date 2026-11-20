using MarketingStudio.Application.Users;
using MarketingStudio.Domain.Users;
using MediatR;

namespace MarketingStudio.Api.Endpoints;

public sealed record ChangeRoleRequest(string Role);

public sealed record CreateInviteRequest(string Email, string Role);

public sealed record AcceptInviteRequest(string Token, string Name, string Password);

internal static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        // ---- AUTH-6: members ----
        var users = app.MapGroup("/api/v1/users").WithTags("Users").RequireAuthorization(Permissions.UsersManage);

        users.MapGet("/", (ISender sender, CancellationToken ct) => sender.Send(new ListUsersQuery(), ct));

        users.MapPut("/{userId}/role", async (string userId, ChangeRoleRequest body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ChangeUserRoleCommand(userId, body.Role), ct);
            return Results.NoContent();
        });

        users.MapPost("/{userId}/deactivate", async (string userId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeactivateUserCommand(userId), ct);
            return Results.NoContent();
        });

        users.MapPost("/{userId}/reactivate", async (string userId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ReactivateUserCommand(userId), ct);
            return Results.NoContent();
        });

        // ---- AUTH-5: invites (admin side) ----
        var invites = app.MapGroup("/api/v1/invites").WithTags("Invites");

        invites.MapGet("/", (ISender sender, CancellationToken ct) => sender.Send(new ListInvitesQuery(), ct))
            .RequireAuthorization(Permissions.UsersManage);

        invites.MapPost("/", async (CreateInviteRequest body, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new CreateInviteCommand(body.Email, body.Role), ct)))
            .RequireAuthorization(Permissions.UsersManage);

        invites.MapPost("/{inviteId}/resend", (string inviteId, ISender sender, CancellationToken ct) =>
            sender.Send(new ResendInviteCommand(inviteId), ct))
            .RequireAuthorization(Permissions.UsersManage);

        invites.MapDelete("/{inviteId}", async (string inviteId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new RevokeInviteCommand(inviteId), ct);
            return Results.NoContent();
        }).RequireAuthorization(Permissions.UsersManage);

        // ---- AUTH-5: invites (invitee side, public; the token is the credential) ----
        var publicInvites = app.MapGroup("/api/v1/invites").WithTags("Invites")
            .RequireRateLimiting(AuthEndpoints.AuthRateLimitPolicy)
            .AllowAnonymous();

        publicInvites.MapGet("/lookup", (string token, ISender sender, CancellationToken ct) =>
            sender.Send(new GetInviteQuery(token), ct));

        publicInvites.MapPost("/accept", async (AcceptInviteRequest body, ISender sender, HttpResponse response, CancellationToken ct) =>
        {
            var result = await sender.Send(new AcceptInviteCommand(body.Token, body.Name, body.Password), ct);
            RefreshCookie.Set(response, result.RefreshToken, result.RefreshTokenExpiresAt);
            response.Headers.CacheControl = "no-store";
            return Results.Ok(new AuthResponse(result.AccessToken, result.AccessTokenExpiresAt, result.User));
        });

        return app;
    }
}
