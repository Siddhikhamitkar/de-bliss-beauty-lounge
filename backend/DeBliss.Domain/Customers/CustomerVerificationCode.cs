using DeBliss.Domain.Common;

namespace DeBliss.Domain.Customers;

/// <summary>
/// One one-time code sent to a mobile number, and — once the code is proven —
/// the short-lived single-use token that lets the caller finish the step the code
/// was for (activate, reset the password, change the number). Only hashes are
/// stored; the plaintext code and token exist only in the message and the
/// response that carried them.
/// </summary>
public class CustomerVerificationCode : Entity
{
    public CustomerVerificationPurpose Purpose { get; set; }

    /// <summary>E.164 number the code was sent to (the new number for a change).</summary>
    public string PhoneNumber { get; set; } = null!;

    /// <summary>The customer the code is about, when one exists (null for a new sign-up).</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>Salted SHA-256 of the code: <c>base64(salt):base64(hash)</c>.</summary>
    public string CodeHash { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? VerifiedAt { get; set; }

    /// <summary>SHA-256 of the verification token issued when the code was proven.</summary>
    public string? TokenHash { get; set; }

    public DateTimeOffset? TokenExpiresAt { get; set; }

    /// <summary>Set when the token was spent; a consumed row can never be used again.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }
}

public enum CustomerVerificationPurpose
{
    /// <summary>First-time activation of a front-desk record, or a brand-new sign-up.</summary>
    Activation = 0,
    PasswordReset = 1,
    ChangeMobile = 2
}
