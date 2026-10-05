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
}

/// <summary>
/// Internal auth view of a user row: includes the password hash (required for
/// verification) but nothing else sensitive. Never leaves the Application layer.
/// </summary>
public sealed record AuthUserReadModel(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? PasswordHash,
    bool IsActive,
    IReadOnlyList<string> RoleNames,
    Guid? StaffId);
