using MarketingStudio.Domain.Users;
using MarketingStudio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;

namespace MarketingStudio.Infrastructure.Identity;

/// <summary>
/// ASP.NET Core Identity store on MongoDB (Decision Register D5: custom store, no third-party package).
/// The email address is the user name. Sign-in needs cross-tenant lookup by email, so this store is
/// the one intentional exception to tenant-scoped repositories; it is not exposed to features.
/// </summary>
internal sealed class MongoUserStore(MongoContext context, MongoSessionAccessor sessions) :
    IUserPasswordStore<User>,
    IUserEmailStore<User>,
    IUserSecurityStampStore<User>,
    IUserLockoutStore<User>
{
    private IMongoCollection<User> Users => context.Users;

    public void Dispose()
    {
    }

    // ---- IUserStore ----
    public Task<string> GetUserIdAsync(User user, CancellationToken cancellationToken) => Task.FromResult(user.Id);

    public Task<string?> GetUserNameAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.Email);

    public Task SetUserNameAsync(User user, string? userName, CancellationToken cancellationToken)
    {
        user.Email = userName ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.NormalizedEmail);

    public Task SetNormalizedUserNameAsync(User user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedEmail = normalizedName ?? string.Empty;
        return Task.CompletedTask;
    }

    public async Task<IdentityResult> CreateAsync(User user, CancellationToken cancellationToken)
    {
        try
        {
            var session = sessions.Session;
            if (session is null)
            {
                await Users.InsertOneAsync(user, cancellationToken: cancellationToken);
            }
            else
            {
                await Users.InsertOneAsync(session, user, cancellationToken: cancellationToken);
            }

            return IdentityResult.Success;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return IdentityResult.Failed(new IdentityErrorDescriber().DuplicateEmail(user.Email));
        }
    }

    public async Task<IdentityResult> UpdateAsync(User user, CancellationToken cancellationToken)
    {
        var expectedStamp = user.ConcurrencyStamp;
        user.ConcurrencyStamp = Guid.NewGuid().ToString("N");

        var filter = Builders<User>.Filter.Eq(u => u.Id, user.Id)
            & Builders<User>.Filter.Eq(u => u.ConcurrencyStamp, expectedStamp);
        var result = await Users.ReplaceOneAsync(filter, user, cancellationToken: cancellationToken);

        if (result.MatchedCount == 0)
        {
            user.ConcurrencyStamp = expectedStamp;
            return IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());
        }

        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(User user, CancellationToken cancellationToken)
    {
        await Users.DeleteOneAsync(u => u.Id == user.Id, cancellationToken);
        return IdentityResult.Success;
    }

    public async Task<User?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        await Users.Find(u => u.Id == userId).FirstOrDefaultAsync(cancellationToken);

    public async Task<User?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        await Users.Find(u => u.NormalizedEmail == normalizedUserName).FirstOrDefaultAsync(cancellationToken);

    // ---- IUserPasswordStore ----
    public Task SetPasswordHashAsync(User user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PasswordHash is not null);

    // ---- IUserEmailStore ----
    public Task SetEmailAsync(User user, string? email, CancellationToken cancellationToken)
    {
        user.Email = email ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.Email);

    public Task<bool> GetEmailConfirmedAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(user.EmailConfirmed);

    public Task SetEmailConfirmedAsync(User user, bool confirmed, CancellationToken cancellationToken)
    {
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        FindByNameAsync(normalizedEmail, cancellationToken);

    public Task<string?> GetNormalizedEmailAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.NormalizedEmail);

    public Task SetNormalizedEmailAsync(User user, string? normalizedEmail, CancellationToken cancellationToken)
    {
        user.NormalizedEmail = normalizedEmail ?? string.Empty;
        return Task.CompletedTask;
    }

    // ---- IUserSecurityStampStore ----
    public Task SetSecurityStampAsync(User user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.SecurityStamp);

    // ---- IUserLockoutStore ----
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockoutEnd is null
            ? (DateTimeOffset?)null
            : new DateTimeOffset(DateTime.SpecifyKind(user.LockoutEnd.Value, DateTimeKind.Utc)));

    public Task SetLockoutEndDateAsync(User user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        user.LockoutEnd = lockoutEnd?.UtcDateTime;
        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(User user, CancellationToken cancellationToken)
    {
        user.AccessFailedCount++;
        return Task.FromResult(user.AccessFailedCount);
    }

    public Task ResetAccessFailedCountAsync(User user, CancellationToken cancellationToken)
    {
        user.AccessFailedCount = 0;
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(user.AccessFailedCount);

    public Task<bool> GetLockoutEnabledAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockoutEnabled);

    public Task SetLockoutEnabledAsync(User user, bool enabled, CancellationToken cancellationToken)
    {
        user.LockoutEnabled = enabled;
        return Task.CompletedTask;
    }
}
