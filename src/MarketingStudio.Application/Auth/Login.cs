using FluentValidation;
using MarketingStudio.Application.Abstractions;
using MarketingStudio.Domain.Audit;
using MediatR;

namespace MarketingStudio.Application.Auth;

/// <summary>AUTH-2: sign in. Every failure returns the same generic error.</summary>
public sealed record LoginCommand(string Email, string Password) : IRequest<AuthResult>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class LoginCommandHandler(
    IIdentityService identity,
    IAccessTokenService accessTokens,
    IRefreshTokenService refreshTokens,
    IAuditLog audit,
    IClientInfo client,
    TimeProvider clock) : IRequestHandler<LoginCommand, AuthResult>
{
    public const string GenericFailure = "Invalid email or password, or the account is temporarily locked.";

    public async Task<AuthResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var user = await identity.FindByEmailAsync(request.Email, cancellationToken);

        if (user is null)
        {
            await WriteAuditAsync(AuditActions.SignInFailed, null, null, now, cancellationToken, ("reason", "unknown_email"));
            throw new UnauthenticatedException(GenericFailure);
        }

        var result = await identity.CheckPasswordSignInAsync(user, request.Password, cancellationToken);
        if (result != PasswordSignInResult.Succeeded)
        {
            var action = result == PasswordSignInResult.LockedOut
                ? AuditActions.AccountLockedOut
                : AuditActions.SignInFailed;
            await WriteAuditAsync(action, user.TenantId, user.Id, now, cancellationToken, ("reason", result.ToString()));
            throw new UnauthenticatedException(GenericFailure);
        }

        await identity.RecordSuccessfulSignInAsync(user, cancellationToken);
        var access = accessTokens.CreateAccessToken(user);
        var refresh = await refreshTokens.IssueAsync(user, cancellationToken);
        await WriteAuditAsync(AuditActions.SignInSucceeded, user.TenantId, user.Id, now, cancellationToken);

        return new AuthResult(access.Token, access.ExpiresAt, refresh.Token, refresh.ExpiresAt, user.ToSummary());
    }

    private Task WriteAuditAsync(
        string action,
        string? tenantId,
        string? userId,
        DateTime now,
        CancellationToken cancellationToken,
        params (string Key, string Value)[] metadata) =>
        audit.WriteAsync(
            new AuditEntry
            {
                TenantId = tenantId,
                ActorUserId = userId,
                Action = action,
                TargetType = "user",
                TargetId = userId,
                Metadata = metadata.ToDictionary(m => m.Key, m => m.Value),
                Ip = client.IpAddress,
                CreatedAt = now,
            },
            cancellationToken);
}
