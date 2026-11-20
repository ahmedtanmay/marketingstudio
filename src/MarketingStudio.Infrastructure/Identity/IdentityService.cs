using MarketingStudio.Application.Abstractions;
using MarketingStudio.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace MarketingStudio.Infrastructure.Identity;

internal sealed class IdentityService(UserManager<User> users, TimeProvider clock) : IIdentityService
{
    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        users.FindByEmailAsync(email);

    public Task<User?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        users.FindByIdAsync(userId);

    public async Task<IReadOnlyList<string>> ValidatePasswordAsync(string password)
    {
        var probe = new User();
        var errors = new List<string>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, probe, password);
            errors.AddRange(result.Errors.Select(e => e.Description));
        }

        return errors;
    }

    public async Task CreateUserAsync(User user, string password, CancellationToken cancellationToken)
    {
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "User creation failed: " + string.Join("; ", result.Errors.Select(e => e.Code)));
        }
    }

    public async Task UpdateUserAsync(User user, CancellationToken cancellationToken)
    {
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "User update failed: " + string.Join("; ", result.Errors.Select(e => e.Code)));
        }
    }

    /// <summary>Same semantics as SignInManager.CheckPasswordSignInAsync with lockout enabled.</summary>
    public async Task<PasswordSignInResult> CheckPasswordSignInAsync(
        User user,
        string password,
        CancellationToken cancellationToken)
    {
        if (user.Status != UserStatus.Active)
        {
            return PasswordSignInResult.NotAllowed;
        }

        if (await users.IsLockedOutAsync(user))
        {
            return PasswordSignInResult.LockedOut;
        }

        if (await users.CheckPasswordAsync(user, password))
        {
            if (await users.GetAccessFailedCountAsync(user) > 0)
            {
                await users.ResetAccessFailedCountAsync(user);
            }

            return PasswordSignInResult.Succeeded;
        }

        await users.AccessFailedAsync(user);
        return await users.IsLockedOutAsync(user) ? PasswordSignInResult.LockedOut : PasswordSignInResult.Failed;
    }

    public async Task RecordSuccessfulSignInAsync(User user, CancellationToken cancellationToken)
    {
        user.LastLoginAt = clock.GetUtcNow().UtcDateTime;
        await users.UpdateAsync(user);
    }

    public Task<string> GenerateEmailConfirmationTokenAsync(User user) =>
        users.GenerateEmailConfirmationTokenAsync(user);

    public async Task<bool> ConfirmEmailAsync(string userId, string token, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            return false;
        }

        if (user.EmailConfirmed)
        {
            return true;
        }

        var result = await users.ConfirmEmailAsync(user, token);
        return result.Succeeded;
    }

    public Task<string> GeneratePasswordResetTokenAsync(User user) =>
        users.GeneratePasswordResetTokenAsync(user);

    public async Task<ResetPasswordOutcome> ResetPasswordAsync(User user, string token, string newPassword)
    {
        var result = await users.ResetPasswordAsync(user, token, newPassword);
        if (result.Succeeded)
        {
            return ResetPasswordOutcome.Success;
        }

        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
        {
            return ResetPasswordOutcome.BadToken;
        }

        return new ResetPasswordOutcome(false, false, result.Errors.Select(e => e.Description).ToList());
    }
}
