using DeBliss.Domain.Customers;

namespace DeBliss.Application.Customers;

public static class CustomerPhone
{
    public const string InvalidMessage = "Enter a valid mobile number, for example +971 50 123 4567.";

    /// <summary>
    /// The digits of <paramref name="phoneNumber"/> with all formatting stripped.
    /// Used for searching ("501234567" finds "+971 50 123 4567"); identity
    /// comparisons use <see cref="ToCanonical"/> instead.
    /// </summary>
    public static string? Normalize(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return null;
        }

        var digits = string.Concat(phoneNumber.Where(char.IsAsciiDigit));

        return digits.Length == 0 ? null : digits;
    }

    /// <summary>
    /// The canonical E.164 identity key (<see cref="MobileNumber.Normalize"/>), or
    /// null when the input is not a usable phone number.
    /// </summary>
    public static string? ToCanonical(string? phoneNumber) => MobileNumber.Normalize(phoneNumber);
}
