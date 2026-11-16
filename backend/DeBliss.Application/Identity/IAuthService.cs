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

    /// <summary>
    /// Customer sign-in with a mobile number in any accepted format. Only a
    /// customer whose account is active and whose number is verified can sign in
    /// this way; every failure returns <c>null</c>, indistinguishably.
    /// </summary>
    Task<AuthUserDto?> AuthenticateByPhoneAsync(
        string phoneNumber,
        string password,
        CancellationToken cancellationToken);

    /// <summary>The user's current security stamp, embedded in issued tokens.</summary>
    Task<string?> GetSecurityStampAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The safe user view for a known id, or <c>null</c> if the user is gone.</summary>
    Task<AuthUserDto?> GetUserAsync(Guid userId, CancellationToken cancellationToken);
}
