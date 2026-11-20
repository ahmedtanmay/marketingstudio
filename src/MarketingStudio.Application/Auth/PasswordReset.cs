using FluentValidation;
using FluentValidation.Results;
using MarketingStudio.Application.Abstractions;
using MarketingStudio.Domain.Audit;
using MarketingStudio.Domain.Users;
using MediatR;

namespace MarketingStudio.Application.Auth;

/// <summary>AUTH-4: request a reset link. Always completes the same way (no account enumeration).</summary>
public sealed record ForgotPasswordCommand(string Email) : IRequest;

public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
}

public sealed class ForgotPasswordCommandHandler(
    IIdentityService identity,
    IEmailSender email,
    IAppLinks links,
    IAuditLog audit,
    IClientInfo client,
    TimeProvider clock) : IRequestHandler<ForgotPasswordCommand>
{
    public async Task Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await identity.FindByEmailAsync(request.Email, cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
        {
            return;
        }

        var token = await identity.GeneratePasswordResetTokenAsync(user);
        await email.SendAsync(
            EmailTemplates.ResetPassword(user.Email, user.DisplayName, links.ResetPassword(user.Email, token)),
            cancellationToken);

        await audit.WriteAsync(
            new AuditEntry
            {
                TenantId = user.TenantId,
                ActorUserId = user.Id,
                Action = AuditActions.PasswordResetRequested,
                TargetType = "user",
                TargetId = user.Id,
                Ip = client.IpAddress,
                CreatedAt = clock.GetUtcNow().UtcDateTime,
            },
            cancellationToken);
    }
}

/// <summary>AUTH-4: set a new password with the emailed token; signs the user out everywhere.</summary>
public sealed record ResetPasswordCommand(string Email, string Token, string NewPassword) : IRequest;

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Token).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(10).MaximumLength(128);
    }
}

public sealed class ResetPasswordCommandHandler(
    IIdentityService identity,
    IRefreshTokenService refreshTokens,
    IAuditLog audit,
    IClientInfo client,
    TimeProvider clock) : IRequestHandler<ResetPasswordCommand>
{
    public const string InvalidLink = "This reset link is invalid or has expired. Please request a new one.";

    public async Task Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await identity.FindByEmailAsync(request.Email, cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
        {
            throw InvalidLinkError();
        }

        var outcome = await identity.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (outcome.InvalidToken)
        {
            throw InvalidLinkError();
        }

        if (!outcome.Succeeded)
        {
            throw new ValidationException(outcome.PasswordErrors.Select(
                e => new ValidationFailure(nameof(ResetPasswordCommand.NewPassword), e)));
        }

        await refreshTokens.RevokeAllForUserAsync(user.Id, cancellationToken);
        await audit.WriteAsync(
            new AuditEntry
            {
                TenantId = user.TenantId,
                ActorUserId = user.Id,
                Action = AuditActions.PasswordReset,
                TargetType = "user",
                TargetId = user.Id,
                Ip = client.IpAddress,
                CreatedAt = clock.GetUtcNow().UtcDateTime,
            },
            cancellationToken);
    }

    private static ValidationException InvalidLinkError() =>
        new([new ValidationFailure(nameof(ResetPasswordCommand.Token), InvalidLink)]);
}
