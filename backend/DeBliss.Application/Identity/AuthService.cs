using DeBliss.Application.Identity.Dtos;
using DeBliss.Application.Identity.Ports;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace DeBliss.Application.Identity;

/// <summary>
/// Credential verification using ASP.NET Core Identity's <see cref="PasswordHasher{TUser}"/>
/// (PBKDF2-SHA256, per-password salt, randomised iteration count) — no custom or
/// hand-rolled hashing anywhere in the codebase.
/// </summary>
public sealed class AuthService(
    IUserRepository users,
    TimeProvider clock,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly PasswordHasher<AuthUserReadModel> _hasher = new();

    public async Task<AuthUserDto?> AuthenticateAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(email, cancellationToken);

        return await VerifyAsync(user, password, cancellationToken);
    }

    public async Task<AuthUserDto?> AuthenticateByPhoneAsync(
        string phoneNumber,
        string password,
        CancellationToken cancellationToken)
    {
        var canonical = Customers.CustomerPhone.ToCanonical(phoneNumber);

        var user = canonical is null
            ? null
            : await users.FindByVerifiedCustomerPhoneAsync(canonical, cancellationToken);

        // Mobile sign-in is a customer feature only: a team login that happens
        // to share a number never authenticates this way.
        if (user is not null && !user.RoleNames.Contains(RoleNames.Customer))
        {
            user = null;
        }

        return await VerifyAsync(user, password, cancellationToken);
    }

    public async Task<string?> GetSecurityStampAsync(Guid userId, CancellationToken cancellationToken)
    {
        var state = await users.GetSessionStateAsync(userId, cancellationToken);

        return state?.SecurityStamp;
    }

    private async Task<AuthUserDto?> VerifyAsync(
        AuthUserReadModel? user,
        string password,
        CancellationToken cancellationToken)
    {
        if (user is null || !user.IsActive || string.IsNullOrEmpty(user.PasswordHash))
        {
            // Spend the same hashing work as a real check, so response time does
            // not reveal whether the account exists.
            _hasher.VerifyHashedPassword(TimingDummy.User, TimingDummy.Hash, password);
            return null;
        }

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);

        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            logger.LogInformation("Password hash for user {UserId} needs rehashing on next password change.", user.Id);
        }

        await users.MarkLoggedInAsync(user.Id, clock.GetUtcNow(), cancellationToken);

        return ToDto(user);
    }

    public async Task<AuthUserDto?> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId, cancellationToken);

        return user is null ? null : ToDto(user);
    }

    private static class TimingDummy
    {
        public static readonly AuthUserReadModel User =
            new(Guid.Empty, string.Empty, string.Empty, null, null, false, [], null, null);

        public static readonly string Hash = HashPassword(Guid.NewGuid().ToString());
    }

    /// <summary>Hashing helper for seeders/tests — the only other caller of the hasher besides verification.</summary>
    public static string HashPassword(string password)
    {
        var dummy = new AuthUserReadModel(
            Guid.Empty, string.Empty, string.Empty, string.Empty, null, true, [], null, null);

        return new PasswordHasher<AuthUserReadModel>().HashPassword(dummy, password);
    }

    private static AuthUserDto ToDto(AuthUserReadModel user) =>
        new(user.Id, user.FirstName, user.LastName, user.Email, user.RoleNames, user.StaffId, user.CustomerId);
}
