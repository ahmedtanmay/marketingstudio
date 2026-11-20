using System.Net;
using FluentValidation;
using FluentValidation.Results;
using MarketingStudio.Application.Abstractions;
using MarketingStudio.Application.Auth;
using MarketingStudio.Application.Common;
using MarketingStudio.Domain.Audit;
using MarketingStudio.Domain.Users;
using MediatR;

namespace MarketingStudio.Application.Users;

public sealed record InviteListItem(string Id, string Email, string Role, DateTime ExpiresAt, DateTime LastSentAt);

/// <summary>AUTH-5: invite someone by email with a role. Re-inviting the same email renews the pending invite.</summary>
public sealed record CreateInviteCommand(string Email, string Role) : IRequest<InviteListItem>;

public sealed class CreateInviteCommandValidator : AbstractValidator<CreateInviteCommand>
{
    public CreateInviteCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Role).Must(r => Roles.TenantRoles.Contains(r))
            .WithMessage($"Role must be one of: {string.Join(", ", Roles.TenantRoles)}.");
    }
}

public sealed class CreateInviteCommandHandler(
    IInviteRepository invites,
    IUserRepository users,
    SeatGuard seats,
    ITenantContext context,
    ITenantRepository tenants,
    IEmailSender email,
    IAppLinks links,
    IAuditLog audit,
    IClientInfo client,
    TimeProvider clock) : IRequestHandler<CreateInviteCommand, InviteListItem>
{
    public async Task<InviteListItem> Handle(CreateInviteCommand request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var normalized = Invite.Normalize(request.Email);

        var members = await users.ListAsync(cancellationToken);
        if (members.Any(u => u.NormalizedEmail == normalized))
        {
            throw new ValidationException([new ValidationFailure("email", "This person is already a member of your workspace.")]);
        }

        var (raw, hash) = SecureToken.Create();
        var invite = await invites.FindOpenByEmailAsync(normalized, now, cancellationToken);
        if (invite is null)
        {
            await seats.EnsureSeatAvailableAsync(cancellationToken);
            invite = Invite.Create(context.RequireTenantId(), request.Email, request.Role, context.RequireUserId(), hash, now);
            await invites.AddAsync(invite, cancellationToken);
        }
        else
        {
            invite.Role = request.Role;
            invite.Renew(hash, now);
            await invites.UpdateAsync(invite, cancellationToken);
        }

        await InviteEmails.SendAsync(invite, raw, tenants, email, links, cancellationToken);
        await audit.RecordAsync(
            AuditActions.UserInvited, invite.TenantId, context.UserId, "invite", invite.Id, client.IpAddress, now,
            cancellationToken, new Dictionary<string, string> { ["role"] = invite.Role });

        return invite.ToListItem();
    }
}

public sealed record ListInvitesQuery : IRequest<IReadOnlyList<InviteListItem>>;

public sealed class ListInvitesQueryHandler(IInviteRepository invites, TimeProvider clock)
    : IRequestHandler<ListInvitesQuery, IReadOnlyList<InviteListItem>>
{
    public async Task<IReadOnlyList<InviteListItem>> Handle(ListInvitesQuery request, CancellationToken cancellationToken) =>
        (await invites.ListOpenAsync(clock.GetUtcNow().UtcDateTime, cancellationToken))
            .OrderByDescending(i => i.LastSentAt)
            .Select(i => i.ToListItem())
            .ToList();
}

public sealed record ResendInviteCommand(string InviteId) : IRequest<InviteListItem>;

public sealed class ResendInviteCommandHandler(
    IInviteRepository invites,
    ITenantContext context,
    ITenantRepository tenants,
    IEmailSender email,
    IAppLinks links,
    IAuditLog audit,
    IClientInfo client,
    TimeProvider clock) : IRequestHandler<ResendInviteCommand, InviteListItem>
{
    public async Task<InviteListItem> Handle(ResendInviteCommand request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var invite = await invites.GetByIdAsync(request.InviteId, cancellationToken);
        if (invite is null || invite.Status != InviteStatus.Pending)
        {
            throw new NotFoundException();
        }

        var (raw, hash) = SecureToken.Create();
        invite.Renew(hash, now);
        await invites.UpdateAsync(invite, cancellationToken);
        await InviteEmails.SendAsync(invite, raw, tenants, email, links, cancellationToken);
        await audit.RecordAsync(
            AuditActions.InviteResent, invite.TenantId, context.UserId, "invite", invite.Id, client.IpAddress, now, cancellationToken);

        return invite.ToListItem();
    }
}

public sealed record RevokeInviteCommand(string InviteId) : IRequest;

public sealed class RevokeInviteCommandHandler(
    IInviteRepository invites,
    ITenantContext context,
    IAuditLog audit,
    IClientInfo client,
    TimeProvider clock) : IRequestHandler<RevokeInviteCommand>
{
    public async Task Handle(RevokeInviteCommand request, CancellationToken cancellationToken)
    {
        var invite = await invites.GetByIdAsync(request.InviteId, cancellationToken) ?? throw new NotFoundException();
        if (invite.Status != InviteStatus.Pending)
        {
            return;
        }

        invite.Status = InviteStatus.Revoked;
        await invites.UpdateAsync(invite, cancellationToken);
        await audit.RecordAsync(
            AuditActions.InviteRevoked, invite.TenantId, context.UserId, "invite", invite.Id, client.IpAddress,
            clock.GetUtcNow().UtcDateTime, cancellationToken);
    }
}

