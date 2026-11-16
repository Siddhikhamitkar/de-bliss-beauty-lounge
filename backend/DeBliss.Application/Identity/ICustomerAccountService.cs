using DeBliss.Application.Identity.Dtos;

namespace DeBliss.Application.Identity;

/// <summary>
/// Mobile-number ownership for customers: one-time codes, first-time activation
/// (including brand-new sign-ups), password reset, and changing the number.
/// </summary>
public interface ICustomerAccountService
{
    /// <summary>
    /// Sends a code when one is due. Deliberately silent about whether the number
    /// belongs to a customer: every well-formed request gets the same answer.
    /// </summary>
    Task RequestCodeAsync(RequestCustomerCodeRequest request, CancellationToken cancellationToken);

    Task<CustomerCodeVerifiedDto> VerifyCodeAsync(VerifyCustomerCodeRequest request, CancellationToken cancellationToken);

    Task<AuthUserDto> ActivateAsync(ActivateCustomerAccountRequest request, CancellationToken cancellationToken);

    Task ResetPasswordAsync(ResetCustomerPasswordRequest request, CancellationToken cancellationToken);

    Task<CustomerSelfDto> UpdateProfileAsync(Guid customerId, UpdateCustomerProfileRequest request, CancellationToken cancellationToken);

    Task RequestMobileChangeAsync(Guid customerId, RequestMobileChangeRequest request, CancellationToken cancellationToken);

    Task<CustomerSelfDto> ConfirmMobileChangeAsync(Guid customerId, ConfirmMobileChangeRequest request, CancellationToken cancellationToken);
}
