using DeBliss.Domain.Identity;

namespace DeBliss.Application.Identity.Ports;

/// <summary>
/// Everything authentication needs from the User/Role/UserRole schema — nothing
/// more, so the read surface can never leak sensitive columns.
/// </summary>
public interface IUserRepository
{
    Task<AuthUserReadModel?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    Task<AuthUserReadModel?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task MarkLoggedInAsync(Guid userId, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>
    /// Id of the role with the given name, or null when the platform has not been
    /// seeded with it. Registration refuses to run against an unseeded role store.
    /// </summary>
    Task<Guid?> GetRoleIdAsync(string roleName, CancellationToken cancellationToken);

    /// <summary>
    /// Tracks a new user — including any role assignments — in the current unit of
    /// work. Nothing is written until the application service saves.
    /// </summary>
    void Add(User user);

    /// <summary>
    /// The login linked to the customer holding this canonical mobile number,
    /// but only when that customer's account is active and the number verified —
    /// the only state in which mobile sign-in is allowed.
    /// </summary>
    Task<AuthUserReadModel?> FindByVerifiedCustomerPhoneAsync(string canonicalPhone, CancellationToken cancellationToken);

    /// <summary>The user row, tracked, for password and profile changes.</summary>
    Task<User?> GetTrackedAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>True when another login (not <paramref name="exceptUserId"/>) already uses this email.</summary>
    Task<bool> IsEmailTakenAsync(string email, Guid? exceptUserId, CancellationToken cancellationToken);

    /// <summary>The user's current security stamp and active flag; null when the user does not exist.</summary>
    Task<(string? SecurityStamp, bool IsActive)?> GetSessionStateAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// Internal auth view of a user row: includes the password hash (required for
/// verification) but nothing else sensitive. Never leaves the Application layer.
/// </summary>
public sealed record AuthUserReadModel(
    Guid Id,
    string FirstName,
    string LastName,
    string? Email,
    string? PasswordHash,
    bool IsActive,
    IReadOnlyList<string> RoleNames,
    Guid? StaffId,
    Guid? CustomerId = null,
    string? SecurityStamp = null);
