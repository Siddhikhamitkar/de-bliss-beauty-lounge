namespace DeBliss.Application.Identity.Dtos;

/// <summary>"activation" (first-time activation or a new sign-up) or "password-reset".</summary>
public sealed record RequestCustomerCodeRequest(string? PhoneNumber, string? Purpose);

public sealed record VerifyCustomerCodeRequest(string? PhoneNumber, string? Purpose, string? Code);

/// <summary>
/// Proof that the caller owns the number. <see cref="NeedsProfile"/> is true for
/// an activation with no customer record behind the number yet — the activation
/// call must then carry the customer's name.
/// </summary>
public sealed record CustomerCodeVerifiedDto(string VerificationToken, DateTimeOffset ExpiresAt, bool NeedsProfile);

public sealed record ActivateCustomerAccountRequest(
    string? VerificationToken,
    string? Password,
    string? FirstName,
    string? LastName,
    string? Email);

public sealed record ResetCustomerPasswordRequest(string? VerificationToken, string? Password);

public sealed record UpdateCustomerProfileRequest(string? FirstName, string? LastName, string? Email);

public sealed record RequestMobileChangeRequest(string? PhoneNumber);

public sealed record ConfirmMobileChangeRequest(string? PhoneNumber, string? Code);
