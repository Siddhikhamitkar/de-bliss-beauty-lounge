namespace DeBliss.Application.Identity;

/// <summary>
/// Limits for customer one-time codes, bound from the <c>CustomerOtp</c>
/// configuration section. The defaults are the production policy.
/// </summary>
public sealed class CustomerOtpOptions
{
    public const string SectionName = "CustomerOtp";

    /// <summary>Digits in a code.</summary>
    public int CodeLength { get; set; } = 6;

    /// <summary>How long a sent code can be used.</summary>
    public TimeSpan CodeLifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Wrong guesses allowed on one code before it is dead.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Minimum gap between two codes to the same number for the same purpose.</summary>
    public TimeSpan ResendCooldown { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Codes allowed to one number for one purpose in any rolling hour.</summary>
    public int MaxSendsPerHour { get; set; } = 5;

    /// <summary>How long the token issued for a proven code can be spent.</summary>
    public TimeSpan VerificationTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);
}
