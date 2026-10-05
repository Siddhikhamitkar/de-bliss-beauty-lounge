using DeBliss.Application.Identity.Dtos;

namespace DeBliss.Application.Identity;

public interface IAuthService
{
    /// <summary>
    /// Verifies the credentials and, on success, stamps <c>LastLoginAt</c> before
    /// returning the safe user view. Returns <c>null</c> for every failure mode —
    /// unknown email, wrong password, or a deactivated account — so callers cannot
    /// distinguish which one happened.
    /// </summary>
    Task<AuthUserDto?> AuthenticateAsync(
        string email,
        string password,
        CancellationToken cancellationToken);

    /// <summary>The safe user view for a known id, or <c>null</c> if the user is gone.</summary>
    Task<AuthUserDto?> GetUserAsync(Guid userId, CancellationToken cancellationToken);
}
