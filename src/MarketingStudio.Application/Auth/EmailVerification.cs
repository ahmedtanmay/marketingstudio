using FluentValidation;
using FluentValidation.Results;
using MarketingStudio.Application.Abstractions;
using MarketingStudio.Domain.Audit;
using MediatR;

namespace MarketingStudio.Application.Auth;

/// <summary>AUTH-1: confirm the email address from the link in the verification email.</summary>
public sealed record VerifyEmailCommand(string UserId, string Token) : IRequest;

public sealed class VerifyEmailCommandValidator : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Token).NotEmpty().MaximumLength(2048);
    }
}

public sealed class VerifyEmailCommandHandler(
    IIdentityService identity,
    IAuditLog audit,
    IClientInfo client,
    TimeProvider clock) : IRequestHandler<VerifyEmailCommand>
{
    public const string InvalidLink = "This verification link is invalid or has expired.";

    public async Task Handle(VerifyEmailCommand request, CancellationToken cancellationToken)
    {
        if (!await identity.ConfirmEmailAsync(request.UserId, request.Token, cancellationToken))
        {
            throw new ValidationException([new ValidationFailure(nameof(VerifyEmailCommand.Token), InvalidLink)]);
        }

        var user = await identity.FindByIdAsync(request.UserId, cancellationToken);
        await audit.WriteAsync(
            new AuditEntry
            {
                TenantId = user?.TenantId,
                ActorUserId = request.UserId,
                Action = AuditActions.EmailVerified,
                TargetType = "user",
                TargetId = request.UserId,
                Ip = client.IpAddress,
                CreatedAt = clock.GetUtcNow().UtcDateTime,
            },
            cancellationToken);
    }
}

/// <summary>Sends a new verification email to the signed-in user.</summary>
public sealed record ResendVerificationCommand : IRequest;

public sealed class ResendVerificationCommandHandler(
    ITenantContext context,
    IIdentityService identity,
    IEmailSender email,
    IAppLinks links) : IRequestHandler<ResendVerificationCommand>
{
    public async Task Handle(ResendVerificationCommand request, CancellationToken cancellationToken)
    {
        var user = await identity.FindByIdAsync(context.RequireUserId(), cancellationToken)
            ?? throw new UnauthenticatedException();

        if (user.EmailConfirmed)
        {
            return;
        }

        var token = await identity.GenerateEmailConfirmationTokenAsync(user);
        await email.SendAsync(
            EmailTemplates.VerifyEmail(user.Email, user.DisplayName, links.VerifyEmail(user.Id, token)),
            cancellationToken);
    }
}
