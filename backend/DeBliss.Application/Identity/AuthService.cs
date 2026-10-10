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

        if (user is null || !user.IsActive || string.IsNullOrEmpty(user.PasswordHash))
        {
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
