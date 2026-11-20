using FluentValidation;
using FluentValidation.Results;
using MarketingStudio.Application.Abstractions;
using MarketingStudio.Domain.Audit;
using MarketingStudio.Domain.Billing;
using MarketingStudio.Domain.Tenants;
using MarketingStudio.Domain.Users;
using MediatR;

namespace MarketingStudio.Application.Auth;

/// <summary>
/// AUTH-1: sign up and create a workspace. Always completes the same way whether or not the email is
/// already registered, so the endpoint cannot be used to discover accounts. The portal signs the user
/// in right after with the password they just chose.
/// </summary>
public sealed record RegisterCommand(string Name, string Email, string Password, string CompanyName) : IRequest;

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(10).MaximumLength(128);
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(120);
    }
}

public sealed class RegisterCommandHandler(
    IIdentityService identity,
    ITenantRepository tenants,
    ISubscriptionRepository subscriptions,
    IUnitOfWork unitOfWork,
    IEmailSender email,
    IAppLinks links,
    IAuditLog audit,
    IClientInfo client,
    TimeProvider clock) : IRequestHandler<RegisterCommand>
{
    public async Task Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var existing = await identity.FindByEmailAsync(request.Email, cancellationToken);
        if (existing is not null)
        {
            await email.SendAsync(
                EmailTemplates.AccountAlreadyExists(existing.Email, links.SignIn(), links.ForgotPassword()),
                cancellationToken);
            return;
        }

        var passwordErrors = await identity.ValidatePasswordAsync(request.Password);
        if (passwordErrors.Count > 0)
        {
            throw new ValidationException(
                passwordErrors.Select(e => new ValidationFailure(nameof(RegisterCommand.Password), e)));
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var tenant = Tenant.Create(request.CompanyName, now);
        var subscription = Subscription.StartTrial(tenant.Id, now);
        tenant.SubscriptionId = subscription.Id;
        var user = User.Create(tenant.Id, request.Email, request.Name, Roles.TenantAdmin, now);

        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await tenants.AddAsync(tenant, ct);
                await subscriptions.AddAsync(subscription, ct);
                await identity.CreateUserAsync(user, request.Password, ct);
            },
            cancellationToken);

        await audit.WriteAsync(
            new AuditEntry
            {
                TenantId = tenant.Id,
                ActorUserId = user.Id,
                Action = AuditActions.TenantCreated,
                TargetType = "tenant",
                TargetId = tenant.Id,
                Ip = client.IpAddress,
                CreatedAt = now,
            },
            cancellationToken);

        var token = await identity.GenerateEmailConfirmationTokenAsync(user);
        await email.SendAsync(
            EmailTemplates.VerifyEmail(user.Email, user.DisplayName, links.VerifyEmail(user.Id, token)),
            cancellationToken);
    }
}
