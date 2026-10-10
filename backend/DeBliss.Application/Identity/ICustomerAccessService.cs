using DeBliss.Application.Identity.Dtos;

namespace DeBliss.Application.Identity;

/// <summary>
/// Self-service identity for the customer website: public registration and the
/// signed-in customer's own profile. Sign-in and sign-out reuse the existing
/// auth endpoints; nothing here duplicates them.
/// </summary>
public interface ICustomerAccessService
{
    /// <summary>
    /// Creates a customer login (the CUSTOMER role, always the only role) and,
    /// in the same unit of work, links an unambiguous front-desk customer record
    /// to it or creates a fresh one. Throws <c>ConflictException</c> when the
    /// email already belongs to an account.
    /// </summary>
    Task<AuthUserDto> RegisterAsync(
        CustomerRegistrationRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// The profile of the customer named by a validated <c>customer_id</c> token
    /// claim — never by a caller-supplied id. Throws <c>NotFoundException</c>
    /// when the record is gone.
    /// </summary>
    Task<CustomerSelfDto> GetProfileAsync(Guid customerId, CancellationToken cancellationToken);
}
