using MarketingStudio.Application.Abstractions;
using MarketingStudio.Domain.Audit;
using MarketingStudio.Domain.Users;
using MediatR;

namespace MarketingStudio.Application.Auth;

/// <summary>AUTH-3: exchange the refresh-token cookie for a new access token (and a rotated refresh token).</summary>
public sealed record RefreshCommand(string? RefreshToken) : IRequest<AuthResult>;

public sealed class RefreshCommandHandler(
    IIdentityService identity,
    IAccessTokenService accessTokens,
    IRefreshTokenService refreshTokens,
    IAuditLog audit,
    IClientInfo client,
    TimeProvider clock) : IRequestHandler<RefreshCommand, AuthResult>
{
    public async Task<AuthResult> Handle(RefreshCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw new UnauthenticatedException();
        }

        var rotation = await refreshTokens.RotateAsync(request.RefreshToken, cancellationToken);
        if (!rotation.Succeeded)
        {
            if (rotation.Failure == RefreshFailure.ReuseDetected)
            {
                await audit.WriteAsync(
                    new AuditEntry
                    {
                        TenantId = rotation.TenantId,
                        ActorUserId = rotation.UserId,
                        Action = AuditActions.RefreshTokenReuse,
                        TargetType = "user",
                        TargetId = rotation.UserId,
                        Ip = client.IpAddress,
                        CreatedAt = clock.GetUtcNow().UtcDateTime,
                    },
                    cancellationToken);
            }

            throw new UnauthenticatedException();
        }

        var user = await identity.FindByIdAsync(rotation.UserId!, cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
        {
            await refreshTokens.RevokeAllForUserAsync(rotation.UserId!, cancellationToken);
            throw new UnauthenticatedException();
        }

        var access = accessTokens.CreateAccessToken(user);
        var refresh = rotation.NewToken!;
        return new AuthResult(access.Token, access.ExpiresAt, refresh.Token, refresh.ExpiresAt, user.ToSummary());
    }
}

/// <summary>AUTH-3: sign out — revokes the refresh-token family behind the cookie.</summary>
public sealed record LogoutCommand(string? RefreshToken) : IRequest;

public sealed class LogoutCommandHandler(
    IRefreshTokenService refreshTokens,
    ITenantContext context,
    IAuditLog audit,
    IClientInfo client,
    TimeProvider clock) : IRequestHandler<LogoutCommand>
{
    public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            await refreshTokens.RevokeFamilyAsync(request.RefreshToken, cancellationToken);
        }

        await audit.WriteAsync(
            new AuditEntry
            {
                TenantId = context.TenantId,
                ActorUserId = context.UserId,
                Action = AuditActions.SignedOut,
                TargetType = "user",
                TargetId = context.UserId,
                Ip = client.IpAddress,
                CreatedAt = clock.GetUtcNow().UtcDateTime,
            },
            cancellationToken);
    }
}