public sealed record InviteDetails(string WorkspaceName, string Email, string Role);

/// <summary>Public: what the accept-invite page shows. Invalid, used, revoked and expired links all look the same.</summary>
public sealed record GetInviteQuery(string Token) : IRequest<InviteDetails>;

public sealed class GetInviteQueryHandler(IInviteRepository invites, TimeProvider clock)
    : IRequestHandler<GetInviteQuery, InviteDetails>
{
    public const string InvalidLink = "This invitation link is invalid or has expired. Ask your admin to send a new one.";

    public async Task<InviteDetails> Handle(GetInviteQuery request, CancellationToken cancellationToken)
    {
        var invite = await invites.FindByTokenHashAsync(SecureToken.Hash(request.Token), cancellationToken);
        if (invite is null || !invite.IsOpen(clock.GetUtcNow().UtcDateTime))
        {
            throw new NotFoundException(InvalidLink);
        }

        var workspace = await invites.GetTenantNameAsync(invite.TenantId, cancellationToken) ?? "your team";
        return new InviteDetails(workspace, invite.Email, invite.Role);
    }
}

/// <summary>
/// Public: accept an invite by choosing a name and password. Creates the user in the inviting tenant
/// (email counts as verified — they clicked the emailed link) and signs them in.
/// </summary>
public sealed record AcceptInviteCommand(string Token, string Name, string Password) : IRequest<AuthResult>;

public sealed class AcceptInviteCommandValidator : AbstractValidator<AcceptInviteCommand>
{
    public AcceptInviteCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(10).MaximumLength(128);
    }
}

public sealed class AcceptInviteCommandHandler(
    IInviteRepository invites,
    IIdentityService identity,
    IUnitOfWork unitOfWork,
    IAccessTokenService accessTokens,
    IRefreshTokenService refreshTokens,
    IAuditLog audit,
    IClientInfo client,
    TimeProvider clock) : IRequestHandler<AcceptInviteCommand, AuthResult>
{
    public const string AlreadyRegistered =
        "An account with this email already exists. One email can belong to one workspace for now — "
        + "sign in with it, or ask your admin to invite a different address.";

    public async Task<AuthResult> Handle(AcceptInviteCommand request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var invite = await invites.FindByTokenHashAsync(SecureToken.Hash(request.Token), cancellationToken);
        if (invite is null || !invite.IsOpen(now))
        {
            throw new NotFoundException(GetInviteQueryHandler.InvalidLink);
        }

        if (await identity.FindByEmailAsync(invite.Email, cancellationToken) is not null)
        {
            throw new ValidationException([new ValidationFailure("email", AlreadyRegistered)]);
        }

        var passwordErrors = await identity.ValidatePasswordAsync(request.Password);
        if (passwordErrors.Count > 0)
        {
            throw new ValidationException(passwordErrors.Select(e => new ValidationFailure("password", e)));
        }

        var user = User.Create(invite.TenantId, invite.Email, request.Name, invite.Role, now);
        user.EmailConfirmed = true;
        invite.Accept(user.Id, now);

        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await identity.CreateUserAsync(user, request.Password, ct);
                await invites.SaveAcceptedAsync(invite, ct);
            },
            cancellationToken);

        await audit.RecordAsync(
            AuditActions.InviteAccepted, user.TenantId, user.Id, "invite", invite.Id, client.IpAddress, now, cancellationToken);

        await identity.RecordSuccessfulSignInAsync(user, cancellationToken);
        var access = accessTokens.CreateAccessToken(user);
        var refresh = await refreshTokens.IssueAsync(user, cancellationToken);
        return new AuthResult(access.Token, access.ExpiresAt, refresh.Token, refresh.ExpiresAt, user.ToSummary());
    }
}

internal static class InviteEmails
{
    public static async Task SendAsync(
        Invite invite,
        string rawToken,
        ITenantRepository tenants,
        IEmailSender email,
        IAppLinks links,
        CancellationToken cancellationToken)
    {
        var workspace = (await tenants.GetCurrentAsync(cancellationToken))?.Name ?? "a workspace";
        var link = links.AcceptInvite(rawToken);
        string e(string value) => WebUtility.HtmlEncode(value);

        var subject = $"You're invited to join {workspace} on AI Marketing Studio";
        var text =
            $"Hi,\n\nYou've been invited to join {workspace} on AI Marketing Studio as {RoleNames.Display(invite.Role)}.\n\n"
            + $"Accept invitation: {link}\n\nThe link expires in 7 days.";
        var html =
            $"<p>Hi,</p><p>You've been invited to join <strong>{e(workspace)}</strong> on AI Marketing Studio as "
            + $"{e(RoleNames.Display(invite.Role))}.</p><p><a href=\"{e(link)}\">Accept invitation</a></p>"
            + "<p style=\"color:#666\">The link expires in 7 days.</p>";

        await email.SendAsync(new EmailMessage(invite.Email, subject, html, text), cancellationToken);
    }
}

internal static class InviteMapping
{
    public static InviteListItem ToListItem(this Invite invite) =>
        new(invite.Id, invite.Email, invite.Role, invite.ExpiresAt, invite.LastSentAt);
}

public static class RoleNames
{
    public static string Display(string role) => role switch
    {
        Roles.TenantAdmin => "Tenant Admin",
        Roles.MarketingManager => "Marketing Manager",
        Roles.ContentManager => "Content Manager",
        Roles.Viewer => "Viewer",
        Roles.SuperAdmin => "Super Admin",
        _ => role,
    };
}
