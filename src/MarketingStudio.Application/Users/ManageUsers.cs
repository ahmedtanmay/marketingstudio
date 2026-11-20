using FluentValidation;
using FluentValidation.Results;
using MarketingStudio.Application.Abstractions;
using MarketingStudio.Application.Common;
using MarketingStudio.Domain.Audit;
using MarketingStudio.Domain.Users;
using MediatR;

namespace MarketingStudio.Application.Users;

public sealed record UserListItem(
    string Id,
    string Email,
    string DisplayName,
    string Role,
    string Status,
    bool EmailVerified,
    DateTime? LastLoginAt,
    DateTime CreatedAt);

/// <summary>AUTH-6: members of the caller's tenant.</summary>
public sealed record ListUsersQuery : IRequest<IReadOnlyList<UserListItem>>;

public sealed class ListUsersQueryHandler(IUserRepository users) : IRequestHandler<ListUsersQuery, IReadOnlyList<UserListItem>>
{
    public async Task<IReadOnlyList<UserListItem>> Handle(ListUsersQuery request, CancellationToken cancellationToken) =>
        (await users.ListAsync(cancellationToken))
            .OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(u => new UserListItem(
                u.Id,
                u.Email,
                u.DisplayName,
                u.Roles.FirstOrDefault() ?? Roles.Viewer,
                u.Status.ToString(),
                u.EmailConfirmed,
                u.LastLoginAt,
                u.CreatedAt))
            .ToList();
}

/// <summary>AUTH-6: change a member's role. The last active Tenant Admin cannot be downgraded.</summary>
public sealed record ChangeUserRoleCommand(string UserId, string Role) : IRequest;

public sealed class ChangeUserRoleCommandValidator : AbstractValidator<ChangeUserRoleCommand>
{
    public ChangeUserRoleCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Role).Must(r => Roles.TenantRoles.Contains(r))
            .WithMessage($"Role must be one of: {string.Join(", ", Roles.TenantRoles)}.");
    }
}

public sealed class ChangeUserRoleCommandHandler(
    IUserRepository users,
    IIdentityService identity,
    ITenantContext context,
    IAuditLog audit,
    IClientInfo client,
    TimeProvider clock) : IRequestHandler<ChangeUserRoleCommand>
{
    public async Task Handle(ChangeUserRoleCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.UserId, cancellationToken) ?? throw new NotFoundException();
        var previous = user.Roles.FirstOrDefault() ?? Roles.Viewer;
        if (previous == request.Role)
        {
            return;
        }

        if (previous == Roles.TenantAdmin && user.Status == UserStatus.Active)
        {
            await LastAdminRule.EnsureNotLastAdminAsync(users, cancellationToken);
        }

        user.Roles = [request.Role];
        await identity.UpdateUserAsync(user, cancellationToken);

        await audit.RecordAsync(
            AuditActions.UserRoleChanged, context.TenantId, context.UserId, "user", user.Id, client.IpAddress,
            clock.GetUtcNow().UtcDateTime, cancellationToken,
            new Dictionary<string, string> { ["from"] = previous, ["to"] = request.Role });
    }
}

/// <summary>AUTH-6: deactivate a member. Their refresh tokens are revoked at once; access tokens expire within 15 minutes.</summary>
public sealed record DeactivateUserCommand(string UserId) : IRequest;

public sealed class DeactivateUserCommandHandler(
    IUserRepository users,
    IIdentityService identity,
    IRefreshTokenService refreshTokens,
    ITenantContext context,
    IAuditLog audit,
    IClientInfo client,
    TimeProvider clock) : IRequestHandler<DeactivateUserCommand>
{
    public async Task Handle(DeactivateUserCommand request, CancellationToken cancellationToken)
    {
        if (request.UserId == context.UserId)
        {
            throw new ValidationException([new ValidationFailure("userId", "You can't deactivate your own account.")]);
        }

        var user = await users.GetByIdAsync(request.UserId, cancellationToken) ?? throw new NotFoundException();
        if (user.Status == UserStatus.Deactivated)
        {
            return;
        }

        if (user.Roles.Contains(Roles.TenantAdmin))
        {
            await LastAdminRule.EnsureNotLastAdminAsync(users, cancellationToken);
        }

        user.Status = UserStatus.Deactivated;
        await identity.UpdateUserAsync(user, cancellationToken);
        await refreshTokens.RevokeAllForUserAsync(user.Id, cancellationToken);

        await audit.RecordAsync(
            AuditActions.UserDeactivated, context.TenantId, context.UserId, "user", user.Id, client.IpAddress,
            clock.GetUtcNow().UtcDateTime, cancellationToken);
    }
}

/// <summary>AUTH-6: reactivate a member (needs a free seat).</summary>
public sealed record ReactivateUserCommand(string UserId) : IRequest;

public sealed class ReactivateUserCommandHandler(
    IUserRepository users,
    IIdentityService identity,
    SeatGuard seats,
    ITenantContext context,
    IAuditLog audit,
    IClientInfo client,
    TimeProvider clock) : IRequestHandler<ReactivateUserCommand>
{
    public async Task Handle(ReactivateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.UserId, cancellationToken) ?? throw new NotFoundException();
        if (user.Status == UserStatus.Active)
        {
            return;
        }

        await seats.EnsureSeatAvailableAsync(cancellationToken);
        user.Status = UserStatus.Active;
        await identity.UpdateUserAsync(user, cancellationToken);

        await audit.RecordAsync(
            AuditActions.UserReactivated, context.TenantId, context.UserId, "user", user.Id, client.IpAddress,
            clock.GetUtcNow().UtcDateTime, cancellationToken);
    }
}

internal static class LastAdminRule
{
    public const string Message = "A workspace needs at least one active Tenant Admin. Make someone else an admin first.";

    /// <summary>Call before removing admin rights from an active admin.</summary>
    public static async Task EnsureNotLastAdminAsync(IUserRepository users, CancellationToken cancellationToken)
    {
        if (await users.CountActiveWithRoleAsync(Roles.TenantAdmin, cancellationToken) <= 1)
        {
            throw new ValidationException([new ValidationFailure("role", Message)]);
        }
    }
}
